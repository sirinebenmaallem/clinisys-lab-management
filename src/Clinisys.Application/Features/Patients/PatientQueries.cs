namespace Clinisys.Application.Features.Patients;

public sealed class PatientQueries(IPatientStore store)
{
    public async Task<PatientDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(id, ct) ?? throw PatientException.NotFound();
    public Task<PatientPage> SearchAsync(PatientPageQuery query, CancellationToken ct)
    {
        query.Validate();
        return store.SearchAsync(query, ct);
    }
}
