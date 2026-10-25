namespace Clinisys.Application.Features.Authentication;

public static class AuthenticationRules
{
    public const int MinimumUserNameLength = 3;
    public const int MaximumUserNameLength = 64;
    public const int MinimumPasswordLength = 15;
    public const int MaximumPasswordLength = 128;

    public static string ValidateUserName(string? userName)
    {
        var trimmed = userName?.Trim();
        if (trimmed is null || trimmed.Length is < MinimumUserNameLength or > MaximumUserNameLength
            || !IsAsciiAlphaNumeric(trimmed[0])
            || trimmed.Any(c => !IsAsciiAlphaNumeric(c) && c is not ('.' or '_' or '-')))
            throw AuthenticationException.Validation("userName");
        return trimmed;
    }

    public static string NormalizeUserName(string? userName) => ValidateUserName(userName).ToUpperInvariant();

    public static string ValidatePassword(string? password)
    {
        // Length is measured in .NET string characters (UTF-16 code units).
        // Never trim, normalize or change case, including whitespace.
        if (password is null || password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            throw AuthenticationException.Validation("password");
        return password;
    }

    public static LoginInput ValidateLogin(LoginInput? input) =>
        new(ValidateUserName(input?.UserName), ValidatePassword(input?.Password));

    // Explicit account/session role assignments reject unknown values instead of
    // silently granting defaults. Permission calculation independently ignores unknown roles.
    public static IReadOnlyList<string> ValidateRoles(IEnumerable<string>? roles)
    {
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles ?? [])
        {
            if (!AuthenticationRoles.IsKnown(role)) throw AuthenticationException.Validation("roles");
            validated.Add(role);
        }
        if (validated.Count == 0) throw AuthenticationException.Validation("roles");
        return Array.AsReadOnly(validated.Order(StringComparer.Ordinal).ToArray());
    }

    // Caller supplies time; no clock, database or session issuance is hidden here.
    public static AuthenticationSessionDto ValidateSession(AuthenticationSessionDto? session, DateTimeOffset now)
    {
        if (session is null || session.UserId == Guid.Empty || session.ExpiresAt.Offset != TimeSpan.Zero
            || session.ExpiresAt <= now)
            throw AuthenticationException.Validation("session");
        return new(session.UserId, ValidateUserName(session.UserName), ValidateRoles(session.Roles), session.ExpiresAt);
    }

    private static bool IsAsciiAlphaNumeric(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
