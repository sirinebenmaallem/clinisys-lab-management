using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Clinisys.Infrastructure.Authentication;

// Additional validators retain Identity's standard duplicate-name checks.
public sealed class ApplicationUserValidator : IUserValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        try
        {
            var name = AuthenticationRules.ValidateUserName(user.UserName);
            if (user.Id == Guid.Empty) return Task.FromResult(Invalid("InvalidUserId"));
            user.UserName = name;
            return Task.FromResult(IdentityResult.Success);
        }
        catch (AuthenticationException) { return Task.FromResult(Invalid("InvalidUserName")); }
    }

    internal static IdentityResult Invalid(string code) => IdentityResult.Failed(new IdentityError
        { Code = code, Description = "Account input is missing or invalid." });
}

public sealed class ApplicationPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        try { AuthenticationRules.ValidatePassword(password); return Task.FromResult(IdentityResult.Success); }
        catch (AuthenticationException) { return Task.FromResult(ApplicationUserValidator.Invalid("InvalidPassword")); }
    }
}

public sealed class ApplicationRoleValidator : IRoleValidator<ApplicationRole>
{
    public Task<IdentityResult> ValidateAsync(RoleManager<ApplicationRole> manager, ApplicationRole role) =>
        Task.FromResult(role.Id != Guid.Empty && AuthenticationRoles.IsKnown(role.Name)
            ? IdentityResult.Success : ApplicationUserValidator.Invalid("InvalidRole"));
}

public sealed class ApplicationLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name)
    {
        // A normalizer is not a validator. Never turn a non-null invalid input
        // into null: Identity's standard duplicate checker expects a lookup key.
        // ApplicationUserValidator still rejects every disallowed username.
        return name?.Trim().ToUpperInvariant();
    }

    // Email is not a login identifier; keep standard Identity normalization.
    public string? NormalizeEmail(string? email) => new UpperInvariantLookupNormalizer().NormalizeEmail(email);
}
