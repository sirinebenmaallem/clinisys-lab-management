using System.Net;
using System.Net.Http.Json;
using System.Text;
using Clinisys.Application.Features.Authentication;
using Clinisys.Tests.Persistence;

namespace Clinisys.Tests.Authentication;

// Prepared only. Every entry point is opt-in through the EXISTING SqlServerFact.
[Collection(AuthenticationSqlCollection.Name)]
public sealed class AuthenticationSqlTests
{
    [SqlServerFact]
    public async Task StandardIdentity_CreatesSyntheticUsersAssignsRolesAndIssuesRealCookies()
    {
        foreach (var role in AuthenticationRoles.All)
        {
            await using var session = await AuthenticationSqlSession.OpenAsync(role);
            await session.WithUserAsync(async (users, _, user) =>
            {
                Assert.True(await users.CheckPasswordAsync(user, AuthenticationSqlSession.Password));
                Assert.False(await users.CheckPasswordAsync(user, "Synthetic incorrect SQL password"));
                Assert.Equal(new[] { role }, await users.GetRolesAsync(user));
                Assert.True(await users.HasPasswordAsync(user));
            });
            var client = session.Client();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
            await AuthenticationSqlSession.CsrfAsync(client);
            using var login = await session.LoginAsync(client);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Clinisys.Auth="));
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
            using var response = await client.GetAsync("/api/auth/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var dto = (await response.Content.ReadFromJsonAsync<AuthenticationSessionDto>())!;
            Assert.Equal(session.User.Id, dto.UserId);
            Assert.Equal(new[] { role }, dto.Roles);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(AuthenticationSqlSession.Password, text);
            Assert.DoesNotContain("passwordHash", text);
            Assert.DoesNotContain("securityStamp", text);
        }
    }

    [SqlServerFact]
    public async Task FiveWrongHttpPasswords_LockForFifteenMinutesAndRejectCorrectPassword()
    {
        await using var session = await AuthenticationSqlSession.OpenAsync(AuthenticationRoles.Technician);
        var client = session.Client(); await AuthenticationSqlSession.CsrfAsync(client);
        var before = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await session.LoginAsync(client, "Synthetic incorrect SQL password");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        await session.WithUserAsync(async (users, _, user) =>
        {
            Assert.True(await users.IsLockedOutAsync(user));
            Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
            Assert.InRange((await users.GetLockoutEndDateAsync(user))!.Value, before.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
        });
        using var correct = await session.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.False(correct.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(v => v.StartsWith("__Host-Clinisys.Auth=")));
    }

    [SqlServerFact]
    public async Task DisabledAccount_IsRejectedAndExistingCookieIsRevoked()
    {
        await using var session = await AuthenticationSqlSession.OpenAsync(AuthenticationRoles.Secretary);
        var client = session.Client(); await AuthenticationSqlSession.CsrfAsync(client);
        using var login = await session.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await session.WithUserAsync(async (users, _, user) =>
        { user.IsActive = false; AuthenticationSqlSession.Require(await users.UpdateSecurityStampAsync(user)); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        var other = session.Client(); await AuthenticationSqlSession.CsrfAsync(other);
        using var rejected = await session.LoginAsync(other);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [SqlServerFact]
    public async Task RealCookies_EnforceBusinessRolesAndCsrfBeforeAnyMedicalWrite()
    {
        foreach (var role in AuthenticationRoles.All)
        {
            await using var session = await AuthenticationSqlSession.OpenAsync(role);
            var client = session.Client(); await AuthenticationSqlSession.CsrfAsync(client);
            using var login = await session.LoginAsync(client);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            // The anonymous token is invalid under the new authenticated identity.
            using var noCsrf = await client.PostAsJsonAsync("/api/auth/logout", new { });
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
            await AuthenticationSqlSession.CsrfAsync(client);
            var routes = new[]
            {
                ("/api/medical-analyses", role == AuthenticationRoles.Biologist),
                ("/api/patients", role == AuthenticationRoles.Secretary),
                ("/api/laboratory-requests", role == AuthenticationRoles.Secretary),
                ($"/api/laboratory-requests/{Guid.Empty}/items/{Guid.Empty}/result", role != AuthenticationRoles.Secretary)
            };
            foreach (var (route, permitted) in routes)
            {
                // Invalid input exercises authorization/binding, never a medical write.
                using var response = await client.PostAsync(route, new StringContent("invalid JSON", Encoding.UTF8, "application/json"));
                Assert.Equal(permitted ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Null(response.Headers.Location);
            }
            if (role != AuthenticationRoles.Secretary)
            {
                using var detail = await client.GetAsync($"/api/patients/{Guid.Empty}");
                Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
                Assert.True(detail.Headers.CacheControl?.NoStore);
            }
        }
    }

    [SqlServerFact]
    public async Task Logout_RevokesTwoRealCookiesThroughPersistedSecurityStamp()
    {
        await using var session = await AuthenticationSqlSession.OpenAsync(AuthenticationRoles.Biologist);
        var first = session.Client(); var second = session.Client();
        foreach (var client in new[] { first, second })
        {
            await AuthenticationSqlSession.CsrfAsync(client);
            using var login = await session.LoginAsync(client);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        }
        await AuthenticationSqlSession.CsrfAsync(first);
        using var logout = await first.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        foreach (var client in new[] { first, second })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [SqlServerFact]
    public async Task AdministrativeRoleChange_RevokesOldCookieAndNewLoginGetsNewRole()
    {
        await using var session = await AuthenticationSqlSession.OpenAsync(AuthenticationRoles.Secretary);
        var client = session.Client(); await AuthenticationSqlSession.CsrfAsync(client);
        using var login = await session.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await session.WithUserAsync(async (users, roles, user) =>
        {
            if (!await roles.RoleExistsAsync(AuthenticationRoles.Technician))
                AuthenticationSqlSession.Require(await roles.CreateAsync(new() { Name = AuthenticationRoles.Technician }));
            AuthenticationSqlSession.Require(await users.RemoveFromRoleAsync(user, AuthenticationRoles.Secretary));
            AuthenticationSqlSession.Require(await users.AddToRoleAsync(user, AuthenticationRoles.Technician));
            AuthenticationSqlSession.Require(await users.UpdateSecurityStampAsync(user));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        var fresh = session.Client(); await AuthenticationSqlSession.CsrfAsync(fresh);
        using var next = await session.LoginAsync(fresh);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var dto = (await fresh.GetFromJsonAsync<AuthenticationSessionDto>("/api/auth/session"))!;
        Assert.Equal(new[] { AuthenticationRoles.Technician }, dto.Roles);
    }
}
