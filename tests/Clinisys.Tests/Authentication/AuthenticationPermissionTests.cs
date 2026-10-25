using Clinisys.Application.Features.Authentication;

namespace Clinisys.Tests.Authentication;

public sealed class AuthenticationPermissionTests
{
    [Theory]
    [InlineData(AuthenticationRoles.Secretary, "Catalog.Read,Patients.ReadDetail,Patients.Search,Patients.Write,Requests.Create,Requests.Read")]
    [InlineData(AuthenticationRoles.Technician, "Catalog.Read,Patients.Search,Requests.Read,Results.Read,Results.Write")]
    [InlineData(AuthenticationRoles.Biologist, "Catalog.Read,Catalog.Write,Patients.Search,Requests.Read,Results.Read,Results.Write")]
    public void EachRole_HasExactlyItsApprovedPermissions(string role, string expected)
    {
        Assert.True(AuthenticationRoles.IsKnown(role));
        Assert.Equal(expected.Split(','), AuthenticationPermissions.ForRoles([role]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Administrator")]
    [InlineData("biologist")]
    [InlineData(" Biologist ")]
    [InlineData("*")]
    public void UnknownRoles_GrantNothingAndCannotBeAssigned(string? role)
    {
        Assert.False(AuthenticationRoles.IsKnown(role));
        Assert.Empty(AuthenticationPermissions.ForRoles([role!]));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateRoles([role!]));
    }

    [Fact]
    public void Roles_MustBeExplicitAndNonempty_NoImplicitSecretaryOrBiologist()
    {
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateRoles(null));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateRoles([]));
        Assert.Empty(AuthenticationPermissions.ForRoles(null));
        Assert.Empty(AuthenticationPermissions.ForRoles([]));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateRoles([AuthenticationRoles.Technician, "Unknown"]));
        Assert.Equal(AuthenticationPermissions.ForRoles([AuthenticationRoles.Technician]),
            AuthenticationPermissions.ForRoles([AuthenticationRoles.Technician, "Unknown"]));
    }

    [Fact]
    public void MultipleRoles_GrantOnlyUnion_DuplicatesAreHarmless()
    {
        var assigned = AuthenticationRules.ValidateRoles([AuthenticationRoles.Secretary, AuthenticationRoles.Technician, AuthenticationRoles.Secretary]);
        Assert.Equal(new[] { AuthenticationRoles.Secretary, AuthenticationRoles.Technician }, assigned);
        var permissions = AuthenticationPermissions.ForRoles(assigned);
        var expected = AuthenticationPermissions.ForRoles([AuthenticationRoles.Secretary])
            .Union(AuthenticationPermissions.ForRoles([AuthenticationRoles.Technician])).Order(StringComparer.Ordinal);
        Assert.Equal(expected, permissions);
        Assert.DoesNotContain(AuthenticationPermissions.CatalogWrite, permissions);
        Assert.Equal(permissions, AuthenticationPermissions.ForRoles(assigned.Concat(assigned)));
    }

    [Fact]
    public void Biologist_IsNotAnImplicitAdministratorOrSecretary()
    {
        var permissions = AuthenticationPermissions.ForRoles([AuthenticationRoles.Biologist]);
        Assert.DoesNotContain(AuthenticationPermissions.PatientsReadDetail, permissions);
        Assert.DoesNotContain(AuthenticationPermissions.PatientsWrite, permissions);
        Assert.DoesNotContain(AuthenticationPermissions.RequestsCreate, permissions);
        Assert.Equal(9, AuthenticationPermissions.All.Count);
        Assert.DoesNotContain(AuthenticationPermissions.All, x => x.Contains("Validat", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Users", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReturnedPolicyCollections_CannotBeMutated()
    {
        Assert.Throws<NotSupportedException>(() => ((IList<string>)AuthenticationRoles.All)[0] = "Administrator");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)AuthenticationPermissions.All)[0] = "Users.Manage");
        var permissions = AuthenticationPermissions.ForRoles([AuthenticationRoles.Technician]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)permissions).Add("Catalog.Write"));
        Assert.DoesNotContain("Catalog.Write", AuthenticationPermissions.ForRoles([AuthenticationRoles.Technician]));
    }
}
