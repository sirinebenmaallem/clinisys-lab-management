namespace Clinisys.Application.Features.Patients;

public enum PatientError { Validation, NotFound, Conflict }
public sealed class PatientException(PatientError error, string message, string? field = null) : Exception(message)
{
    public PatientError Error { get; } = error;
    public string? Field { get; } = field;
    public static PatientException Validation(string field) => new(PatientError.Validation, "Patient input is missing or invalid.", field);
    public static PatientException NotFound() => new(PatientError.NotFound, "Patient was not found.");
    public static PatientException Conflict() => new(PatientError.Conflict, "The patient could not be saved because of a duplicate identifier or concurrent change. Reload and review before retrying.");
}
