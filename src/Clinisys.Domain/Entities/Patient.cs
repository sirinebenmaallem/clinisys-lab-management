using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class Patient : Entity
{
    private Patient() { }

    public Patient(string firstName, string lastName, DateOnly dateOfBirth, Sex sex,
        string? nationalIdentityNumber = null, string? phoneNumber = null)
        => Update(firstName, lastName, dateOfBirth, sex, nationalIdentityNumber, phoneNumber);

    public void Update(string firstName, string lastName, DateOnly dateOfBirth, Sex sex,
        string? nationalIdentityNumber = null, string? phoneNumber = null)
    {
        var validFirstName = Guard.Required(firstName, 100, nameof(firstName));
        var validLastName = Guard.Required(lastName, 100, nameof(lastName));
        if (dateOfBirth == default || dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentOutOfRangeException(nameof(dateOfBirth));
        var validSex = Guard.EnumValue(sex, nameof(sex));
        var validIdentity = Guard.Optional(nationalIdentityNumber, 50, nameof(nationalIdentityNumber))?.ToUpperInvariant();
        var validPhone = Guard.Optional(phoneNumber, 30, nameof(phoneNumber));
        if (validIdentity is null && validPhone is null)
            throw new ArgumentException("A national identity number or phone number is required.");
        (FirstName, LastName, DateOfBirth, Sex, NationalIdentityNumber, PhoneNumber) =
            (validFirstName, validLastName, dateOfBirth, validSex, validIdentity, validPhone);
    }

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public Sex Sex { get; private set; }
    public string? NationalIdentityNumber { get; private set; }
    public string? PhoneNumber { get; private set; }
}
