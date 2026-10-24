using System.ComponentModel.DataAnnotations;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.ReferenceRanges;

public sealed record ReferenceRangeInput(
    [Required, EnumDataType(typeof(Sex))] Sex? Sex,
    [Required] int? MinimumAgeDays,
    int? MaximumAgeDays,
    [Required] decimal? LowerBound,
    [Required] decimal? UpperBound);

public sealed record ReferenceRangeDto(Guid Id, Guid MedicalAnalysisId, Sex Sex,
    int MinimumAgeDays, int? MaximumAgeDays, decimal LowerBound, decimal UpperBound);

public interface IReferenceRangeStore
{
    Task<IReadOnlyList<ReferenceRangeDto>> ListAsync(Guid analysisId, CancellationToken ct);
    Task<ReferenceRangeDto?> GetAsync(Guid analysisId, Guid id, CancellationToken ct);
    Task<ReferenceRangeDto> CreateAsync(ReferenceRange range, CancellationToken ct);
    Task<ReferenceRangeDto> UpdateAsync(Guid analysisId, Guid id, ReferenceRange draft, CancellationToken ct);
    Task DeleteAsync(Guid analysisId, Guid id, CancellationToken ct);
}
