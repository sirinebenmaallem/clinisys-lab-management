using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class LaboratoryRequest : Entity
{
    private LaboratoryRequest() { }
    public LaboratoryRequest(string number, Patient patient, string prescribingPhysician, decimal discountPercent = 0)
    {
        ArgumentNullException.ThrowIfNull(patient);
        Number = Guard.Required(number, 30, nameof(number)).ToUpperInvariant();
        PatientId = patient.Id;
        PatientDateOfBirthSnapshot = patient.DateOfBirth;
        PatientSexSnapshot = patient.Sex;
        PrescribingPhysician = Guard.Required(prescribingPhysician, 200, nameof(prescribingPhysician));
        DiscountPercent = Guard.Discount(discountPercent, nameof(discountPercent));
    }

    public string Number { get; private set; } = null!;
    public Guid PatientId { get; private set; }
    public DateOnly PatientDateOfBirthSnapshot { get; private set; }
    public Sex PatientSexSnapshot { get; private set; }
    public string PrescribingPhysician { get; private set; } = null!;
    public decimal DiscountPercent { get; private set; }
    public RequestStatus Status { get; private set; } = RequestStatus.Registered;
    public DateTimeOffset RegisteredAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? ValidatedBy { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
}
