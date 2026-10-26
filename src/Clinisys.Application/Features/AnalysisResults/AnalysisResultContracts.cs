using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clinisys.Domain;

namespace Clinisys.Application.Features.AnalysisResults;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnalysisResultInput([Required] decimal? Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnalysisResultUpdateInput([Required] decimal? Value, [Required] string? Version);

public sealed record AnalysisResultDetailDto(Guid Id, Guid LaboratoryRequestId, Guid RequestItemId,
    Guid MedicalAnalysisId, string AnalysisCodeSnapshot, string AnalysisNameSnapshot, string UnitSnapshot,
    decimal Value, decimal? ReferenceLowerBoundSnapshot, decimal? ReferenceUpperBoundSnapshot,
    Abnormality Abnormality, DateTimeOffset EnteredAt, string EnteredBy,
    DateTimeOffset? ValidatedAt, string? ValidatedBy, string Version, bool IsCalculated = false, LdlCalculationDto? LdlCalculation = null);

public sealed record AnalysisResultListItemDto(Guid RequestItemId, Guid MedicalAnalysisId,
    string AnalysisCodeSnapshot, string AnalysisNameSnapshot, string UnitSnapshot, AnalysisResultDetailDto? Result,
    AnalysisResultReferenceDto? Reference = null);

// Decimal strings preserve exact bounds in the browser, including decimal(18,6) extremes.
public sealed record AnalysisResultReferenceDto(string Status, string? LowerBound = null, string? UpperBound = null);

public sealed record AnalysisResultListDto(Guid LaboratoryRequestId, IReadOnlyList<AnalysisResultListItemDto> Items, LdlCalculationDto? LdlCalculation = null);

// Implementations must check parent/item membership and editability inside their
// transaction. Create rejects duplicates; Update requires an existing result and
// compares the supplied version. Reference selection belongs only to creation.
public interface IAnalysisResultStore
{
    Task<AnalysisResultDetailDto> CreateAsync(Guid requestId, Guid itemId, decimal value, CancellationToken ct);
    Task<AnalysisResultDetailDto> UpdateAsync(Guid requestId, Guid itemId, decimal value, byte[] version, CancellationToken ct);
    Task<AnalysisResultDetailDto?> GetAsync(Guid requestId, Guid itemId, CancellationToken ct);
    // null means the request does not exist; existing items without results are included.
    Task<AnalysisResultListDto?> ListForRequestAsync(Guid requestId, CancellationToken ct);
}
