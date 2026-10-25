using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.LaboratoryRequests;

public sealed partial class LaboratoryRequestStore
{
    private static decimal Total(IEnumerable<RequestItem> items)
    {
        try { return items.Sum(x => x.NetPrice); }
        catch (OverflowException) { throw LaboratoryRequestException.Internal(); }
    }
    private static RequestPatientDto PatientDto(Patient p) => new(p.Id, p.FirstName, p.LastName);
    private static LaboratoryRequestDetailDto Detail(LaboratoryRequest r, Patient p, List<RequestItem> items, List<RequestPanel> panels) =>
        new(r.Id, r.Number, PatientDto(p), r.PatientDateOfBirthSnapshot, r.PatientSexSnapshot, r.PrescribingPhysician,
            r.DiscountPercent, r.Status, r.RegisteredAt,
            panels.OrderBy(x => x.PanelCodeSnapshot).ThenBy(x => x.Id)
                .Select(x => new RequestPanelDto(x.Id, x.AnalysisPanelId, x.PanelCodeSnapshot, x.PanelNameSnapshot)).ToArray(),
            items.OrderBy(x => x.AnalysisCodeSnapshot).ThenBy(x => x.Id)
                .Select(x => new RequestItemDto(x.Id, x.MedicalAnalysisId, x.AnalysisCodeSnapshot, x.AnalysisNameSnapshot,
                    x.UnitSnapshot, x.UnitPriceSnapshot, x.DiscountPercentSnapshot, x.NetPrice)).ToArray(),
            Total(items), Convert.ToBase64String(r.Version));

    public async Task<LaboratoryRequestDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var request = await db.LaboratoryRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (request is null) return null;
            var patient = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == request.PatientId, ct);
            var items = await db.RequestItems.AsNoTracking().Where(x => x.LaboratoryRequestId == id).ToListAsync(ct);
            var panels = await db.RequestPanels.AsNoTracking().Where(x => x.LaboratoryRequestId == id).ToListAsync(ct);
            return Detail(request, patient, items, panels);
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205)
        {
            throw LaboratoryRequestException.Conflict();
        }
    }

    public async Task<LaboratoryRequestPage> SearchAsync(LaboratoryRequestPageQuery query, CancellationToken ct)
    {
        try
        {
            var source = db.LaboratoryRequests.AsNoTracking();
            if (query.PatientId.HasValue) source = source.Where(x => x.PatientId == query.PatientId);
            if (!string.IsNullOrWhiteSpace(query.Number))
            {
                var number = query.Number.Trim().ToUpperInvariant();
                source = source.Where(x => x.Number == number);
            }
            if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
            if (query.RegisteredFrom.HasValue) source = source.Where(x => x.RegisteredAt >= query.RegisteredFrom);
            if (query.RegisteredTo.HasValue) source = source.Where(x => x.RegisteredAt < query.RegisteredTo);
            var count = await source.CountAsync(ct);
            var rows = await source.OrderByDescending(x => x.RegisteredAt).ThenBy(x => x.Id)
                .Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
            var ids = rows.Select(x => x.Id).ToArray();
            var patientIds = rows.Select(x => x.PatientId).Distinct().ToArray();
            var patients = await db.Patients.AsNoTracking().Where(x => patientIds.Contains(x.Id))
                .Select(x => new RequestPatientDto(x.Id, x.FirstName, x.LastName)).ToDictionaryAsync(x => x.Id, ct);
            var items = await db.RequestItems.AsNoTracking().Where(x => ids.Contains(x.LaboratoryRequestId)).ToListAsync(ct);
            var totals = items.GroupBy(x => x.LaboratoryRequestId).ToDictionary(x => x.Key, Total);
            return new(rows.Select(x => new LaboratoryRequestListDto(x.Id, x.Number, patients[x.PatientId], x.PrescribingPhysician,
                x.Status, x.RegisteredAt, totals.GetValueOrDefault(x.Id))).ToArray(), count, query.Page, query.PageSize);
        }
        catch (Exception ex) when (SqlNumber(ex) == 1205)
        {
            throw LaboratoryRequestException.Conflict();
        }
    }
}
