using System.Net;
using System.Net.Http.Json;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.AnalysisResults;

public sealed class PatientAnalysisHistoryApiTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("Secretary", 403)]
    [InlineData("Unknown", 403)]
    [InlineData("Technician", 200)]
    [InlineData("Biologist", 200)]
    public async Task ResultsReadPolicy_EnforcesRolesAndNoStore(string? role, int expected)
    {
        using var host = new Host(role);
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync(host.Route);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(expected == 200 ? 1 : 0, host.Calls);
        if (expected == 200)
        {
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal("High", body.GetProperty("points")[0].GetProperty("abnormality").GetString());
            Assert.Equal("unit", body.GetProperty("points")[0].GetProperty("unit").GetString());
        }
    }

    [Fact]
    public async Task InvalidMissingEmptyAndTechnicalFailures_AreConsistentAndSanitized()
    {
        using var host = new Host("Technician");
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
        foreach (var path in new[] { $"/api/patients/{Guid.Empty}/analyses/{host.Analysis}/history", $"/api/patients/{host.Patient}/analyses/{Guid.Empty}/history" })
        {
            using var invalid = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(0, host.Calls);
        using var malformed = await client.GetAsync($"/api/patients/invalid/analyses/{host.Analysis}/history");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        host.Data = null;
        using var missing = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        host.Data = new(host.Patient, host.Analysis, []);
        using var empty = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<PatientAnalysisHistoryDto>())!.Points);
        host.Failure = new InvalidOperationException("SYNTHETIC-PRIVATE");
        using var failed = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", await failed.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
        Assert.True(failed.Headers.CacheControl?.NoStore);
    }

    private sealed class Host(string? role) : WebApplicationFactory<Program>
    {
        public Guid Patient = Guid.NewGuid(), Analysis = Guid.NewGuid();
        public PatientAnalysisHistoryDto? Data = new(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), "SYN-HISTORY", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 12, "unit", Abnormality.High)]);
        public Exception? Failure;
        public int Calls;
        public string Route => $"/api/patients/{Patient}/analyses/{Analysis}/history";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddTestAuthentication(role is null ? [] : [role]);
                services.RemoveAll<IPatientAnalysisHistoryStore>();
                services.AddScoped<IPatientAnalysisHistoryStore>(_ => new Store(this));
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden."));
            });
        }
        private sealed class Store(Host host) : IPatientAnalysisHistoryStore
        {
            public Task<PatientAnalysisHistoryDto?> GetAsync(Guid patientId, Guid analysisId, CancellationToken ct)
            { host.Calls++; if (host.Failure is not null) throw host.Failure; return Task.FromResult(host.Data); }
        }
    }
}
