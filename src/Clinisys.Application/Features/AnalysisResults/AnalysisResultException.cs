namespace Clinisys.Application.Features.AnalysisResults;

public enum AnalysisResultError { Validation, NotFound, Conflict, Internal, CommitUnknown }

public sealed class AnalysisResultException(AnalysisResultError error, string message, string? field = null) : Exception(message)
{
    public AnalysisResultError Error { get; } = error;
    public string? Field { get; } = field;
    public static AnalysisResultException Validation(string field) =>
        new(AnalysisResultError.Validation, "Result input is missing or invalid.", field);
    public static AnalysisResultException NotFound() =>
        new(AnalysisResultError.NotFound, "A requested resource was not found.");
    public static AnalysisResultException Conflict() =>
        new(AnalysisResultError.Conflict, "The result cannot be saved in its current state or changed concurrently.");
    public static AnalysisResultException AmbiguousReference() =>
        new(AnalysisResultError.Conflict, "More than one reference range applies to this result.");
    public static AnalysisResultException IncompatibleUnit() =>
        new(AnalysisResultError.Conflict, "The requested measurement unit is incompatible with the applicable catalogue reference.");
    public static AnalysisResultException Internal() =>
        new(AnalysisResultError.Internal, "An internal result data inconsistency occurred.");
    public static AnalysisResultException Unknown() =>
        new(AnalysisResultError.CommitUnknown, "The transaction outcome could not be confirmed. Verify the result before submitting again.");
}
