using System.Data;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinisys.Infrastructure.LaboratoryRequests;

public sealed class RequestWorkflowStore(ClinisysDbContext db, TimeProvider clock,
    IAnalysisResultActorProvider actorProvider) : IRequestWorkflowStore
{
    public async Task<RequestWorkflowDto> TransitionAsync(Guid requestId, RequestWorkflowAction action, byte[] version, CancellationToken ct)
    {
        try { return await WriteAsync(requestId, action, version, ct); }
        catch (Exception ex) when (SqlNumber(ex) == 1205) { throw LaboratoryRequestException.WorkflowConflict(); }
    }

    private async Task<RequestWorkflowDto> WriteAsync(Guid requestId, RequestWorkflowAction action, byte[] version, CancellationToken ct)
    {
        if (version is null || version.Length != 8 || requestId == Guid.Empty || !Enum.IsDefined(action))
            throw LaboratoryRequestException.Validation("request");
        if (System.Transactions.Transaction.Current is not null || db.Database.CreateExecutionStrategy().RetriesOnFailure)
            throw new InvalidOperationException("Workflow writes require explicit transactions without automatic retries.");
        var actor = actorProvider.GetRequiredActor();
        if (!Guid.TryParse(actor, out var actorId) || actorId == Guid.Empty) throw LaboratoryRequestException.Internal();
        actor = actorId.ToString();
        if (db.Database.CurrentTransaction is { } caller)
            return await InCallerTransaction(caller, requestId, action, version, actor, ct);

        await using var context = db.CreateIndependentContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var committing = false;
        try
        {
            var response = await Apply(context, requestId, action, version, actor, ct);
            committing = true;
            await transaction.CommitAsync(ct);
            return response;
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205) { throw LaboratoryRequestException.WorkflowConflict(); }
        catch (Exception) when (committing) { throw LaboratoryRequestException.WorkflowUnknown(); }
        catch (Exception ex)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { throw LaboratoryRequestException.WorkflowUnknown(); }
            throw Translate(ex);
        }
    }

    private async Task<RequestWorkflowDto> InCallerTransaction(IDbContextTransaction transaction, Guid id,
        RequestWorkflowAction action, byte[] version, string actor, CancellationToken ct)
    {
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted || !transaction.SupportsSavepoints ||
            new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString).MultipleActiveResultSets ||
            db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Workflow writes require a clean READ COMMITTED caller transaction with savepoints.");
        await using var context = db.CreateIndependentContext();
        context.Database.SetDbConnection(db.Database.GetDbConnection(), contextOwnsConnection: false);
        await using var enlisted = await context.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var savepoint = "WF" + Guid.NewGuid().ToString("N")[..24];
        await transaction.CreateSavepointAsync(savepoint, ct);
        try { return await Apply(context, id, action, version, actor, ct); }
        catch (Exception ex)
        {
            // A deadlock victim's whole transaction has already been rolled back by SQL Server.
            if (SqlNumber(ex) != 1205)
            {
                try { await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None); }
                catch { throw LaboratoryRequestException.WorkflowUnknown(); }
            }
            throw Translate(ex);
        }
    }

    private async Task<RequestWorkflowDto> Apply(ClinisysDbContext context, Guid id, RequestWorkflowAction action,
        byte[] version, string actor, CancellationToken ct)
    {
        // Same parent-first protocol as result entry/correction: no result may change
        // between completeness checks and biological validation.
        var request = await context.LaboratoryRequests.FromSqlInterpolated(
            $"SELECT * FROM dbo.LaboratoryRequests WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(ct) ?? throw LaboratoryRequestException.NotFound();
        if (!request.Version.SequenceEqual(version)) throw LaboratoryRequestException.WorkflowConflict();
        var previous = request.Status;
        var instant = clock.GetUtcNow().ToUniversalTime();
        try
        {
            switch (action)
            {
                case RequestWorkflowAction.Start:
                    request.StartProcessing();
                    break;
                case RequestWorkflowAction.CompleteEntry:
                case RequestWorkflowAction.Validate:
                    var items = await context.RequestItems.FromSqlInterpolated(
                        $"SELECT * FROM dbo.RequestItems WITH (HOLDLOCK) WHERE LaboratoryRequestId = {id}")
                        .AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
                    // Join restricts the locked rows to this request, not the whole result table.
                    var results = await context.AnalysisResults.FromSqlInterpolated(
                        $"SELECT r.* FROM dbo.AnalysisResults r WITH (UPDLOCK,HOLDLOCK) INNER JOIN dbo.RequestItems i ON i.Id=r.RequestItemId WHERE i.LaboratoryRequestId={id}")
                        .OrderBy(x => x.RequestItemId).ToListAsync(ct);
                    var complete = items.Count > 0 && items.All(i => results.Any(r => r.RequestItemId == i.Id));
                    if (action == RequestWorkflowAction.CompleteEntry) request.MarkResultsEntered(complete);
                    else
                    {
                        request.ValidateResults(complete, actor, instant);
                        foreach (var result in results) result.Validate(actor, instant);
                    }
                    break;
                case RequestWorkflowAction.Deliver:
                    request.Deliver(instant);
                    break;
                default: throw LaboratoryRequestException.Validation("action");
            }
        }
        catch (InvalidOperationException) { throw LaboratoryRequestException.WorkflowConflict(); }
        catch (ArgumentException) { throw LaboratoryRequestException.WorkflowConflict(); }

        var auditAction = action switch
        {
            RequestWorkflowAction.Validate => AuditAction.Validated,
            RequestWorkflowAction.Deliver => AuditAction.Delivered,
            _ => AuditAction.StatusChanged
        };
        context.AuditEntries.Add(new AuditEntry(id, null, auditAction, actor, nameof(LaboratoryRequest.Status),
            previous.ToString(), request.Status.ToString(), instant));
        context.Entry(request).Property(x => x.Version).OriginalValue = version;
        await context.SaveChangesAsync(ct);
        return new(request.Id, request.Status, Convert.ToBase64String(request.Version),
            request.ValidatedAt, request.ValidatedBy, request.DeliveredAt);
    }

    private static int? SqlNumber(Exception ex)
    {
        while (ex is InvalidOperationException or DbUpdateException && ex.InnerException is { } inner) ex = inner;
        return ex is SqlException sql ? sql.Number : null;
    }

    private static Exception Translate(Exception ex) => ex is DbUpdateConcurrencyException || SqlNumber(ex) == 1205
        ? LaboratoryRequestException.WorkflowConflict() : ex;
}
