using Clinisys.Application.Common;
using Clinisys.Application.Features.AnalysisPanels;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Catalog;

public sealed class AnalysisPanelStore(ClinisysDbContext db) : IAnalysisPanelStore
{
    private static PanelDto Dto(AnalysisPanel x) => new(x.Id, x.Code, x.Name, x.IsActive);

    public async Task<PanelDetailsDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var panel = await db.AnalysisPanels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (panel is null) return null;
        var analyses = await db.MedicalAnalyses.AsNoTracking()
            .Where(x => db.PanelAnalyses.Any(link => link.AnalysisPanelId == id && link.MedicalAnalysisId == x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id).Select(MedicalAnalysisStore.Projection).ToListAsync(ct);
        return new(panel.Id, panel.Code, panel.Name, panel.IsActive, analyses);
    }

    public async Task<CatalogPage<PanelDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct)
    {
        var source = db.AnalysisPanels.AsNoTracking();
        if (query.IsActive is { } active) source = source.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }
        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip(query.Offset).Take(query.PageSize)
            .Select(x => new PanelDto(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);
        return new(items, count, query.Page, query.PageSize);
    }

    public async Task<PanelDto> CreateAsync(AnalysisPanel panel, CancellationToken ct)
    {
        db.AnalysisPanels.Add(panel);
        await CatalogPersistence.SaveAsync(db, ct);
        return Dto(panel);
    }

    public async Task<PanelDto> UpdateAsync(Guid id, AnalysisPanel draft, CancellationToken ct)
    {
        var panel = await db.AnalysisPanels.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CatalogException.NotFound("Analysis panel");
        panel.Update(draft.Code, draft.Name);
        await CatalogPersistence.SaveAsync(db, ct);
        return Dto(panel);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var panel = await CatalogPersistence.LockPanelAsync(db, id, ct);
        panel.Deactivate();
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task AddAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var panel = await CatalogPersistence.LockPanelAsync(db, panelId, ct);
        var analysis = await CatalogPersistence.LockAnalysisAsync(db, analysisId, ct);
        if (!panel.IsActive || !analysis.IsActive)
            throw CatalogException.Conflict("Both the panel and analysis must be active to add an association.");
        db.PanelAnalyses.Add(new PanelAnalysis(panelId, analysisId));
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task RemoveAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await CatalogPersistence.LockPanelAsync(db, panelId, ct);
        var association = await db.PanelAnalyses.SingleOrDefaultAsync(x => x.AnalysisPanelId == panelId && x.MedicalAnalysisId == analysisId, ct)
            ?? throw CatalogException.NotFound("Panel analysis association");
        // Only the live catalogue association is removed, never requested panels/items.
        db.PanelAnalyses.Remove(association);
        await CatalogPersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
