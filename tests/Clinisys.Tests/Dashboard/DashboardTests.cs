using Clinisys.Application.Features.Dashboard;

namespace Clinisys.Tests.Dashboard;

public sealed class DashboardTests
{
    [Fact]
    public void RevenueText_PreservesCentsExactly() => Assert.Equal("9999999999999999.99",
        new DashboardDto(new(2026, 1, 1), new(2026, 1, 1), 1, 9999999999999999.99m, [], []).RevenueText);

    [Theory]
    [InlineData(null, "2026-01-01")]
    [InlineData("2026-01-01", null)]
    [InlineData("2026-02-01", "2026-01-01")]
    [InlineData("2024-01-01", "2025-01-01")]
    [InlineData("0001-01-01", "0001-01-02")]
    [InlineData("9999-12-31", "9999-12-31")]
    public async Task InvalidPeriods_RejectBeforeStore(string? from, string? to)
    {
        var store = new Store();
        await Assert.ThrowsAsync<DashboardException>(() => new DashboardQueries(store).GetAsync(
            from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), default));
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-01")]
    [InlineData("2024-01-01", "2024-12-31")]
    public async Task InclusiveDayAndLeapYear_AreAccepted(string from, string to)
    {
        var store = new Store();
        var result = await new DashboardQueries(store).GetAsync(DateOnly.Parse(from), DateOnly.Parse(to), default);
        Assert.Equal(DateOnly.Parse(from), result.From);
        Assert.Equal(DateOnly.Parse(to), result.To);
        Assert.Equal(1, store.Calls);
    }

    private sealed class Store : IDashboardStore
    {
        public int Calls;
        public Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct)
        { Calls++; return Task.FromResult(new DashboardDto(from, to, 0, 0, [], [])); }
    }
}
