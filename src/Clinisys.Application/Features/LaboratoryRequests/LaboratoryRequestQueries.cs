namespace Clinisys.Application.Features.LaboratoryRequests;

public sealed class LaboratoryRequestQueries(ILaboratoryRequestStore store)
{
    public async Task<LaboratoryRequestDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(id, ct) ?? throw LaboratoryRequestException.NotFound();
    public Task<LaboratoryRequestPage> SearchAsync(LaboratoryRequestPageQuery query, CancellationToken ct)
    {
        query.Validate();
        return store.SearchAsync(query, ct);
    }
}
