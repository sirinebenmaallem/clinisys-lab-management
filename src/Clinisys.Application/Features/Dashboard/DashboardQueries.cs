namespace Clinisys.Application.Features.Dashboard;

public sealed record DashboardDayDto(DateOnly Date, long RequestCount);
public sealed record DashboardAnalysisDto(Guid AnalysisId, string Code, string Name, long RequestCount);
public sealed record DashboardDto(DateOnly From, DateOnly To, long RequestCount, decimal Revenue,
    IReadOnlyList<DashboardDayDto> Days, IReadOnlyList<DashboardAnalysisDto> TopAnalyses)
{
    // Preserve cents when the total exceeds JavaScript's exact number precision.
    public string RevenueText => Revenue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class DashboardException : Exception;

public interface IDashboardStore
{
    Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed class DashboardQueries(IDashboardStore store)
{
    public Task<DashboardDto> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from is null || to is null || from == default(DateOnly) || to >= DateOnly.MaxValue
            || from > to || to.Value.DayNumber - from.Value.DayNumber > 365)
            throw new DashboardException();
        return store.GetAsync(from.Value, to.Value, ct);
    }
}
