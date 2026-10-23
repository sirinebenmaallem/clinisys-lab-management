using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class Patient : Entity
{
    private Patient() { }

    public Patient(string firstName, string lastName, DateOnly dateOfBirth, Sex sex,
        string? nationalIdentityNumber = null, string? phoneNumber = null)
    {
        FirstName = Guard.Required(firstName, 100, nameof(firstName));
        LastName = Guard.Required(lastName, 100, nameof(lastName));
        if (dateOfBirth == default || dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentOutOfRangeException(nameof(dateOfBirth));
        DateOfBirth = dateOfBirth;
        Sex = Guard.EnumValue(sex, nameof(sex));
        NationalIdentityNumber = Guard.Optional(nationalIdentityNumber, 50, nameof(nationalIdentityNumber))?.ToUpperInvariant();
        PhoneNumber = Guard.Optional(phoneNumber, 30, nameof(phoneNumber));
        if (NationalIdentityNumber is null && PhoneNumber is null)
            throw new ArgumentException("A national identity number or phone number is required.");
    }

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public Sex Sex { get; private set; }
    public string? NationalIdentityNumber { get; private set; }
    public string? PhoneNumber { get; private set; }
}
