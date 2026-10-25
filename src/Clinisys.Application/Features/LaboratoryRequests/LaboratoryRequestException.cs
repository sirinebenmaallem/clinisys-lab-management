namespace Clinisys.Application.Features.LaboratoryRequests;

public enum LaboratoryRequestError { Validation, NotFound, Conflict, Internal, CommitUnknown }
public sealed class LaboratoryRequestException(LaboratoryRequestError error, string message, string? field = null) : Exception(message)
{
    public LaboratoryRequestError Error { get; } = error;
    public string? Field { get; } = field;
    public static LaboratoryRequestException Validation(string field) => new(LaboratoryRequestError.Validation, "Request input is missing or invalid.", field);
    public static LaboratoryRequestException NotFound() => new(LaboratoryRequestError.NotFound, "A requested resource was not found.");
    public static LaboratoryRequestException Conflict() => new(LaboratoryRequestError.Conflict, "The selection is unavailable, empty, inactive, or changed concurrently.");
    public static LaboratoryRequestException WorkflowConflict() => new(LaboratoryRequestError.Conflict,
        "The workflow state, required results or version does not permit this operation.");
    public static LaboratoryRequestException WorkflowUnknown() => new(LaboratoryRequestError.CommitUnknown,
        "The transaction outcome could not be confirmed. Read the request before submitting again.");
    public static LaboratoryRequestException Exhausted() => new(LaboratoryRequestError.Conflict, "Annual request numbering capacity has been reached.");
    public static LaboratoryRequestException Internal() => new(LaboratoryRequestError.Internal, "An internal numbering or persistence inconsistency occurred.");
    public static LaboratoryRequestException Unknown() => new(LaboratoryRequestError.CommitUnknown,
        "The transaction outcome could not be confirmed. Verify whether the request exists before submitting again.");
}
