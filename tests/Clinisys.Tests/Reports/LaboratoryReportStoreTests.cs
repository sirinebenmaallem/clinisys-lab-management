using System.Data.Common;
using Clinisys.Infrastructure.LaboratoryReports;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.Reports;

public sealed class LaboratoryReportStoreTests
{
    [Fact]
    public async Task ReadProjectionCompiles_AndNeverOpensPhysicalConnectionInUnitTest()
    {
        var guard = new Guard();
        await using var db = new ClinisysDbContext(new DbContextOptionsBuilder<ClinisysDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=UnitTest;Integrated Security=True")
            .AddInterceptors(guard).Options);
        await Assert.ThrowsAsync<Blocked>(() => new LaboratoryReportStore(db).GetAsync(Guid.NewGuid(), default));
        Assert.Equal(1, guard.Attempts);
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private sealed class Blocked : InvalidOperationException;
    private sealed class Guard : DbConnectionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Attempts++; throw new Blocked(); }
    }
}
