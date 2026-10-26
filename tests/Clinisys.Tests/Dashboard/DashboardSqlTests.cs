using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Dashboard;
using Clinisys.Tests.AnalysisResults;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.Dashboard;

[Collection(AnalysisResultSqlCollection.Name)]
public sealed class DashboardSqlTests
{
    [SqlServerFact]
    public async Task Procedure_UsesUtcInclusivePeriod_NetLineRounding_AndStableRanking()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var provisional = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var from = new DateOnly(2080, 2, 28); var to = new DateOnly(2080, 2, 29);
        var store = new DashboardStore(db);
        Assert.Equal(0, (await store.GetAsync(from, to, default)).RequestCount);
        var requests = await db.LaboratoryRequests.Where(x => x.Id == fixture.Request.Id || x.Id == fixture.OtherRequest.Id
            || x.Id == provisional.Request.Id || x.Id == provisional.OtherRequest.Id).ToDictionaryAsync(x => x.Id);
        Date(requests[fixture.Request.Id], new(2080, 2, 28, 0, 0, 0, TimeSpan.Zero));
        Date(requests[fixture.OtherRequest.Id], new(2080, 2, 29, 23, 59, 59, TimeSpan.Zero));
        Date(requests[provisional.Request.Id], new(2080, 2, 28, 23, 30, 0, TimeSpan.FromHours(-1)));
        Date(requests[provisional.OtherRequest.Id], new(2080, 3, 1, 0, 0, 0, TimeSpan.Zero));
        Validate(requests[fixture.Request.Id], false);
        Validate(requests[fixture.OtherRequest.Id], true);
        requests[provisional.Request.Id].StartProcessing();
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET DiscountPercent=75 WHERE Id={fixture.Request.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET UnitPriceSnapshot=0.05, DiscountPercentSnapshot=10 WHERE Id={fixture.Item.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET UnitPriceSnapshot=12.35, DiscountPercentSnapshot=10 WHERE Id={fixture.EmptyItem.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET UnitPriceSnapshot=20, DiscountPercentSnapshot=25 WHERE Id={fixture.ForeignItem.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=999 WHERE Id={fixture.Analysis.Id}");
        db.ChangeTracker.Clear();
        var result = await store.GetAsync(from, to, default);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(26.17m, result.Revenue); // .045 -> .05; 11.115 -> 11.12; 20 * .75 -> 15
        Assert.Equal(new long[] { 1, 2 }, result.Days.Select(x => x.RequestCount));
        Assert.Equal(new[] { from, to }, result.Days.Select(x => x.Date));
        Assert.Equal(2, result.TopAnalyses.Single(x => x.AnalysisId == fixture.Analysis.Id).RequestCount);
        Assert.Equal(1, result.TopAnalyses.Single(x => x.AnalysisId == provisional.Analysis.Id).RequestCount);
        Assert.Equal(result.TopAnalyses.OrderByDescending(x => x.RequestCount).ThenBy(x => x.Code, StringComparer.Ordinal).Select(x => x.AnalysisId),
            result.TopAnalyses.Select(x => x.AnalysisId));
        Assert.Empty(db.ChangeTracker.Entries());
        var singleDay = await store.GetAsync(to, to, default);
        Assert.Equal(2, singleDay.RequestCount); Assert.Equal(15, singleDay.Revenue);
        await tx.RollbackAsync();
    }

    [SqlServerFact]
    public async Task Procedure_ExcludesProvisionalStatusesFromRevenue_AndSupportsFullDiscount()
    {
        foreach (var status in Enum.GetValues<RequestStatus>())
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id);
            Date(request, new(2081, 1, 1, 0, 0, 0, TimeSpan.Zero));
            if (status >= RequestStatus.InProgress) request.StartProcessing();
            if (status >= RequestStatus.ResultsEntered) request.MarkResultsEntered(true);
            if (status >= RequestStatus.Validated) request.ValidateResults(true, "SYN-STATS", new(2081, 1, 2, 0, 0, 0, TimeSpan.Zero));
            if (status == RequestStatus.Delivered) request.Deliver(new(2081, 1, 3, 0, 0, 0, TimeSpan.Zero));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET DiscountPercentSnapshot=100 WHERE Id={fixture.Item.Id}");
            var result = await new DashboardStore(db).GetAsync(new(2081, 1, 1), new(2081, 1, 1), default);
            Assert.Equal(1, result.RequestCount);
            Assert.Equal(status >= RequestStatus.Validated ? 10 : 0, result.Revenue);
            await tx.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task EmptyPeriod_ReturnsZerosAndEmptyCollections()
    {
        await using var db = SqlServerTestDatabase.CreateContext();
        var result = await new DashboardStore(db).GetAsync(new(2, 1, 1), new(2, 1, 2), default);
        Assert.Equal(0, result.RequestCount); Assert.Equal(0, result.Revenue);
        Assert.Empty(result.Days); Assert.Empty(result.TopAnalyses);
    }

    [SqlServerFact]
    public async Task TopTen_TiesUseOrdinalCode_AndCountEachRequestedAnalysisOnce()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id);
        Date(request, new(2082, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var analyses = Enumerable.Range(0, 12).Select(i => new MedicalAnalysis($"AAA{i:D2}{Guid.NewGuid():N}"[..25], "Synthetic stats", "unit", 1)).ToArray();
        db.MedicalAnalyses.AddRange(analyses);
        db.RequestItems.AddRange(analyses.Select(a => new RequestItem(request, a)));
        await db.SaveChangesAsync();
        var result = await new DashboardStore(db).GetAsync(new(2082, 1, 1), new(2082, 1, 1), default);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal(10, result.TopAnalyses.Count);
        Assert.All(result.TopAnalyses, a => Assert.Equal(1, a.RequestCount));
        Assert.Equal(analyses.Select(a => a.Code).Order(StringComparer.Ordinal).Take(10), result.TopAnalyses.Select(a => a.Code));
        await tx.RollbackAsync();
    }

    [SqlServerFact]
    public async Task LargeSnapshotPrice_MatchesExistingDomainNetPriceWithoutDoubleDiscount()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id);
        Date(request, new(2079, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Validate(request, false);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET UnitPriceSnapshot=9999999999999999.99, DiscountPercentSnapshot=99.99 WHERE Id={fixture.Item.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET DiscountPercentSnapshot=100 WHERE Id={fixture.EmptyItem.Id}");
        var item = await db.RequestItems.AsNoTracking().SingleAsync(x => x.Id == fixture.Item.Id);
        var result = await new DashboardStore(db).GetAsync(new(2079, 1, 1), new(2079, 1, 1), default);
        Assert.Equal(item.NetPrice, result.Revenue);
        await tx.RollbackAsync();
    }

    private static void Date(LaboratoryRequest request, DateTimeOffset date) =>
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!.SetValue(request, date);
    private static void Validate(LaboratoryRequest request, bool delivered)
    {
        request.StartProcessing(); request.MarkResultsEntered(true);
        request.ValidateResults(true, "SYN-STATS", new(2080, 3, 2, 0, 0, 0, TimeSpan.Zero));
        if (delivered) request.Deliver(new(2080, 3, 3, 0, 0, 0, TimeSpan.Zero));
    }
}
