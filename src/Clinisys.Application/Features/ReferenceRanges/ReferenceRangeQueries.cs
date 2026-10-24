using Clinisys.Application.Common;

namespace Clinisys.Application.Features.ReferenceRanges;

public sealed class ReferenceRangeQueries(IReferenceRangeStore store)
{
    public Task<IReadOnlyList<ReferenceRangeDto>> ListAsync(Guid analysisId, CancellationToken ct) => store.ListAsync(analysisId, ct);
    public async Task<ReferenceRangeDto> GetAsync(Guid analysisId, Guid id, CancellationToken ct) =>
        await store.GetAsync(analysisId, id, ct) ?? throw CatalogException.NotFound("Reference range");
}
