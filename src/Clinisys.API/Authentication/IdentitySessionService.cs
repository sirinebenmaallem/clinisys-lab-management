using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Clinisys.API.Authentication;

public sealed class IdentitySessionService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IHttpContextAccessor accessor, TimeProvider clock, IServiceScopeFactory scopes) : IAuthenticationSessionService
{
    private HttpContext Context => accessor.HttpContext ?? throw AuthenticationException.Unauthenticated();

    public async Task<AuthenticationSessionDto> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var validated = AuthenticationRules.ValidateLogin(input);
        ct.ThrowIfCancellationRequested();
        if (!Context.Request.IsHttps) throw AuthenticationException.InvalidCredentials();
        var user = await users.FindByNameAsync(validated.UserName!);
        if (user == null || !user.IsActive) throw AuthenticationException.InvalidCredentials();
        var roles = await users.GetRolesAsync(user);
        if (roles.Count == 0 || roles.Any(r => !AuthenticationRoles.IsKnown(r)) || user.TwoFactorEnabled)
            throw AuthenticationException.InvalidCredentials();
        var result = await signIn.CheckPasswordSignInAsync(user, validated.Password!, lockoutOnFailure: true);
        if (!result.Succeeded) throw AuthenticationException.InvalidCredentials();
        // A fresh standard manager avoids returning an old EF-tracked account.
        var expectedStamp = user.SecurityStamp;
        using var scope = scopes.CreateScope();
        var freshUsers = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        user = await freshUsers.FindByIdAsync(user.Id.ToString()) ?? throw AuthenticationException.InvalidCredentials();
        var currentRoles = await freshUsers.GetRolesAsync(user);
        if (!user.IsActive || currentRoles.Count == 0 || currentRoles.Any(r => !AuthenticationRoles.IsKnown(r))
            || !string.Equals(expectedStamp, user.SecurityStamp, StringComparison.Ordinal)
            || !roles.Order(StringComparer.Ordinal).SequenceEqual(currentRoles.Order(StringComparer.Ordinal)))
            throw AuthenticationException.InvalidCredentials();
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(30);
        await signIn.SignInAsync(user, new AuthenticationProperties
        { IsPersistent = false, AllowRefresh = false, IssuedUtc = now, ExpiresUtc = expires });
        return new(user.Id, user.UserName!, currentRoles.Order(StringComparer.Ordinal).ToArray(), expires);
    }

    public async Task<AuthenticationSessionDto> GetSessionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ticket = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!ticket.Succeeded || ticket.Properties?.ExpiresUtc is not { } expires)
            throw AuthenticationException.Unauthenticated();
        var user = await users.GetUserAsync(Context.User) ?? throw AuthenticationException.Unauthenticated();
        return new(user.Id, user.UserName!, (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToArray(), expires);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(Context.User) ?? throw AuthenticationException.Unauthenticated();
        try
        {
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded)
                throw new InvalidOperationException("Session revocation could not be completed.");
        }
        finally { await signIn.SignOutAsync(); }
    }
}
