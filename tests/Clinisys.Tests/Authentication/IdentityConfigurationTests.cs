using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clinisys.Tests.Authentication;

// Validators/options/hashing only. No context or user store is used.
public sealed class IdentityConfigurationTests
{
    [Fact]
    public void NewIdentities_HaveDistinctServerGuidsStampsAndInactiveDefaults()
    {
        var first = new ApplicationUser();
        var second = new ApplicationUser();
        var role = new ApplicationRole();
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.False(first.IsActive);
        Assert.True(first.LockoutEnabled);
        Assert.False(string.IsNullOrWhiteSpace(first.SecurityStamp));
        Assert.NotEqual(first.SecurityStamp, second.SecurityStamp);
        Assert.False(string.IsNullOrWhiteSpace(role.ConcurrencyStamp));
        Assert.Null(first.PasswordHash);
    }

    [Theory]
    [InlineData("  User.Name_1-2  ", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData(".abc", false)]
    [InlineData("abc@domain", false)]
    [InlineData("abcé", false)]
    [InlineData(null, false)]
    public async Task UserValidator_UsesApplicationRules(string? name, bool valid)
    {
        var user = new ApplicationUser { UserName = name };
        var result = await new ApplicationUserValidator().ValidateAsync(null!, user);
        Assert.Equal(valid, result.Succeeded);
        if (valid) Assert.Equal(AuthenticationRules.ValidateUserName(name), user.UserName);
        else Assert.All(result.Errors, e => Assert.Equal("Account input is missing or invalid.", e.Description));
    }

    [Fact]
    public async Task UserValidator_RejectsEmptyGuidAndOverlongUserNameWithoutMutation()
    {
        var user = new ApplicationUser { Id = Guid.Empty, UserName = " valid " };
        Assert.False((await new ApplicationUserValidator().ValidateAsync(null!, user)).Succeeded);
        Assert.Equal(" valid ", user.UserName);
        user.Id = Guid.NewGuid();
        user.UserName = new string('a', 65);
        Assert.False((await new ApplicationUserValidator().ValidateAsync(null!, user)).Succeeded);
        user.UserName = new string('a', 64);
        Assert.True((await new ApplicationUserValidator().ValidateAsync(null!, user)).Succeeded);
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task PasswordValidator_EnforcesBothBoundsWithoutCompositionRequirement(int length, bool valid)
    {
        var password = new string(' ', length);
        var result = await new ApplicationPasswordValidator().ValidateAsync(null!, new(), password);
        Assert.Equal(valid, result.Succeeded);
        Assert.Equal(new string(' ', length), password);
    }

    [Fact]
    public async Task PasswordValidator_RejectsNullWithoutEchoingCredentials()
    {
        var result = await new ApplicationPasswordValidator().ValidateAsync(null!, new(), null);
        Assert.False(result.Succeeded);
        Assert.Equal("InvalidPassword", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(AuthenticationRoles.Secretary, true)]
    [InlineData(AuthenticationRoles.Technician, true)]
    [InlineData(AuthenticationRoles.Biologist, true)]
    [InlineData("Administrator", false)]
    [InlineData("biologist", false)]
    [InlineData(null, false)]
    public async Task RoleValidator_AcceptsOnlyApprovedExactNames(string? name, bool valid)
    {
        var result = await new ApplicationRoleValidator().ValidateAsync(null!, new() { Name = name });
        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void Registration_UsesOfficialHasherVersion2SchemaAndApprovedLockoutWithoutHttpAuthentication()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinisysIdentity();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var identity = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(IdentitySchemaVersions.Version2, identity.Stores.SchemaVersion);
        Assert.Equal(128, identity.Stores.MaxLengthForKeys);
        Assert.Equal(15, identity.Password.RequiredLength);
        Assert.Equal(0, identity.Password.RequiredUniqueChars);
        Assert.False(identity.Password.RequireDigit || identity.Password.RequireLowercase
            || identity.Password.RequireUppercase || identity.Password.RequireNonAlphanumeric);
        Assert.True(identity.Lockout.AllowedForNewUsers);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);
        var options = provider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
        Assert.Equal(PasswordHasherCompatibilityMode.IdentityV3, options.CompatibilityMode);
        Assert.Equal(new PasswordHasherOptions().IterationCount, options.IterationCount);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        Assert.IsType<PasswordHasher<ApplicationUser>>(hasher);
        var user = new ApplicationUser();
        const string password = "  Synthetic password  ";
        var hash = hasher.HashPassword(user, password);
        Assert.NotEqual(password, hash);
        Assert.NotEqual(hash, hasher.HashPassword(user, password));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, password.Trim()));
        var normalizer = provider.GetRequiredService<ILookupNormalizer>();
        Assert.Equal("USER.NAME", normalizer.NormalizeName(" User.Name "));
        Assert.Equal("INVALID@NAME", normalizer.NormalizeName("invalid@name"));
        Assert.Null(normalizer.NormalizeName(null));
        // Standard Identity validators remain alongside our additional pure adapters.
        Assert.Contains(services, d => d.ServiceType == typeof(IUserValidator<ApplicationUser>)
            && d.ImplementationType == typeof(UserValidator<ApplicationUser>));
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "Microsoft.AspNetCore.Authentication.IAuthenticationService");
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAuthenticationSessionService));
    }
}
