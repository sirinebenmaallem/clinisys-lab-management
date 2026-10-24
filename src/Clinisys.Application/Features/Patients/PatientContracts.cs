using System.ComponentModel.DataAnnotations;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.Patients;

public sealed record PatientInput(
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [Required] DateOnly? DateOfBirth,
    [Required, EnumDataType(typeof(Sex))] Sex? Sex,
    [StringLength(50)] string? NationalIdentityNumber = null,
    [StringLength(30)] string? PhoneNumber = null);

public sealed record PatientUpdateInput([Required] PatientInput Patient, [Required] string Version);

// Identifiers are deliberately omitted from list results.
public sealed record PatientListDto(Guid Id, string FirstName, string LastName, DateOnly DateOfBirth, Sex Sex);
public sealed record PatientDetailDto(Guid Id, string FirstName, string LastName, DateOnly DateOfBirth,
    Sex Sex, string? NationalIdentityNumber, string? PhoneNumber, string Version);
public sealed record PatientPage(IReadOnlyList<PatientListDto> Items, int TotalCount, int Page, int PageSize);

public sealed class PatientPageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public int Offset => (Page - 1) * PageSize;
    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue || Search?.Length > 200)
            throw PatientException.Validation("query");
    }
}

public interface IPatientStore
{
    Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct);
    Task<PatientPage> SearchAsync(PatientPageQuery query, CancellationToken ct);
    Task<PatientDetailDto> CreateAsync(Patient patient, CancellationToken ct);
    Task<PatientDetailDto> UpdateAsync(Guid id, Patient draft, byte[] version, CancellationToken ct);
}
