using Clinisys.Application.Common;

namespace Clinisys.Application.Features.AnalysisPanels;

public sealed class AnalysisPanelQueries(IAnalysisPanelStore store)
{
    public async Task<PanelDetailsDto> GetAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(id, ct) ?? throw CatalogException.NotFound("Analysis panel");
    public Task<CatalogPage<PanelDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct)
    {
        query.Validate();
        return store.SearchAsync(query, ct);
    }
}
