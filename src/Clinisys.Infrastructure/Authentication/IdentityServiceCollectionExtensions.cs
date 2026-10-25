using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinisys.Infrastructure.Authentication;

public static class IdentityServiceCollectionExtensions
{
    // Identity Core/stores only: no cookie scheme, SignInManager, HTTP endpoint or seed.
    public static IServiceCollection AddClinisysIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
            options.Stores.MaxLengthForKeys = 128;
            // The custom validator owns the complete ASCII rule and trimming.
            options.User.AllowedUserNameCharacters = null!;
            options.User.RequireUniqueEmail = false;
            options.Password.RequiredLength = AuthenticationRules.MinimumPasswordLength;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 0;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ClinisysDbContext>()
            .AddUserValidator<ApplicationUserValidator>()
            .AddPasswordValidator<ApplicationPasswordValidator>()
            .AddRoleValidator<ApplicationRoleValidator>();
        services.Replace(ServiceDescriptor.Singleton<ILookupNormalizer, ApplicationLookupNormalizer>());
        services.Configure<PasswordHasherOptions>(options => options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3);
        return services;
    }
}
