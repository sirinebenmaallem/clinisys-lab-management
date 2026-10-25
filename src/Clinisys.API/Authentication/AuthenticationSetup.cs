using System.Threading.RateLimiting;
using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Clinisys.API.Authentication;

public static class AuthenticationSetup
{
    public static void AddHttpIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var protection = services.AddDataProtection().SetApplicationName("Clinisys");
        var directory = configuration["DataProtection:KeyDirectory"];
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("A protected persistent Data Protection key directory is required in Production.");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Explicit key storage requires Windows DPAPI in this deployment.");
            protection.PersistKeysToFileSystem(new DirectoryInfo(directory)).ProtectKeysWithDpapi();
        }
        new IdentityBuilder(typeof(ApplicationUser), typeof(ApplicationRole), services)
            .AddSignInManager().AddDefaultTokenProviders();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-Clinisys.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = ctx => AuthProblems.WriteAsync(ctx.HttpContext, 401);
            options.Events.OnRedirectToAccessDenied = ctx => AuthProblems.WriteAsync(ctx.HttpContext, 403);
            options.Events.OnValidatePrincipal = async ctx =>
            {
                var signIn = ctx.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                var user = await signIn.ValidateSecurityStampAsync(ctx.Principal);
                var manager = signIn.UserManager;
                var roles = user == null ? [] : await manager.GetRolesAsync(user);
                var ticketRoles = ctx.Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray() ?? [];
                if (user is null || !user.IsActive || roles.Count == 0 || roles.Any(r => !AuthenticationRoles.IsKnown(r))
                    || !ticketRoles.SequenceEqual(roles.Order(StringComparer.Ordinal)))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
                // Check every request; do not renew or extend the absolute ticket.
            };
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in AuthenticationPermissions.All)
            {
                var roles = AuthenticationRoles.All.Where(role => AuthenticationPermissions.ForRoles([role]).Contains(permission)).ToArray();
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireRole(roles));
            }
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-Clinisys.Csrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        services.AddScoped<CsrfFilter>();
        services.AddScoped<IAuthenticationSessionService, IdentitySessionService>();
        services.AddScoped<IAntiforgeryTokenProvider, AntiforgeryTokenProvider>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = (ctx, _) => new ValueTask(AuthProblems.WriteAsync(ctx.HttpContext, 429));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
    }
}
