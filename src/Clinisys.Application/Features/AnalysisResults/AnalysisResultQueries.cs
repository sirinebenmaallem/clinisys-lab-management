namespace Clinisys.Application.Features.AnalysisResults;

public sealed class AnalysisResultQueries(IAnalysisResultStore store)
{
    public async Task<AnalysisResultDetailDto> GetAsync(Guid requestId, Guid itemId, CancellationToken ct)
    {
        if (requestId == Guid.Empty || itemId == Guid.Empty) throw AnalysisResultException.Validation("identifiers");
        return await store.GetAsync(requestId, itemId, ct) ?? throw AnalysisResultException.NotFound();
    }

    public async Task<AnalysisResultListDto> ListForRequestAsync(Guid requestId, CancellationToken ct)
    {
        if (requestId == Guid.Empty) throw AnalysisResultException.Validation("identifiers");
        return await store.ListForRequestAsync(requestId, ct) ?? throw AnalysisResultException.NotFound();
    }
}
