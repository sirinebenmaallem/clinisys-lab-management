using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clinisys.Domain;

namespace Clinisys.Application.Features.LaboratoryRequests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LaboratoryRequestInput(
    Guid PatientId,
    [Required, StringLength(200)] string PrescribingPhysician,
    decimal DiscountPercent = 0,
    IReadOnlyList<Guid>? AnalysisIds = null,
    IReadOnlyList<Guid>? PanelIds = null);

public sealed record RequestPatientDto(Guid Id, string CurrentFirstName, string CurrentLastName);
public sealed record RequestItemDto(Guid Id, Guid MedicalAnalysisId, string AnalysisCodeSnapshot,
    string AnalysisNameSnapshot, string UnitSnapshot, decimal UnitPriceSnapshot,
    decimal DiscountPercentSnapshot, decimal NetPrice);
public sealed record RequestPanelDto(Guid Id, Guid AnalysisPanelId, string PanelCodeSnapshot, string PanelNameSnapshot);
public sealed record LaboratoryRequestDetailDto(Guid Id, string Number, RequestPatientDto Patient,
    DateOnly PatientDateOfBirthSnapshot, Sex PatientSexSnapshot, string PrescribingPhysician,
    decimal DiscountPercent, RequestStatus Status, DateTimeOffset RegisteredAt,
    IReadOnlyList<RequestPanelDto> Panels, IReadOnlyList<RequestItemDto> Items, decimal Total, string Version);
public sealed record LaboratoryRequestListDto(Guid Id, string Number, RequestPatientDto Patient,
    string PrescribingPhysician, RequestStatus Status, DateTimeOffset RegisteredAt, decimal Total);
public sealed record LaboratoryRequestPage(IReadOnlyList<LaboratoryRequestListDto> Items, int TotalCount, int Page, int PageSize);

public sealed class LaboratoryRequestPageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public Guid? PatientId { get; init; }
    [StringLength(30)] public string? Number { get; init; }
    [EnumDataType(typeof(RequestStatus))] public RequestStatus? Status { get; init; }
    public DateTimeOffset? RegisteredFrom { get; init; }
    public DateTimeOffset? RegisteredTo { get; init; }
    public int Offset => (Page - 1) * PageSize;
    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue ||
            PatientId == Guid.Empty || Number?.Length > 30 ||
            (Status.HasValue && !Enum.IsDefined(Status.Value)) || RegisteredFrom > RegisteredTo)
            throw LaboratoryRequestException.Validation("query");
    }
}

public interface ILaboratoryRequestStore
{
    Task<LaboratoryRequestDetailDto> CreateAsync(LaboratoryRequestInput input, CancellationToken ct);
    Task<LaboratoryRequestDetailDto?> GetAsync(Guid id, CancellationToken ct);
    Task<LaboratoryRequestPage> SearchAsync(LaboratoryRequestPageQuery query, CancellationToken ct);
}
