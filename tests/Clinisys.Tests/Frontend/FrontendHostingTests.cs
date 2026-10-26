using System.Net;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.Frontend;

public sealed class FrontendHostingTests
{
    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/css/style.css", "text/css")]
    [InlineData("/assets/clinisys-logo.png", "image/png")]
    [InlineData("/js/vendor/jquery-3.7.1.min.js", "text/javascript")]
    [InlineData("/js/api.js", "text/javascript")]
    [InlineData("/js/app.js", "text/javascript")]
    [InlineData("/js/catalog.js", "text/javascript")]
    [InlineData("/js/patients.js", "text/javascript")]
    [InlineData("/js/requests.js", "text/javascript")]
    [InlineData("/js/result-entry.js", "text/javascript")]
    [InlineData("/js/history.js", "text/javascript")]
    [InlineData("/js/dashboard.js", "text/javascript")]
    public async Task LoginAssets_ArePublicAndServedFromApiOutput(string path, string contentType)
    {
        using var host = new Host();
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/laboratory-requests")]
    [InlineData("/api/medical-analyses")]
    public async Task PublicLoginAssets_DoNotExposeBusinessApis(string path)
    {
        using var host = new Host();
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        {
            services.AddLogging(logging => logging.ClearProviders());
            services.AddTestAuthentication();
            services.RemoveAll<ClinisysDbContext>();
            services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden in frontend hosting tests."));
        });
    }
}
