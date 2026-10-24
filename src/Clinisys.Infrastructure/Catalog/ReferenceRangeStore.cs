using System.Linq.Expressions;
using Clinisys.Application.Common;
using Clinisys.Application.Features.ReferenceRanges;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Catalog;

public sealed class ReferenceRangeStore(ClinisysDbContext db) : IReferenceRangeStore
{
    private static readonly Expression<Func<ReferenceRange, ReferenceRangeDto>> Projection =
        x => new ReferenceRangeDto(x.Id, x.MedicalAnalysisId, x.Sex, x.MinimumAgeDays, x.MaximumAgeDays, x.LowerBound, x.UpperBound);
    private static ReferenceRangeDto Dto(ReferenceRange x) =>
        new(x.Id, x.MedicalAnalysisId, x.Sex, x.MinimumAgeDays, x.MaximumAgeDays, x.LowerBound, x.UpperBound);

    public async Task<IReadOnlyList<ReferenceRangeDto>> ListAsync(Guid analysisId, CancellationToken ct)
    {
        if (!await db.MedicalAnalyses.AsNoTracking().AnyAsync(x => x.Id == analysisId, ct))
            throw CatalogException.NotFound("Medical analysis");
        return await db.ReferenceRanges.AsNoTracking().Where(x => x.MedicalAnalysisId == analysisId)
            .OrderBy(x => x.Sex).ThenBy(x => x.MinimumAgeDays).ThenBy(x => x.Id).Select(Projection).ToListAsync(ct);
    }

    public Task<ReferenceRangeDto?> GetAsync(Guid analysisId, Guid id, CancellationToken ct) =>
        db.ReferenceRanges.AsNoTracking().Where(x => x.MedicalAnalysisId == analysisId && x.Id == id)
            .Select(Projection).SingleOrDefaultAsync(ct);

    public async Task<ReferenceRangeDto> CreateAsync(ReferenceRange range, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await RequireActiveAnalysis(range.MedicalAnalysisId, ct);
        await RejectOverlap(range, null, ct);
        db.ReferenceRanges.Add(range);
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Dto(range);
    }

    public async Task<ReferenceRangeDto> UpdateAsync(Guid analysisId, Guid id, ReferenceRange draft, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await RequireActiveAnalysis(analysisId, ct);
        var range = await db.ReferenceRanges.SingleOrDefaultAsync(x => x.Id == id && x.MedicalAnalysisId == analysisId, ct)
            ?? throw CatalogException.NotFound("Reference range");
        await RejectOverlap(draft, id, ct);
        range.Update(draft.Sex, draft.MinimumAgeDays, draft.MaximumAgeDays, new ReferenceInterval(draft.LowerBound, draft.UpperBound));
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Dto(range);
    }

    public async Task DeleteAsync(Guid analysisId, Guid id, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await CatalogPersistence.LockAnalysisAsync(db, analysisId, ct);
        var range = await db.ReferenceRanges.SingleOrDefaultAsync(x => x.Id == id && x.MedicalAnalysisId == analysisId, ct)
            ?? throw CatalogException.NotFound("Reference range");
        // Results hold their own bounds; no clinical/history record references this catalogue row.
        db.ReferenceRanges.Remove(range);
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private async Task RequireActiveAnalysis(Guid id, CancellationToken ct)
    {
        if (!(await CatalogPersistence.LockAnalysisAsync(db, id, ct)).IsActive)
            throw CatalogException.Conflict("Reference ranges cannot be created or updated for an inactive analysis.");
    }

    private async Task RejectOverlap(ReferenceRange candidate, Guid? excludedId, CancellationToken ct)
    {
        var existing = await db.ReferenceRanges.AsNoTracking().Where(x => x.MedicalAnalysisId == candidate.MedicalAnalysisId
            && x.Id != excludedId && x.Sex == candidate.Sex).ToListAsync(ct);
        if (existing.Any(candidate.Overlaps))
            throw CatalogException.Conflict("An overlapping reference range already exists for this analysis and sex.");
    }
}
