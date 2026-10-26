using Clinisys.Domain;

namespace Clinisys.Application.Features.AnalysisResults;

public sealed record PatientAnalysisHistoryPointDto(Guid RequestId, string RequestNumber,
    DateTimeOffset RequestedAt, DateTimeOffset ResultAt, decimal Value, string Unit, Abnormality Abnormality);

public sealed record PatientAnalysisHistoryDto(Guid PatientId, Guid AnalysisId,
    IReadOnlyList<PatientAnalysisHistoryPointDto> Points);

public interface IPatientAnalysisHistoryStore
{
    // null means a missing patient or analysis; an existing pair may have no points.
    Task<PatientAnalysisHistoryDto?> GetAsync(Guid patientId, Guid analysisId, CancellationToken ct);
}

public sealed class PatientAnalysisHistoryQueries(IPatientAnalysisHistoryStore store)
{
    public async Task<PatientAnalysisHistoryDto> GetAsync(Guid patientId, Guid analysisId, CancellationToken ct)
    {
        if (patientId == Guid.Empty || analysisId == Guid.Empty)
            throw AnalysisResultException.Validation("identifiers");
        return await store.GetAsync(patientId, analysisId, ct) ?? throw AnalysisResultException.NotFound();
    }
}
