using System.Linq.Expressions;
using Clinisys.Application.Common;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Catalog;

public sealed class MedicalAnalysisStore(ClinisysDbContext db) : IMedicalAnalysisStore
{
    internal static readonly Expression<Func<MedicalAnalysis, AnalysisDto>> Projection =
        x => new AnalysisDto(x.Id, x.Code, x.Name, x.Unit, x.Price, x.IsActive);
    internal static AnalysisDto Dto(MedicalAnalysis x) => new(x.Id, x.Code, x.Name, x.Unit, x.Price, x.IsActive);

    public Task<AnalysisDto?> GetAsync(Guid id, CancellationToken ct) =>
        db.MedicalAnalyses.AsNoTracking().Where(x => x.Id == id).Select(Projection).SingleOrDefaultAsync(ct);

    public async Task<CatalogPage<AnalysisDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct)
    {
        var source = db.MedicalAnalyses.AsNoTracking();
        if (query.IsActive is { } active) source = source.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }
        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip(query.Offset).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new(items, count, query.Page, query.PageSize);
    }

    public async Task<AnalysisDto> CreateAsync(MedicalAnalysis analysis, CancellationToken ct)
    {
        db.MedicalAnalyses.Add(analysis);
        await CatalogPersistence.SaveAsync(db, ct);
        return Dto(analysis);
    }

    public async Task<AnalysisDto> UpdateAsync(Guid id, MedicalAnalysis draft, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var analysis = await CatalogPersistence.LockAnalysisAsync(db, id, ct);
        if (analysis.Unit != draft.Unit && await db.ReferenceRanges.AnyAsync(x => x.MedicalAnalysisId == id, ct))
            throw CatalogException.Conflict("Remove or review the catalogue reference ranges before changing the measurement unit. No automatic unit conversion is performed.");
        analysis.Update(draft.Code, draft.Name, draft.Unit, draft.Price);
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Dto(analysis);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var analysis = await CatalogPersistence.LockAnalysisAsync(db, id, ct);
        analysis.Deactivate();
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
