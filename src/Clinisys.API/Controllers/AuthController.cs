using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAntiforgeryTokenProvider tokens) : ControllerBase
{
    [HttpGet("antiforgery"), AllowAnonymous]
    public Task<AntiforgeryTokenDto> Antiforgery(CancellationToken ct) => tokens.GetTokenAsync(ct);

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public Task<AuthenticationSessionDto> Login(LoginInput input, [FromServices] IAuthenticationSessionService sessions, CancellationToken ct) => sessions.LoginAsync(input, ct);

    [HttpGet("session"), Authorize]
    public Task<AuthenticationSessionDto> Session([FromServices] IAuthenticationSessionService sessions, CancellationToken ct) => sessions.GetSessionAsync(ct);

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(LogoutInput input, [FromServices] IAuthenticationSessionService sessions, CancellationToken ct)
    { await sessions.LogoutAsync(ct); return NoContent(); }
}
