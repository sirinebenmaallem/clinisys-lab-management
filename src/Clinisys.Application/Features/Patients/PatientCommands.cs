using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.Patients;

public sealed class PatientCommands(IPatientStore store)
{
    public Task<PatientDetailDto> CreateAsync(PatientInput input, CancellationToken ct) => store.CreateAsync(Draft(input), ct);
    public Task<PatientDetailDto> UpdateAsync(Guid id, PatientUpdateInput input, CancellationToken ct)
    {
        byte[] version;
        try { version = Convert.FromBase64String(input.Version ?? ""); }
        catch (FormatException) { throw PatientException.Validation("version"); }
        if (version.Length != 8) throw PatientException.Validation("version");
        return store.UpdateAsync(id, Draft(input.Patient), version, ct);
    }

    private static Patient Draft(PatientInput input)
    {
        if (input is null) throw PatientException.Validation("patient");
        try
        {
            return new Patient(input.FirstName, input.LastName,
                input.DateOfBirth ?? throw new ArgumentException(null, "dateOfBirth"),
                input.Sex ?? throw new ArgumentException(null, "sex"), input.NationalIdentityNumber, input.PhoneNumber);
        }
        catch (ArgumentException ex) { throw PatientException.Validation(ex.ParamName ?? "identifiers"); }
    }
}
