using System.Net;
using System.Net.Http.Json;
using System.Text;
using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clinisys.Tests.Authentication;

public sealed class AuthenticationHttpTests
{
    private static async Task Csrf(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var token = (await client.GetFromJsonAsync<AntiforgeryTokenDto>("/api/auth/antiforgery"))!;
        client.DefaultRequestHeaders.Add(token.HeaderName, token.RequestToken);
    }
    private static Task<HttpResponseMessage> Login(HttpClient client, string? password = null) =>
        client.PostAsJsonAsync("/api/auth/login", new { userName = "synthetic.user", password = password ?? IdentityHttpTestHost.Password });

    [Fact]
    public async Task LoginSessionLogout_UseSecureCookiesAndRevokeOtherClients()
    {
        using var host = new IdentityHttpTestHost();
        using var first = host.Client(); using var second = host.Client();
        await Csrf(first); await Csrf(second);
        using var response = await Login(first);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith("__Host-Clinisys.Auth="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        var session = (await response.Content.ReadFromJsonAsync<AuthenticationSessionDto>())!;
        Assert.Equal(host.Account.Id, session.UserId);
        Assert.Equal(host.Time.GetUtcNow().AddMinutes(30), session.ExpiresAt);
        Assert.Equal(HttpStatusCode.OK, (await Login(second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/session")).StatusCode);
        // An anonymous CSRF token must not be reused after identity changes.
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        await Csrf(first);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task LoginRequiresCsrfAndStrictCredentialContract()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client)).StatusCode);
        await Csrf(client);
        using var invalid = await client.PostAsync("/api/auth/login", new StringContent(
            "{\"userName\":\"synthetic.user\",\"password\":\"SYN-PRIVATE\",\"role\":\"Biologist\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain("SYN-PRIVATE", await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/register", new { })).StatusCode);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("disabled")]
    [InlineData("no-role")]
    [InlineData("unknown-role")]
    public async Task InvalidAccounts_AreGenericAndNeverIssueCookie(string reason)
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client();
        if (reason == "disabled") host.Account.IsActive = false;
        if (reason == "no-role") host.Roles.Clear();
        if (reason == "unknown-role") host.Roles = new List<string> { "Unknown" };
        await Csrf(client);
        using var response = await Login(client, reason == "password" ? "synthetic invalid password" : null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(host.Account.PasswordHash!, text);
        Assert.DoesNotContain(host.Account.SecurityStamp!, text);
        Assert.DoesNotContain("disabled", text);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(v => v.StartsWith("__Host-Clinisys.Auth=")));
    }

    [Fact]
    public async Task FiveFailures_LockAccountAndCorrectPasswordCannotBypassIt()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client(); await Csrf(client);
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "synthetic invalid password")).StatusCode);
        Assert.Equal(0, host.Account.AccessFailedCount);
        Assert.InRange(host.Account.LockoutEnd!.Value, start.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client)).StatusCode);
    }

    [Fact]
    public async Task AbsoluteExpiry_DoesNotSlideAfterActivity()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client(); await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await Login(client)).StatusCode);
        host.Time.Advance(TimeSpan.FromMinutes(20));
        using var active = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.Contains("Set-Cookie"));
        host.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("disabled")]
    [InlineData("no-role")]
    public async Task AccountChanges_InvalidateNextRequest(string change)
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client(); await Csrf(client); await Login(client);
        if (change == "stamp") host.Account.SecurityStamp = Guid.NewGuid().ToString("N");
        if (change == "disabled") host.Account.IsActive = false;
        if (change == "no-role") host.Roles.Clear();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task KnownRoleChange_RejectsOldCookieEvenWithoutManualStampChange()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client();
        await Csrf(client); await Login(client);
        host.Roles = new List<string> { "Technician" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task OfficialPasswordReset_RevokesExistingCookie()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client();
        await Csrf(client); await Login(client);
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Clinisys.Infrastructure.Authentication.ApplicationUser>>();
        var user = (await users.FindByIdAsync(host.Account.Id.ToString()))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Assert.True((await users.ResetPasswordAsync(user, token, "Synthetic replacement password 07")).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData("Secretary", "/api/patients/10000000-0000-0000-0000-000000000001", false)]
    [InlineData("Technician", "/api/patients/10000000-0000-0000-0000-000000000001", true)]
    [InlineData("Biologist", "/api/patients/10000000-0000-0000-0000-000000000001", true)]
    [InlineData("Secretary", "/api/laboratory-requests/10000000-0000-0000-0000-000000000001/results", true)]
    public async Task PatientDetailsAndResults_AreProtectedBeforeMedicalPersistence(string role, string route, bool denied)
    {
        using var host = new IdentityHttpTestHost(); host.Roles = new List<string> { role };
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        await Csrf(client); await Login(client);
        using var response = await client.GetAsync(route);
        if (denied) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        else Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RateLimit_Is429WithNoQueueOrRedirect()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client(); await Csrf(client);
        for (var i = 0; i < 10; i++) await Login(client, "synthetic invalid password");
        using var response = await Login(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("Secretary")]
    [InlineData("Technician")]
    [InlineData("Biologist")]
    public async Task PatientSearch_PreservesIdentifierSearchButNeverReturnsIdentifiers(string role)
    {
        using var host = new IdentityHttpTestHost(); host.Roles = new List<string> { role };
        using var client = host.Client(); await Csrf(client); await Login(client);
        using var response = await client.GetAsync("/api/patients?search=SYN-CIN");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SYN-CIN", host.LastPatientSearch);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalIdentityNumber", json);
        Assert.DoesNotContain("phoneNumber", json);
        Assert.DoesNotContain("SYN-CIN", json);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("Secretary", "/api/medical-analyses", 403)]
    [InlineData("Technician", "/api/medical-analyses", 403)]
    [InlineData("Biologist", "/api/medical-analyses", 400)]
    [InlineData("Secretary", "/api/patients", 400)]
    [InlineData("Technician", "/api/patients", 403)]
    [InlineData("Biologist", "/api/patients", 403)]
    [InlineData("Secretary", "/api/laboratory-requests", 400)]
    [InlineData("Technician", "/api/laboratory-requests", 403)]
    [InlineData("Biologist", "/api/laboratory-requests", 403)]
    public async Task RolePermissions_AreEnforcedBeforeBinding(string role, string route, int expected)
    {
        using var host = new IdentityHttpTestHost(); host.Roles = new List<string> { role };
        using var client = host.Client(); await Csrf(client); await Login(client); await Csrf(client);
        using var response = await client.PostAsync(route, new StringContent("invalid json", Encoding.UTF8, "application/json"));
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedBusinessWrite_StillRequiresCsrf()
    {
        using var host = new IdentityHttpTestHost(); using var client = host.Client();
        await Csrf(client); await Login(client); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/patients", new { })).StatusCode);
    }

    [Fact]
    public void EveryBusinessAction_DeclaresAnExistingPermissionPolicy()
    {
        var controllers = new[] { typeof(Clinisys.API.Controllers.PatientsController), typeof(Clinisys.API.Controllers.LaboratoryRequestsController),
            typeof(Clinisys.API.Controllers.AnalysisResultsController), typeof(Clinisys.API.Controllers.MedicalAnalysesController),
            typeof(Clinisys.API.Controllers.AnalysisPanelsController), typeof(Clinisys.API.Controllers.ReferenceRangesController) };
        foreach (var controller in controllers)
        foreach (var action in controller.GetMethods().Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), true).Length != 0))
        {
            var policy = Assert.Single(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.Contains(policy.Policy, AuthenticationPermissions.All);
            Assert.Empty(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
        }
    }
}
