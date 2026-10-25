using System.Security.Claims;
using Clinisys.API.AnalysisResults;
using Clinisys.API.Errors;
using Clinisys.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clinisys.Tests.AnalysisResults;

public sealed class AuthenticatedAnalysisResultActorTests
{
    [Theory]
    [InlineData("Technician", true)]
    [InlineData("Biologist", true)]
    [InlineData("Secretary", false)]
    [InlineData("Unknown", false)]
    public void Actor_ComesOnlyFromAuthenticatedGuidAndAllowedRole(string role, bool allowed)
    {
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)], "Synthetic"));
        context.Request.Headers["X-Actor"] = "client-controlled";
        var provider = new AuthenticatedAnalysisResultActorProvider(new HttpContextAccessor { HttpContext = context });
        if (allowed) Assert.Equal(id.ToString(), provider.GetRequiredActor());
        else Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Actor_RejectsMissingOrInvalidGuid(string? id)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim> { new(ClaimTypes.Role, "Technician") };
        if (id != null) claims.Add(new(ClaimTypes.NameIdentifier, id));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Synthetic"));
        Assert.Throws<InvalidOperationException>(() => new AuthenticatedAnalysisResultActorProvider(
            new HttpContextAccessor { HttpContext = context }).GetRequiredActor());
    }

    [Fact]
    public void Actor_RejectsAnonymousOrAbsentHttpContext()
    {
        Assert.Throws<InvalidOperationException>(() => new AuthenticatedAnalysisResultActorProvider(new HttpContextAccessor()).GetRequiredActor());
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Technician")]));
        Assert.Throws<InvalidOperationException>(() => new AuthenticatedAnalysisResultActorProvider(new HttpContextAccessor { HttpContext = context }).GetRequiredActor());
    }

    [Fact]
    public async Task ErrorHandler_DoesNotInterceptOtherModulesExceptions()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var handler = new AnalysisResultExceptionHandler(NullLogger<AnalysisResultExceptionHandler>.Instance);
        Assert.False(await handler.TryHandleAsync(context, CatalogException.NotFound("Synthetic"), default));
        Assert.False(await handler.TryHandleAsync(context, new InvalidOperationException("Synthetic"), default));
        Assert.Equal(0, context.Response.Body.Length);
    }
}
