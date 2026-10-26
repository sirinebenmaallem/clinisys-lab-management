using System.Net;
using System.Net.Http.Json;
using Clinisys.Application.Features.Dashboard;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.Dashboard;

public sealed class DashboardApiTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("Secretary", 403)]
    [InlineData("Technician", 403)]
    [InlineData("Unknown", 403)]
    [InlineData("Biologist", 200)]
    public async Task OnlyBiologist_CanReadFinancialData(string? role, int expected)
    {
        using var host = new Host(role);
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/dashboard?from=2026-01-01&to=2026-01-31");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(expected == 200 ? 1 : 0, host.Calls);
        if (expected == 200)
        {
            var dto = await response.Content.ReadFromJsonAsync<DashboardDto>();
            Assert.Equal(0, dto!.Revenue);
            Assert.Empty(dto.Days); Assert.Empty(dto.TopAnalyses);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2026-01-01")]
    [InlineData("?from=SYN-PRIVATE&to=2026-01-01")]
    [InlineData("?from=2026-03-01&to=2026-01-01")]
    [InlineData("?from=2024-01-01&to=2026-01-01")]
    public async Task InvalidQuery_Is400WithoutEchoingInput(string query)
    {
        using var host = new Host("Biologist");
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/dashboard" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("SYN-PRIVATE", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Calls);
    }

    [Fact]
    public async Task TechnicalFailure_IsSanitized()
    {
        using var host = new Host("Biologist") { Failure = new InvalidOperationException("SYN-PRIVATE connection") };
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/dashboard?from=2026-01-01&to=2026-01-01");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("SYN-PRIVATE", await response.Content.ReadAsStringAsync());
    }

    private sealed class Host(string? role) : WebApplicationFactory<Program>
    {
        public int Calls;
        public Exception? Failure;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddTestAuthentication(role is null ? [] : [role]);
                services.RemoveAll<IDashboardStore>();
                services.AddScoped<IDashboardStore>(_ => new Store(this));
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden."));
            });
        }
        private sealed class Store(Host host) : IDashboardStore
        {
            public Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct)
            { host.Calls++; if (host.Failure is not null) throw host.Failure; return Task.FromResult(new DashboardDto(from, to, 0, 0, [], [])); }
        }
    }
}
