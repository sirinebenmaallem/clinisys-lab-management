using System.Globalization;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.AnalysisResults;

public sealed partial class AnalysisResultStore
{
    private async Task<LdlCalculationDto> SynchronizeLdl(ClinisysDbContext context, LaboratoryRequest request,
        RequestItem changedItem, AnalysisResult changedResult, string actor, CancellationToken ct)
    {
        if (!ldlCodes.IsValid)
        {
            if (await (from r in context.AnalysisResults
                       join i in context.RequestItems on r.RequestItemId equals i.Id
                       where i.LaboratoryRequestId == request.Id && r.IsCalculated select r.Id).AnyAsync(ct))
                throw AnalysisResultException.Conflict();
            return new("NotConfigured", null, null);
        }
        // The parent is already locked, serializing every result writer/validator.
        var items = await context.RequestItems.FromSqlInterpolated(
            $"SELECT * FROM dbo.RequestItems WITH (HOLDLOCK) WHERE LaboratoryRequestId={request.Id}")
            .AsNoTracking().ToListAsync(ct);
        var stored = await context.AnalysisResults.FromSqlInterpolated(
            $"SELECT r.* FROM dbo.AnalysisResults r WITH (UPDLOCK,HOLDLOCK) INNER JOIN dbo.RequestItems i ON r.RequestItemId=i.Id WHERE i.LaboratoryRequestId={request.Id}")
            .AsNoTracking().ToListAsync(ct);
        // Include the pending source change, without saving it outside the transaction.
        stored.RemoveAll(r => r.RequestItemId == changedItem.Id);
        stored.Add(changedResult);
        if (stored.Any(r => r.IsCalculated && !items.Any(i => i.Id == r.RequestItemId && i.AnalysisCodeSnapshot == ldlCodes.CalculatedLdl)))
            throw AnalysisResultException.Conflict();
        var calculation = LdlCalculation.Evaluate(ldlCodes, items, stored);
        if (calculation.Status == "AmbiguousAnalysis") throw AnalysisResultException.Conflict();
        if (calculation.RequestItemId is not { } targetId || calculation.Status == "ManualResultPreserved") return calculation;
        var target = items.Single(i => i.Id == targetId);
        var existing = stored.SingleOrDefault(r => r.RequestItemId == targetId);
        var instant = clock.GetUtcNow().ToUniversalTime();
        if (calculation.Value is not { } value)
        {
            RemoveInvalidCalculatedResult(context, request.Id, existing, actor, instant);
            return calculation;
        }
        if (existing is not null)
        {
            PrepareAuditedCorrection(context, request.Id, existing, value, existing.Version, actor, instant);
            return calculation;
        }
        var analysis = await context.MedicalAnalyses.FromSqlInterpolated(
            $"SELECT * FROM dbo.MedicalAnalyses WITH (UPDLOCK,HOLDLOCK) WHERE Id={target.MedicalAnalysisId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw AnalysisResultException.Internal();
        var ranges = await context.ReferenceRanges.FromSqlInterpolated(
            $"SELECT * FROM dbo.ReferenceRanges WITH (HOLDLOCK) WHERE MedicalAnalysisId={target.MedicalAnalysisId}")
            .AsNoTracking().ToListAsync(ct);
        ReferenceInterval? interval;
        try { interval = AnalysisResultReferenceSelector.Select(request, target, analysis, ranges); }
        catch (AnalysisResultException ex) when (ex.Error == AnalysisResultError.Conflict)
        {
            return new("ReferenceConflict", targetId, null);
        }
        var result = AnalysisResult.CreateCalculated(target.Id, value, actor, interval, instant);
        context.AnalysisResults.Add(result);
        context.AuditEntries.Add(new AuditEntry(request.Id, target.Id, AuditAction.Created, actor,
            nameof(AnalysisResult.Value), null, value.ToString(CultureInfo.InvariantCulture), instant));
        return calculation;
    }

    private static void RemoveInvalidCalculatedResult(ClinisysDbContext context, Guid requestId,
        AnalysisResult? result, string actor, DateTimeOffset instant)
    {
        if (result is null) return;
        if (!result.IsCalculated || result.ValidatedAt is not null || result.ValidatedBy is not null)
            throw AnalysisResultException.Conflict();
        context.AnalysisResults.Remove(result);
        context.AuditEntries.Add(new AuditEntry(requestId, result.RequestItemId, AuditAction.Updated, actor,
            nameof(AnalysisResult.Value), result.Value.ToString(CultureInfo.InvariantCulture), null, instant));
    }
}
