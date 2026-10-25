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

    public void EnsureResultsEditable()
    {
        if (Status is not (RequestStatus.Registered or RequestStatus.InProgress or RequestStatus.ResultsEntered)
            || ValidatedAt is not null || ValidatedBy is not null || DeliveredAt is not null)
            throw new InvalidOperationException("The request does not allow result entry or correction.");
    }

    public void StartProcessing()
    {
        EnsureTransition(RequestStatus.Registered);
        Status = RequestStatus.InProgress;
    }

    public void MarkResultsEntered(bool allResultsPresent)
    {
        EnsureTransition(RequestStatus.InProgress);
        if (!allResultsPresent) throw new InvalidOperationException("All requested results are required.");
        Status = RequestStatus.ResultsEntered;
    }

    public void ValidateResults(bool allResultsPresent, string actor, DateTimeOffset instant)
    {
        EnsureTransition(RequestStatus.ResultsEntered);
        if (!allResultsPresent) throw new InvalidOperationException("All requested results are required.");
        var validActor = Guard.Required(actor, 100, nameof(actor));
        if (instant.Offset != TimeSpan.Zero || instant < RegisteredAt)
            throw new ArgumentException("Invalid validation instant.", nameof(instant));
        ValidatedBy = validActor;
        ValidatedAt = instant;
        Status = RequestStatus.Validated;
    }

    public void Deliver(DateTimeOffset instant)
    {
        if (Status != RequestStatus.Validated || ValidatedAt is null || ValidatedBy is null || DeliveredAt is not null)
            throw new InvalidOperationException("Only a validated request can be delivered.");
        if (instant.Offset != TimeSpan.Zero || instant < ValidatedAt)
            throw new ArgumentException("Invalid delivery instant.", nameof(instant));
        DeliveredAt = instant;
        Status = RequestStatus.Delivered;
    }

    private void EnsureTransition(RequestStatus expected)
    {
        EnsureResultsEditable();
        if (Status != expected) throw new InvalidOperationException("The workflow transition is not allowed.");
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
