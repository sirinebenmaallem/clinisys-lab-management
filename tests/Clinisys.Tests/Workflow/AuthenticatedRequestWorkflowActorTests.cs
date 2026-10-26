using System.Security.Claims;
using Clinisys.API.Authentication;
using Microsoft.AspNetCore.Http;

namespace Clinisys.Tests.Workflow;

public sealed class AuthenticatedRequestWorkflowActorTests
{
    [Theory]
    [InlineData("Secretary")]
    [InlineData("Technician")]
    [InlineData("Biologist")]
    public void KnownRole_UsesIdentityIdRatherThanUserName(string role)
    {
        var id = Guid.NewGuid();
        var provider = Provider(role, id.ToString(), "Synthetic");
        Assert.Equal(id.ToString(), provider.GetRequiredActor());
    }

    [Theory]
    [InlineData("Secretary", null, "Synthetic")]
    [InlineData("Secretary", "Synthetic", "Synthetic")]
    [InlineData("Secretary", "00000000-0000-0000-0000-000000000000", "Synthetic")]
    [InlineData("Administrator", "a0000000-0000-0000-0000-000000000007", "Synthetic")]
    [InlineData("Secretary", "a0000000-0000-0000-0000-000000000007", null)]
    public void InvalidIdentityOrRole_IsRejected(string role, string? id, string? authenticationType)
    {
        Assert.Throws<InvalidOperationException>(() => Provider(role, id, authenticationType).GetRequiredActor());
    }

    [Fact]
    public void MissingContext_IsRejected() => Assert.Throws<InvalidOperationException>(() =>
        new AuthenticatedRequestWorkflowActorProvider(new HttpContextAccessor()).GetRequiredActor());

    private static AuthenticatedRequestWorkflowActorProvider Provider(string role, string? id, string? authenticationType)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "Synthetic"), new(ClaimTypes.Role, role) };
        if (id is not null) claims.Add(new(ClaimTypes.NameIdentifier, id));
        return new(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, authenticationType)) }
        });
    }
}
