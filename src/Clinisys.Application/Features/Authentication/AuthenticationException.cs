namespace Clinisys.Application.Features.Authentication;

public enum AuthenticationError { Validation, InvalidCredentials, Unauthenticated }

public sealed class AuthenticationException : Exception
{
    private AuthenticationException(AuthenticationError error, string message, string? field = null) : base(message)
    { Error = error; Field = field; }

    public AuthenticationError Error { get; }
    public string? Field { get; }

    public static AuthenticationException Validation(string field) => new(AuthenticationError.Validation,
        "Authentication input is missing or invalid.", field is "userName" or "password" or "roles" or "session" ? field : "authentication");
    public static AuthenticationException InvalidCredentials() => new(AuthenticationError.InvalidCredentials, "Authentication failed.");
    public static AuthenticationException Unauthenticated() => new(AuthenticationError.Unauthenticated, "Authentication is required.");
}
