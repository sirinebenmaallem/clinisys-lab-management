using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class LaboratoryRequest : Entity
{
    private LaboratoryRequest() { }
    public static string FormatNumber(int year, int value)
    {
        if (year is < 1 or > 9999 || value is < 1 or > 999999)
            throw new ArgumentOutOfRangeException(nameof(value));
        return FormattableString.Invariant($"LAB-{year:D4}-{value:D6}");
    }

    public static LaboratoryRequest Register(string number, Patient patient, string prescribingPhysician,
        decimal discountPercent, DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(patient);
        if (registeredAt.Offset != TimeSpan.Zero || number.Length != 15 ||
            !int.TryParse(number.AsSpan(9), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            number != FormatNumber(registeredAt.Year, value))
            throw new ArgumentException("Invalid registration number or UTC instant.", nameof(number));
        if (patient.DateOfBirth > DateOnly.FromDateTime(registeredAt.UtcDateTime))
            throw new ArgumentException("Birth date must not follow registration.", nameof(registeredAt));
        return new(number, patient, prescribingPhysician, discountPercent, registeredAt);
    }

    public LaboratoryRequest(string number, Patient patient, string prescribingPhysician, decimal discountPercent = 0)
        : this(number, patient, prescribingPhysician, discountPercent, DateTimeOffset.UtcNow) { }

    private LaboratoryRequest(string number, Patient patient, string prescribingPhysician, decimal discountPercent, DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(patient);
        Number = Guard.Required(number, 30, nameof(number)).ToUpperInvariant();
        PatientId = patient.Id;
        PatientDateOfBirthSnapshot = patient.DateOfBirth;
        PatientSexSnapshot = patient.Sex;
        PrescribingPhysician = Guard.Required(prescribingPhysician, 200, nameof(prescribingPhysician));
        DiscountPercent = Guard.Discount(discountPercent, nameof(discountPercent));
        RegisteredAt = registeredAt;
    }

    public string Number { get; private set; } = null!;
    public Guid PatientId { get; private set; }
    public DateOnly PatientDateOfBirthSnapshot { get; private set; }
    public Sex PatientSexSnapshot { get; private set; }
    public string PrescribingPhysician { get; private set; } = null!;
    public decimal DiscountPercent { get; private set; }
    public RequestStatus Status { get; private set; } = RequestStatus.Registered;
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? ValidatedBy { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
}
