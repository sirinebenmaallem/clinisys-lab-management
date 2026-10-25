using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;

namespace Clinisys.Tests.Authentication;

// In-memory test host registration only; no production handler accepts role/actor headers.
internal static class AuthenticatedApiTestSupport
{
    internal static readonly Guid ActorId = new("a0000000-0000-0000-0000-000000000007");
    internal static void AddTestAuthentication(this IServiceCollection services, params string[] roles)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(new TestRoles(roles));
        services.AddAuthentication(options =>
        { options.DefaultAuthenticateScheme = "Synthetic"; options.DefaultChallengeScheme = "Synthetic"; options.DefaultForbidScheme = "Synthetic"; })
            .AddScheme<AuthenticationSchemeOptions, Handler>("Synthetic", _ => { });
    }
    internal static HttpClient AuthenticatedClient(this WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var token = client.GetFromJsonAsync<AntiforgeryTokenDto>("/api/auth/antiforgery").GetAwaiter().GetResult()!;
        client.DefaultRequestHeaders.Add(token.HeaderName, token.RequestToken);
        return client;
    }
    private sealed record TestRoles(string[] Roles);
    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        TestRoles roles) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (roles.Roles.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()), new Claim(ClaimTypes.Name, "Synthetic") }
                .Concat(roles.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
        protected override Task HandleChallengeAsync(AuthenticationProperties properties) => Clinisys.API.Authentication.AuthProblems.WriteAsync(Context, 401);
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => Clinisys.API.Authentication.AuthProblems.WriteAsync(Context, 403);
    }
}
