using System.Data;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain.Common;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinisys.Infrastructure.LaboratoryRequests;

public sealed partial class LaboratoryRequestStore(ClinisysDbContext db, TimeProvider clock) : ILaboratoryRequestStore
{
    public async Task<LaboratoryRequestDetailDto> CreateAsync(LaboratoryRequestInput input, CancellationToken ct)
    {
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Ambient transactions are not supported for request creation.");
        if (db.Database.CurrentTransaction is { } existing)
            return await CreateInCallerTransaction(input, existing, ct);

        for (var attempt = 1; ; attempt++)
        {
            await using var context = db.CreateIndependentContext();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var committing = false;
            try
            {
                var result = await CreateCore(context, input, transaction, [], ct);
                committing = true;
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (SqlNumber(ex) == 1205)
            {
                // SQL Server has rolled back the entire deadlock victim. Retry only a complete owned operation.
                if (attempt == 3) throw LaboratoryRequestException.Conflict();
            }
            catch (Exception) when (committing)
            {
                // A failed commit must never be assumed to mean rollback or automatically replayed.
                throw LaboratoryRequestException.Unknown();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw Translate(ex);
            }
        }
    }

    private async Task<LaboratoryRequestDetailDto> CreateInCallerTransaction(
        LaboratoryRequestInput input, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Request creation requires a READ COMMITTED caller transaction.");
        if (!transaction.SupportsSavepoints ||
            new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString).MultipleActiveResultSets)
            throw new InvalidOperationException("Request creation requires savepoint support without MARS.");
        // Do not accidentally save unrelated pending changes owned by the caller.
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Use a context without pending changes for request creation.");
        var savepoint = "LR" + Guid.NewGuid().ToString("N")[..24];
        await transaction.CreateSavepointAsync(savepoint, ct);
        var added = new List<Entity>();
        try { return await CreateCore(db, input, transaction, added, ct); }
        catch (Exception ex)
        {
            if (SqlNumber(ex) != 1205)
            {
                try { await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None); }
                catch { throw LaboratoryRequestException.Unknown(); }
            }
            throw Translate(ex);
        }
        finally
        {
            // Also detach successfully saved entities: the outer transaction may later roll back.
            foreach (var entity in added) db.Entry(entity).State = EntityState.Detached;
        }
    }

    private async Task<LaboratoryRequestDetailDto> CreateCore(ClinisysDbContext context,
        LaboratoryRequestInput input, IDbContextTransaction transaction, List<Entity> added, CancellationToken ct)
    {
        var panels = new List<AnalysisPanel>();
        foreach (var id in (input.PanelIds ?? []).Distinct().Order())
        {
            var panel = await context.AnalysisPanels.FromSqlInterpolated(
                $"SELECT * FROM dbo.AnalysisPanels WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}")
                .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw LaboratoryRequestException.NotFound();
            if (!panel.IsActive) throw LaboratoryRequestException.Conflict();
            panels.Add(panel);
        }
        var ids = new HashSet<Guid>(input.AnalysisIds ?? []);
        foreach (var panel in panels)
        {
            var composition = await context.PanelAnalyses.FromSqlInterpolated(
                $"SELECT * FROM dbo.PanelAnalyses WITH (HOLDLOCK) WHERE AnalysisPanelId = {panel.Id}")
                .AsNoTracking().Take(LaboratoryRequestCommands.MaximumSelection + 1).ToListAsync(ct);
            if (composition.Count == 0) throw LaboratoryRequestException.Conflict();
            foreach (var link in composition) ids.Add(link.MedicalAnalysisId);
            if (ids.Count > LaboratoryRequestCommands.MaximumSelection)
                throw LaboratoryRequestException.Validation("selection");
        }
        if (ids.Count is 0 or > LaboratoryRequestCommands.MaximumSelection)
            throw LaboratoryRequestException.Validation("selection");
        var analyses = new List<MedicalAnalysis>();
        foreach (var id in ids.Order())
        {
            var analysis = await context.MedicalAnalyses.FromSqlInterpolated(
                $"SELECT * FROM dbo.MedicalAnalyses WITH (HOLDLOCK) WHERE Id = {id}")
                .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw LaboratoryRequestException.NotFound();
            if (!analysis.IsActive) throw LaboratoryRequestException.Conflict();
            analyses.Add(analysis);
        }
        var patient = await context.Patients.FromSqlInterpolated(
            $"SELECT * FROM dbo.Patients WITH (HOLDLOCK) WHERE Id = {input.PatientId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw LaboratoryRequestException.NotFound();
        var instant = clock.GetUtcNow().ToUniversalTime();
        var next = await Allocate(context, transaction, instant.Year, ct);
        LaboratoryRequest request;
        try { request = LaboratoryRequest.Register(LaboratoryRequest.FormatNumber(instant.Year, next), patient,
            input.PrescribingPhysician, input.DiscountPercent, instant); }
        catch (ArgumentException) { throw LaboratoryRequestException.Validation("request"); }
        var items = analyses.Select(x => new RequestItem(request, x)).ToList();
        var selectedPanels = panels.Select(x => new RequestPanel(request.Id, x)).ToList();
        _ = Total(items); // Detect any decimal overflow before writing the graph.
        added.Add(request);
        added.AddRange(items);
        added.AddRange(selectedPanels);
        context.AddRange(added);
        await context.SaveChangesAsync(ct);
        return Detail(request, patient, items, selectedPanels);
    }

    private static async Task<int> Allocate(ClinisysDbContext context, IDbContextTransaction transaction, int year, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SET NOCOUNT ON;
            DECLARE @last int;
            SELECT @last = LastValue FROM dbo.LaboratoryRequestCounters WITH (UPDLOCK,HOLDLOCK) WHERE [Year] = @year;
            IF @last IS NULL
            BEGIN
                INSERT dbo.LaboratoryRequestCounters ([Year],LastValue) VALUES (@year,1);
                SET @last = 1;
            END
            ELSE
            BEGIN
                IF @last >= 999999 THROW 51021, 'Annual numbering exhausted.', 1;
                SET @last = @last + 1;
                UPDATE dbo.LaboratoryRequestCounters SET LastValue = @last WHERE [Year] = @year;
            END;
            SELECT @last;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@year";
        parameter.DbType = DbType.Int32;
        parameter.Value = year;
        command.Parameters.Add(parameter);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    private static int? SqlNumber(Exception exception)
    {
        // SQL Server's non-retrying EF execution strategy can wrap a transient SQL
        // failure in InvalidOperationException (including a DbUpdateException).
        while (exception is InvalidOperationException or DbUpdateException)
        {
            if (exception.InnerException is not { } inner) return null;
            exception = inner;
        }
        return exception is SqlException sql ? sql.Number : null;
    }
    private static Exception Translate(Exception exception) => SqlNumber(exception) switch
    {
        51021 => LaboratoryRequestException.Exhausted(),
        1205 or 547 => LaboratoryRequestException.Conflict(),
        2601 or 2627 => LaboratoryRequestException.Internal(),
        _ => exception
    };
}
