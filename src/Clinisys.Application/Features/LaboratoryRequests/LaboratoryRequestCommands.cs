namespace Clinisys.Application.Features.LaboratoryRequests;

public sealed class LaboratoryRequestCommands(ILaboratoryRequestStore store)
{
    public const int MaximumSelection = 500;
    public Task<LaboratoryRequestDetailDto> CreateAsync(LaboratoryRequestInput input, CancellationToken ct)
    {
        if (input is null || input.PatientId == Guid.Empty || string.IsNullOrWhiteSpace(input.PrescribingPhysician) ||
            input.PrescribingPhysician.Length > 200 || input.DiscountPercent is < 0 or > 100 ||
            decimal.Round(input.DiscountPercent, 2) != input.DiscountPercent ||
            (input.AnalysisIds?.Count ?? 0) + (long)(input.PanelIds?.Count ?? 0) > MaximumSelection ||
            input.AnalysisIds?.Any(x => x == Guid.Empty) == true || input.PanelIds?.Any(x => x == Guid.Empty) == true ||
            (input.AnalysisIds?.Count ?? 0) + (input.PanelIds?.Count ?? 0) == 0)
            throw LaboratoryRequestException.Validation("request");
        return store.CreateAsync(input with
        {
            PrescribingPhysician = input.PrescribingPhysician.Trim(),
            AnalysisIds = input.AnalysisIds?.Distinct().Order().ToArray() ?? [],
            PanelIds = input.PanelIds?.Distinct().Order().ToArray() ?? []
        }, ct);
    }
}
