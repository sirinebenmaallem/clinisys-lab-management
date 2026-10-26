using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.LaboratoryReports;

public sealed class LaboratoryReportStore(ClinisysDbContext db) : ILaboratoryReportStore
{
    public async Task<LaboratoryReportData?> GetAsync(Guid requestId, CancellationToken ct)
    {
        // Read-only projection: no patient identifier/phone fields, catalogue joins,
        // user lookups, tracking, recalculation, audit writes or SaveChanges.
        var rows = await (
            from request in db.LaboratoryRequests.AsNoTracking()
            join patient in db.Patients.AsNoTracking() on request.PatientId equals patient.Id
            from item in db.RequestItems.AsNoTracking().Where(i => i.LaboratoryRequestId == request.Id).DefaultIfEmpty()
            from result in db.AnalysisResults.AsNoTracking().Where(r => item != null && r.RequestItemId == item.Id).DefaultIfEmpty()
            where request.Id == requestId
            orderby item.AnalysisCodeSnapshot, item.Id
            select new
            {
                request.Id, request.Number, request.Status, request.PatientId,
                patient.FirstName, patient.LastName, request.PatientDateOfBirthSnapshot, request.PatientSexSnapshot,
                request.RegisteredAt, request.ValidatedAt, request.ValidatedBy,
                ItemId = item == null ? (Guid?)null : item.Id,
                Code = item == null ? "" : item.AnalysisCodeSnapshot,
                Name = item == null ? "" : item.AnalysisNameSnapshot,
                Unit = item == null ? "" : item.UnitSnapshot,
                Value = result == null ? (decimal?)null : result.Value,
                Lower = result == null ? null : result.ReferenceLowerBoundSnapshot,
                Upper = result == null ? null : result.ReferenceUpperBoundSnapshot,
                Abnormality = result == null ? (Clinisys.Domain.Abnormality?)null : result.Abnormality,
                ResultValidatedAt = result == null ? null : result.ValidatedAt,
                ResultValidatedBy = result == null ? null : result.ValidatedBy,
                IsCalculated = result != null && result.IsCalculated
            }).ToListAsync(ct);
        if (rows.Count == 0) return null;
        var first = rows[0];
        return new(first.Id, first.Number, first.Status, first.PatientId,
            first.FirstName, first.LastName, first.PatientDateOfBirthSnapshot, first.PatientSexSnapshot,
            first.RegisteredAt, first.ValidatedAt, first.ValidatedBy,
            rows.Where(x => x.ItemId is not null).Select(x => new ReportAnalysisDto(x.Code, x.Name, x.Unit,
                x.Value, x.Lower, x.Upper, x.Abnormality, x.ResultValidatedAt, x.ResultValidatedBy, x.IsCalculated)).ToArray());
    }
}
