using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.AnalysisResults;

public sealed partial class AnalysisResultStore
{
    private static AnalysisResultDetailDto Detail(RequestItem item, AnalysisResult result) =>
        new(result.Id, item.LaboratoryRequestId, item.Id, item.MedicalAnalysisId,
            item.AnalysisCodeSnapshot, item.AnalysisNameSnapshot, item.UnitSnapshot,
            result.Value, result.ReferenceLowerBoundSnapshot, result.ReferenceUpperBoundSnapshot,
            result.Abnormality, result.EnteredAt, result.EnteredBy, result.ValidatedAt, result.ValidatedBy,
            Convert.ToBase64String(result.Version), result.IsCalculated);

    public async Task<AnalysisResultDetailDto?> GetAsync(Guid requestId, Guid itemId, CancellationToken ct)
    {
        RejectReadRetries();
        try
        {
            var row = await (from item in db.RequestItems.AsNoTracking()
                             join result in db.AnalysisResults.AsNoTracking() on item.Id equals result.RequestItemId
                             where item.Id == itemId && item.LaboratoryRequestId == requestId
                             select new { Item = item, Result = result }).SingleOrDefaultAsync(ct);
            return row is null ? null : Detail(row.Item, row.Result);
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205) { throw AnalysisResultException.Conflict(); }
    }

    public async Task<AnalysisResultListDto?> ListForRequestAsync(Guid requestId, CancellationToken ct)
    {
        RejectReadRetries();
        try
        {
            var request = await db.LaboratoryRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
            if (request is null) return null;
            var rows = await (from item in db.RequestItems.AsNoTracking()
                              join result in db.AnalysisResults.AsNoTracking() on item.Id equals result.RequestItemId into results
                              from result in results.DefaultIfEmpty()
                              where item.LaboratoryRequestId == requestId
                              orderby item.AnalysisCodeSnapshot, item.Id
                              select new { Item = item, Result = result }).ToListAsync(ct);
            var calculation = LdlCalculation.Evaluate(ldlCodes, rows.Select(x => x.Item).ToArray(),
                rows.Where(x => x.Result is not null).Select(x => x.Result!).ToArray());
            if (calculation.Status == "Calculated" &&
                !rows.Any(x => x.Item.Id == calculation.RequestItemId && x.Result is not null))
                calculation = calculation with { Status = "NotCalculated", Value = null };
            var analysisIds = rows.Select(x => x.Item.MedicalAnalysisId).Distinct().ToArray();
            var analyses = await db.MedicalAnalyses.AsNoTracking().Where(x => analysisIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var ranges = await db.ReferenceRanges.AsNoTracking().Where(x => analysisIds.Contains(x.MedicalAnalysisId)).ToListAsync(ct);
            return new(requestId, rows.Select(x => new AnalysisResultListItemDto(x.Item.Id, x.Item.MedicalAnalysisId,
                x.Item.AnalysisCodeSnapshot, x.Item.AnalysisNameSnapshot, x.Item.UnitSnapshot,
                x.Result is null ? null : Detail(x.Item, x.Result),
                AnalysisResultReferenceSelector.Preview(request, x.Item, analyses[x.Item.MedicalAnalysisId], ranges, x.Result))).ToArray(), calculation);
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205) { throw AnalysisResultException.Conflict(); }
    }

    private void RejectReadRetries()
    {
        if (db.Database.CreateExecutionStrategy().RetriesOnFailure)
            throw new InvalidOperationException("Result persistence requires an execution strategy without automatic retries.");
    }
}
