using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.AnalysisResults;

public sealed class PatientAnalysisHistoryStore(ClinisysDbContext db) : IPatientAnalysisHistoryStore
{
    public async Task<PatientAnalysisHistoryDto?> GetAsync(Guid patientId, Guid analysisId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(x => x.Id == patientId, ct)
            || !await db.MedicalAnalyses.AsNoTracking().AnyAsync(x => x.Id == analysisId, ct)) return null;
        var points = await (from request in db.LaboratoryRequests.AsNoTracking()
                            join item in db.RequestItems.AsNoTracking() on request.Id equals item.LaboratoryRequestId
                            join result in db.AnalysisResults.AsNoTracking() on item.Id equals result.RequestItemId
                            where request.PatientId == patientId && item.MedicalAnalysisId == analysisId
                                && (request.Status == RequestStatus.Validated || request.Status == RequestStatus.Delivered)
                                && request.ValidatedAt != null && request.ValidatedBy != null
                                && result.ValidatedAt != null && result.ValidatedBy != null
                            orderby request.RegisteredAt, request.Id, item.Id
                            select new PatientAnalysisHistoryPointDto(request.Id, request.Number,
                                request.RegisteredAt, result.EnteredAt, result.Value, item.UnitSnapshot, result.Abnormality))
            .ToListAsync(ct);
        return new(patientId, analysisId, points);
    }
}
