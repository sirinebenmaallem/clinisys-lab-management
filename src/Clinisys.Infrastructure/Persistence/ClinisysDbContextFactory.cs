using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clinisys.Infrastructure.Persistence;

public sealed class ClinisysDbContextFactory : IDesignTimeDbContextFactory<ClinisysDbContext>
{
    public ClinisysDbContext CreateDbContext(string[] args)
    {
        // Explicit configuration only: no credentials or silent server fallback.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ClinisysDb")
            ?? throw new InvalidOperationException("Set ConnectionStrings__ClinisysDb before running EF tools.");
        return new ClinisysDbContext(new DbContextOptionsBuilder<ClinisysDbContext>()
            .UseSqlServer(connectionString).Options);
    }
}
