using Clinisys.Application.Common;

namespace Clinisys.Application.Features.MedicalAnalyses;

public sealed class MedicalAnalysisQueries(IMedicalAnalysisStore store)
{
    public async Task<AnalysisDto> GetAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(id, ct) ?? throw CatalogException.NotFound("Medical analysis");

    public Task<CatalogPage<AnalysisDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct)
    {
        query.Validate();
        return store.SearchAsync(query, ct);
    }
}
