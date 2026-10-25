using System.Text.Json.Serialization;

namespace Clinisys.Application.Features.Authentication;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginInput(string? UserName, string? Password)
{
    public override string ToString() => "LoginInput { credentials redacted }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LogoutInput;

public sealed record AuthenticationSessionDto(Guid UserId, string UserName,
    IReadOnlyList<string> Roles, DateTimeOffset ExpiresAt);

public sealed record AntiforgeryTokenDto(string RequestToken, string HeaderName);

// Future adapters own credential verification, cookie issuance and revocation.
// No implementation or active session/security mechanism is supplied in lot 07-A.
public interface IAuthenticationSessionService
{
    Task<AuthenticationSessionDto> LoginAsync(LoginInput input, CancellationToken ct);
    Task LogoutAsync(CancellationToken ct);
    Task<AuthenticationSessionDto> GetSessionAsync(CancellationToken ct);
}

public interface IAntiforgeryTokenProvider
{
    Task<AntiforgeryTokenDto> GetTokenAsync(CancellationToken ct);
}
