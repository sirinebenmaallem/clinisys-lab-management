using Clinisys.Application.Common;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Catalog;

internal static class CatalogPersistence
{
    // All API writers for an analysis/ranges take this same parent lock in a transaction.
    // This serializes overlap checks even when no range exists yet.
    internal static async Task<MedicalAnalysis> LockAnalysisAsync(ClinisysDbContext db, Guid id, CancellationToken ct) =>
        await db.MedicalAnalyses.FromSqlInterpolated($"SELECT * FROM dbo.MedicalAnalyses WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(ct) ?? throw CatalogException.NotFound("Medical analysis");

    internal static async Task<AnalysisPanel> LockPanelAsync(ClinisysDbContext db, Guid id, CancellationToken ct) =>
        await db.AnalysisPanels.FromSqlInterpolated($"SELECT * FROM dbo.AnalysisPanels WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(ct) ?? throw CatalogException.NotFound("Analysis panel");

    internal static async Task SaveAsync(ClinisysDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw CatalogException.Conflict("The catalogue changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw CatalogException.Conflict("The code or association already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
        {
            throw CatalogException.Conflict("The catalogue changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw CatalogException.Conflict("A related record or database constraint prevents this change.");
        }
    }
}
