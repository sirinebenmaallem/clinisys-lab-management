using System.Data;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinisys.Infrastructure.AnalysisResults;

public sealed partial class AnalysisResultStore : IAnalysisResultStore
{
    private readonly ClinisysDbContext db;
    private readonly TimeProvider clock;
    private readonly IAnalysisResultActorProvider actorProvider;
    private readonly LdlAnalysisCodes ldlCodes;

    public AnalysisResultStore(ClinisysDbContext db, TimeProvider clock, IAnalysisResultActorProvider actorProvider, LdlAnalysisCodes? ldlCodes = null)
    {
        this.ldlCodes = ldlCodes ?? new();
        this.db = db ?? throw new ArgumentNullException(nameof(db));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.actorProvider = actorProvider ?? throw new ArgumentNullException(nameof(actorProvider));
    }

    public Task<AnalysisResultDetailDto> CreateAsync(Guid requestId, Guid itemId, decimal value, CancellationToken ct) =>
        WriteAsync(requestId, itemId, value, null, ct);

    public Task<AnalysisResultDetailDto> UpdateAsync(Guid requestId, Guid itemId, decimal value, byte[] version, CancellationToken ct)
    {
        if (version is null || version.Length != 8) throw AnalysisResultException.Validation("version");
        return WriteAsync(requestId, itemId, value, version.ToArray(), ct);
    }

    private async Task<AnalysisResultDetailDto> WriteAsync(Guid requestId, Guid itemId, decimal value, byte[]? version, CancellationToken ct)
    {
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Ambient transactions are not supported for result writes.");
        // Preserve the configured connection/interceptors, but never inherit automatic retries.
        if (db.Database.CreateExecutionStrategy().RetriesOnFailure)
            throw new InvalidOperationException("Result persistence requires an execution strategy without automatic retries.");
        var actor = RequireActor();
        try
        {
            if (db.Database.CurrentTransaction is { } existing)
                return await WriteInCallerTransaction(existing, requestId, itemId, value, version, actor, ct);

            await using var context = db.CreateIndependentContext();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var committing = false;
            try
            {
                var result = await WriteCore(context, requestId, itemId, value, version, actor, ct);
                committing = true;
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (SqlNumber(ex) == 1205)
            {
                // SQL Server already rolled back the victim. No replay or secondary rollback.
                throw AnalysisResultException.Conflict();
            }
            catch (Exception) when (committing) { throw AnalysisResultException.Unknown(); }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { throw AnalysisResultException.Unknown(); }
                var translated = TranslateWriteFailure(ex);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205) { throw AnalysisResultException.Conflict(); }
    }

    private async Task<AnalysisResultDetailDto> WriteInCallerTransaction(IDbContextTransaction transaction,
        Guid requestId, Guid itemId, decimal value, byte[]? version, string actor, CancellationToken ct)
    {
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Result writes require a READ COMMITTED caller transaction.");
        if (!transaction.SupportsSavepoints ||
            new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString).MultipleActiveResultSets)
            throw new InvalidOperationException("Result writes require savepoints without MARS.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Use a caller context without pending changes for result writes.");

        // A private tracker on the SAME physical connection/transaction prevents
        // stale caller-tracked entities from bypassing database checks. Disposing
        // this context detaches only our state, including after successful saves.
        await using var context = db.CreateIndependentContext();
        context.Database.SetDbConnection(db.Database.GetDbConnection(), contextOwnsConnection: false);
        await using var enlisted = await context.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var savepoint = "AR" + Guid.NewGuid().ToString("N")[..24];
        await transaction.CreateSavepointAsync(savepoint, ct);
        try { return await WriteCore(context, requestId, itemId, value, version, actor, ct); }
        catch (Exception ex)
        {
            if (SqlNumber(ex) != 1205)
            {
                try { await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None); }
                catch { throw AnalysisResultException.Unknown(); }
            }
            var translated = TranslateWriteFailure(ex);
            if (ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
    }

    private async Task<AnalysisResultDetailDto> WriteCore(ClinisysDbContext context, Guid requestId, Guid itemId,
        decimal value, byte[]? version, string actor, CancellationToken ct)
    {
        // Step 08 validation must take this same parent lock BEFORE inspecting children.
        var request = await context.LaboratoryRequests.FromSqlInterpolated(
            $"SELECT * FROM dbo.LaboratoryRequests WITH (UPDLOCK,HOLDLOCK) WHERE Id = {requestId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw AnalysisResultException.NotFound();
        try { request.EnsureResultsEditable(); }
        catch (InvalidOperationException) { throw AnalysisResultException.Conflict(); }

        var item = await context.RequestItems.FromSqlInterpolated(
            $"SELECT * FROM dbo.RequestItems WITH (HOLDLOCK) WHERE Id = {itemId} AND LaboratoryRequestId = {requestId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw AnalysisResultException.NotFound();
        var result = await context.AnalysisResults.FromSqlInterpolated(
            $"SELECT * FROM dbo.AnalysisResults WITH (UPDLOCK,HOLDLOCK) WHERE RequestItemId = {itemId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);

        if (version is null)
        {
            if (result is not null) throw AnalysisResultException.Conflict();
            // Same analysis parent lock as all catalogue reference-range writers.
            // Activity is deliberately not a restriction for a historical item.
            var analysis = await context.MedicalAnalyses.FromSqlInterpolated(
                $"SELECT * FROM dbo.MedicalAnalyses WITH (UPDLOCK,HOLDLOCK) WHERE Id = {item.MedicalAnalysisId}")
                .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw AnalysisResultException.Internal();
            var ranges = await context.ReferenceRanges.FromSqlInterpolated(
                $"SELECT * FROM dbo.ReferenceRanges WITH (HOLDLOCK) WHERE MedicalAnalysisId = {item.MedicalAnalysisId}")
                .AsNoTracking().ToListAsync(ct);
            var interval = AnalysisResultReferenceSelector.Select(request, item, analysis, ranges);
            try { result = new AnalysisResult(item.Id, value, actor, interval, clock.GetUtcNow().ToUniversalTime()); }
            catch (ArgumentException) { throw AnalysisResultException.Validation("value"); }
            context.AnalysisResults.Add(result);
            context.AuditEntries.Add(new AuditEntry(requestId, itemId, Clinisys.Domain.AuditAction.Created,
                actor, nameof(AnalysisResult.Value), null,
                result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), result.EnteredAt));
        }
        else
        {
            if (result is null) throw AnalysisResultException.NotFound();
            if (result.IsCalculated) throw AnalysisResultException.Conflict();
            PrepareAuditedCorrection(context, requestId, result, value, version, actor, clock.GetUtcNow().ToUniversalTime());
        }
        var calculation = await SynchronizeLdl(context, request, item, result, actor, ct);
        await context.SaveChangesAsync(ct);
        // Keep existing result write/read DTO equality when the optional mapping
        // is absent. The request-results list still reports NotConfigured.
        return Detail(item, result) with { LdlCalculation = ldlCodes.IsValid ? calculation : null };
    }

    private static void PrepareAuditedCorrection(ClinisysDbContext context, Guid requestId, AnalysisResult result,
        decimal value, byte[] version, string actor, DateTimeOffset instant)
    {
        var previous = result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PrepareCorrection(context, result, value, version, actor, instant);
        context.AuditEntries.Add(new AuditEntry(requestId, result.RequestItemId, Clinisys.Domain.AuditAction.Updated,
            actor, nameof(AnalysisResult.Value), previous,
            result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), instant));
    }

    private static void PrepareCorrection(ClinisysDbContext context, AnalysisResult result, decimal value,
        byte[] version, string actor, DateTimeOffset instant)
    {
        if (!result.Version.SequenceEqual(version)) throw AnalysisResultException.Conflict();
        try { result.UpdateValue(value, actor, instant); }
        catch (InvalidOperationException) { throw AnalysisResultException.Conflict(); }
        catch (ArgumentException) { throw AnalysisResultException.Validation("value"); }
        context.AnalysisResults.Attach(result);
        context.Entry(result).Property(x => x.Version).OriginalValue = version;
        // Detached materialization is attached AFTER mutation, so explicitly mark
        // only corrected fields. Even an identical value performs a versioned UPDATE.
        context.Entry(result).Property(x => x.Value).IsModified = true;
        context.Entry(result).Property(x => x.Abnormality).IsModified = true;
        context.Entry(result).Property(x => x.EnteredAt).IsModified = true;
        context.Entry(result).Property(x => x.EnteredBy).IsModified = true;
    }

    private string RequireActor()
    {
        var actor = actorProvider.GetRequiredActor();
        if (string.IsNullOrWhiteSpace(actor) || actor.Trim().Length > 100)
            throw AnalysisResultException.Internal();
        return actor.Trim();
    }

    private static int? SqlNumber(Exception exception)
    {
        while (exception is InvalidOperationException or DbUpdateException)
        {
            if (exception.InnerException is not { } inner) return null;
            exception = inner;
        }
        return exception is SqlException sql ? sql.Number : null;
    }

    private static Exception TranslateWriteFailure(Exception exception) => exception is DbUpdateConcurrencyException
        || SqlNumber(exception) is 1205 or 2601 or 2627 ? AnalysisResultException.Conflict() : exception;
}
