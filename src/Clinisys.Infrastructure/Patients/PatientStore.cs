using Clinisys.Application.Features.Patients;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Patients;

public sealed class PatientStore(ClinisysDbContext db) : IPatientStore
{
    private static PatientDetailDto Dto(Patient p) => new(p.Id, p.FirstName, p.LastName, p.DateOfBirth,
        p.Sex, p.NationalIdentityNumber, p.PhoneNumber, Convert.ToBase64String(p.Version));

    public async Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return patient is null ? null : Dto(patient);
    }

    public async Task<PatientPage> SearchAsync(PatientPageQuery query, CancellationToken ct)
    {
        var source = db.Patients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var identity = term.ToUpperInvariant();
            source = source.Where(x => x.FirstName.Contains(term) || x.LastName.Contains(term)
                || x.NationalIdentityNumber == identity || x.PhoneNumber == term);
        }
        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
            .Skip(query.Offset).Take(query.PageSize)
            .Select(x => new PatientListDto(x.Id, x.FirstName, x.LastName, x.DateOfBirth, x.Sex)).ToListAsync(ct);
        return new(items, count, query.Page, query.PageSize);
    }

    public async Task<PatientDetailDto> CreateAsync(Patient patient, CancellationToken ct)
    {
        db.Patients.Add(patient);
        await SaveAsync(ct);
        return Dto(patient);
    }

    public async Task<PatientDetailDto> UpdateAsync(Guid id, Patient draft, byte[] version, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw PatientException.NotFound();
        if (!patient.Version.SequenceEqual(version)) throw PatientException.Conflict();
        // The client token remains the UPDATE predicate even if another writer commits after this read.
        db.Entry(patient).Property(x => x.Version).OriginalValue = version;
        patient.Update(draft.FirstName, draft.LastName, draft.DateOfBirth, draft.Sex, draft.NationalIdentityNumber, draft.PhoneNumber);
        // Also check concurrency for an otherwise unchanged PUT.
        db.Entry(patient).Property(x => x.FirstName).IsModified = true;
        await SaveAsync(ct);
        return Dto(patient);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw PatientException.Conflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 547 or 1205 })
        { throw PatientException.Conflict(); }
    }
}
