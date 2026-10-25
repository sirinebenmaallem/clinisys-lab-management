using System.Net;
using Clinisys.API.AnalysisResults;
using Clinisys.API.Errors;
using Clinisys.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clinisys.Tests.AnalysisResults;

public sealed class AnalysisResultLocalAccessTests
{
    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Testing", "true", true)]
    [InlineData("Production", "true", false)]
    [InlineData("Staging", "true", false)]
    [InlineData("Development", null, false)]
    [InlineData("Testing", "false", false)]
    public void ActorProvider_RequiresTheSameExplicitVerifiedLocalContext(string environment, string? flag, bool allowed)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [AnalysisResultLocalAccess.ConfigurationKey] = flag }).Build();
        var access = new AnalysisResultLocalAccess(new EnvironmentStub { EnvironmentName = environment }, config);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Actor"] = "SYN-PRIVATE";
        var provider = new LocalAnalysisResultActorProvider(access, new HttpContextAccessor { HttpContext = context });
        if (allowed) Assert.Equal("LOCAL-UNAUTHENTICATED", provider.GetRequiredActor());
        else Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);

        config[AnalysisResultLocalAccess.ConfigurationKey] = "false";
        Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);
    }

    [Fact]
    public void ActorProvider_FailsWithoutHttpContextOrWithProxyHeadersOrUnknownAddresses()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [AnalysisResultLocalAccess.ConfigurationKey] = "true" }).Build();
        var access = new AnalysisResultLocalAccess(new EnvironmentStub(), config);
        var accessor = new HttpContextAccessor();
        accessor.HttpContext = null;
        var provider = new LocalAnalysisResultActorProvider(access, accessor);
        Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);
        var context = new DefaultHttpContext();
        accessor.HttpContext = context;
        Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Request.Headers["Forwarded"] = "";
        Assert.Throws<InvalidOperationException>(provider.GetRequiredActor);
    }

    [Fact]
    public async Task ErrorHandler_DoesNotInterceptOtherModulesExceptions()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new AnalysisResultExceptionHandler(NullLogger<AnalysisResultExceptionHandler>.Instance);
        Assert.False(await handler.TryHandleAsync(context, CatalogException.NotFound("Synthetic"), default));
        Assert.False(await handler.TryHandleAsync(context, new InvalidOperationException("Synthetic"), default));
        Assert.Equal(0, context.Response.Body.Length);
    }

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Synthetic test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
