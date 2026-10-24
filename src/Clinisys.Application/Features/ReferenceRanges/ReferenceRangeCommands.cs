using Clinisys.Application.Common;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.ReferenceRanges;

public sealed class ReferenceRangeCommands(IReferenceRangeStore store)
{
    public Task<ReferenceRangeDto> CreateAsync(Guid analysisId, ReferenceRangeInput input, CancellationToken ct) =>
        store.CreateAsync(Draft(analysisId, input), ct);
    public Task<ReferenceRangeDto> UpdateAsync(Guid analysisId, Guid id, ReferenceRangeInput input, CancellationToken ct) =>
        store.UpdateAsync(analysisId, id, Draft(analysisId, input), ct);
    public Task DeleteAsync(Guid analysisId, Guid id, CancellationToken ct) => store.DeleteAsync(analysisId, id, ct);

    private static ReferenceRange Draft(Guid analysisId, ReferenceRangeInput input) => CatalogValidation.Domain(() =>
        new ReferenceRange(analysisId,
            input.Sex ?? throw new ArgumentException("Sex is required.", "sex"),
            input.MinimumAgeDays ?? throw new ArgumentException("Minimum age is required.", "minimumAgeDays"),
            input.MaximumAgeDays,
            new ReferenceInterval(input.LowerBound ?? throw new ArgumentException("Lower bound is required.", "lowerBound"),
                input.UpperBound ?? throw new ArgumentException("Upper bound is required.", "upperBound"))));
}
