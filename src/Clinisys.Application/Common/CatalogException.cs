namespace Clinisys.Application.Common;

public enum CatalogError { Validation, NotFound, Conflict }

public sealed class CatalogException(CatalogError error, string message,
    IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public CatalogError Error { get; } = error;
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    public static CatalogException NotFound(string resource) => new(CatalogError.NotFound, $"{resource} was not found.");
    public static CatalogException Conflict(string message) => new(CatalogError.Conflict, message);
}

public static class CatalogValidation
{
    public static T Domain<T>(Func<T> create)
    {
        try { return create(); }
        catch (ArgumentException ex)
        {
            // Use a fixed message: ArgumentOutOfRangeException.Message may include the input value.
            var key = ex.ParamName ?? "range";
            throw new CatalogException(CatalogError.Validation, "Invalid catalogue input.",
                new Dictionary<string, string[]> { [key] = ["The value is missing, invalid, or exceeds the supported length/precision."] });
        }
    }
}
