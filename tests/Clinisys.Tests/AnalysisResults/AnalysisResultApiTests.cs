using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clinisys.API.AnalysisResults;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.AnalysisResults;

// HTTP in-memory host only. The persistence port is replaced and any accidental
// DbContext resolution throws before a SQL connection can be constructed/opened.
public sealed class AnalysisResultApiTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly string DetailRoute = $"/api/laboratory-requests/{RequestId}/items/{ItemId}/result";
    private static readonly string ListRoute = $"/api/laboratory-requests/{RequestId}/results";
    private static readonly string Version = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);

    [Theory]
    [InlineData(null, "Development", "127.0.0.1", "127.0.0.1", null)]
    [InlineData("false", "Development", "127.0.0.1", "127.0.0.1", null)]
    [InlineData("invalid", "Development", "127.0.0.1", "127.0.0.1", null)]
    [InlineData("true", "Production", "127.0.0.1", "127.0.0.1", null)]
    [InlineData("true", "Staging", "127.0.0.1", "127.0.0.1", null)]
    [InlineData("true", "Development", "192.0.2.10", "127.0.0.1", null)]
    [InlineData("true", "Development", "127.0.0.1", "192.0.2.11", null)]
    [InlineData("true", "Development", null, "127.0.0.1", null)]
    [InlineData("true", "Testing", "127.0.0.1", null, null)]
    [InlineData("true", "Testing", "127.0.0.1", "127.0.0.1", "Forwarded")]
    [InlineData("true", "Development", "127.0.0.1", "127.0.0.1", "X-Forwarded-For")]
    [InlineData("true", "Development", "127.0.0.1", "127.0.0.1", "X-Forwarded-Host")]
    [InlineData("true", "Development", "127.0.0.1", "127.0.0.1", "X-Real-IP")]
    public async Task Gate_FailsClosedForAllEndpointsBeforeBindingOrStoreResolution(string? enabled, string environment,
        string? remote, string? local, string? proxyHeader)
    {
        using var host = new Host(enabled, environment, remote, local);
        using var client = host.Client();
        if (proxyHeader is not null) client.DefaultRequestHeaders.Add(proxyHeader, "SYN-PRIVATE");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Get })
        {
            using var request = new HttpRequestMessage(method, DetailRoute);
            if (method != HttpMethod.Get) request.Content = new StringContent("invalid JSON SYN-PRIVATE", Encoding.UTF8, "application/json");
            await Problem(await client.SendAsync(request), 404);
        }
        await Problem(await client.GetAsync(ListRoute), 404);
        Assert.Equal(0, host.State.Resolutions);
        Assert.Equal(0, host.State.Calls);
    }

    [Theory]
    [InlineData("Development", "127.0.0.1")]
    [InlineData("Testing", "::1")]
    [InlineData("Development", "::ffff:127.0.0.1")]
    public async Task ExplicitLocalActivation_AllowsLifecycleWithOnlyServerActor(string environment, string address)
    {
        using var host = new Host("true", environment, address, address);
        using var client = host.Client();
        client.DefaultRequestHeaders.Add("X-Actor", "SYN-PRIVATE");
        client.DefaultRequestHeaders.Add("EnteredBy", "SYN-PRIVATE");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "SYN-PRIVATE");
        using var created = await client.PostAsJsonAsync(DetailRoute, new { value = 0 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(DetailRoute, created.Headers.Location!.AbsolutePath);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var result = await Body(created);
        Assert.Equal(0, result.GetProperty("value").GetDecimal());
        Assert.Equal("LOCAL-UNAUTHENTICATED", result.GetProperty("enteredBy").GetString());
        Assert.DoesNotContain("SYN-PRIVATE", result.ToString());
        var firstVersion = result.GetProperty("version").GetString();

        using var updated = await client.PutAsJsonAsync(DetailRoute, new { value = 1.5, version = firstVersion });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True(updated.Headers.CacheControl?.NoStore);
        var corrected = await Body(updated);
        Assert.Equal(1.5m, corrected.GetProperty("value").GetDecimal());
        Assert.NotEqual(firstVersion, corrected.GetProperty("version").GetString());
        Assert.Equal("LOCAL-UNAUTHENTICATED", corrected.GetProperty("enteredBy").GetString());
        Assert.Equal(8, Convert.FromBase64String(corrected.GetProperty("version").GetString()!).Length);

        using var detail = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.True(detail.Headers.CacheControl?.NoStore);
        Assert.Equal(1.5m, (await Body(detail)).GetProperty("value").GetDecimal());
        using var list = await client.GetAsync(ListRoute);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        var items = (await Body(list)).GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("result").ValueKind);
        var json = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalIdentityNumber", json);
        Assert.DoesNotContain("phoneNumber", json);
        Assert.DoesNotContain("SYN-PRIVATE", json);
        Assert.Equal(Version, host.State.ReceivedVersion);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"value\":null}", false)]
    [InlineData("{\"value\":\"SYN-PRIVATE\"}", false)]
    [InlineData("{\"value\":0,\"enteredBy\":\"SYN-PRIVATE\"}", false)]
    [InlineData("{\"value\":0,\"enteredAt\":\"SYN-PRIVATE\"}", false)]
    [InlineData("{\"value\":0,\"status\":\"Validated\"}", false)]
    [InlineData("{\"value\":0,\"price\":100}", false)]
    [InlineData("{\"value\":0,\"abnormality\":\"Normal\"}", false)]
    [InlineData("{\"value\":0,\"referenceLowerBoundSnapshot\":1}", false)]
    [InlineData("{\"value\":0}", true)]
    [InlineData("{\"value\":0,\"version\":null}", true)]
    [InlineData("{\"value\":0,\"version\":\"SYN-PRIVATE\"}", true)]
    [InlineData("{\"value\":0,\"version\":\"AQ==\"}", true)]
    [InlineData("{\"value\":0,\"version\":\"AQIDBAUGBwgJ\"}", true)]
    [InlineData("{\"version\":\"AQIDBAUGBwg=\"}", true)]
    [InlineData("{\"value\":0.0000001,\"version\":\"AQIDBAUGBwg=\"}", true)]
    [InlineData("{\"value\":0,\"version\":\"AQIDBAUGBwg=\",\"enteredBy\":\"SYN-PRIVATE\"}", true)]
    public async Task InvalidJsonAndVersions_Return400BeforePersistence(string json, bool update)
    {
        using var host = new Host();
        using var client = host.Client();
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post, DetailRoute)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        var body = await Problem(await client.SendAsync(request), 400);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, host.State.Calls);
    }

    [Theory]
    [InlineData(AnalysisResultError.Validation, 400)]
    [InlineData(AnalysisResultError.NotFound, 404)]
    [InlineData(AnalysisResultError.Conflict, 409)]
    [InlineData(AnalysisResultError.Internal, 500)]
    [InlineData(AnalysisResultError.CommitUnknown, 503)]
    public async Task ApplicationErrors_AreSanitizedAndUnknownCommitIsExplicit(AnalysisResultError error, int status)
    {
        using var host = new Host();
        host.State.Failure = new AnalysisResultException(error, "SYN-PRIVATE SQL password", "SYN-PRIVATE");
        using var client = host.Client();
        var body = await Problem(await client.PostAsJsonAsync(DetailRoute, new { value = 0 }), status);
        Assert.Equal(error == AnalysisResultError.CommitUnknown, body.TryGetProperty("outcomeUnknown", out var unknown));
        if (error == AnalysisResultError.CommitUnknown) Assert.True(unknown.GetBoolean());
        Assert.Equal(1, host.State.Calls);
    }

    [Theory]
    [InlineData(false, 500)]
    [InlineData(true, 503)]
    public async Task TechnicalErrors_DoNotLeakAndDoNotClaimUnknownCommit(bool database, int status)
    {
        using var host = new Host();
        host.State.Failure = database ? new SyntheticDatabaseException() : new InvalidOperationException("SYN-PRIVATE password");
        using var client = host.Client();
        var body = await Problem(await client.GetAsync(DetailRoute), status);
        Assert.False(body.TryGetProperty("outcomeUnknown", out _));
    }

    [Fact]
    public async Task MissingResultsAndRequests_Return404_ExistingListIncludesUnenteredItem()
    {
        using var host = new Host();
        using var client = host.Client();
        await Problem(await client.GetAsync(DetailRoute), 404);
        await Problem(await client.PutAsJsonAsync(DetailRoute, new { value = 0, version = Version }), 404);
        host.State.MissingRequest = true;
        await Problem(await client.GetAsync(ListRoute), 404);
    }

    [Fact]
    public async Task DuplicateAndStaleUpdates_Return409WithoutAutomaticResubmission()
    {
        using var host = new Host();
        using var client = host.Client();
        using var created = await client.PostAsJsonAsync(DetailRoute, new { value = 0 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await Problem(await client.PostAsJsonAsync(DetailRoute, new { value = 1 }), 409);
        await Problem(await client.PutAsJsonAsync(DetailRoute, new { value = 1, version = Convert.ToBase64String(new byte[8]) }), 409);
        Assert.Equal(3, host.State.Calls);
        Assert.Equal(0, host.State.Result!.Value);
        // Fake persistence verifies HTTP contracts only, not actual SQL concurrency.
    }

    [Fact]
    public async Task OpenApi_DescribesAllFourRoutesAndStrictInputsWithoutSql()
    {
        using var host = new Host();
        using var client = host.Client();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await Body(response);
        var paths = document.GetProperty("paths");
        var detail = paths.GetProperty("/api/laboratory-requests/{requestId}/items/{itemId}/result");
        foreach (var method in new[] { "post", "put", "get" })
        {
            var responses = detail.GetProperty(method).GetProperty("responses");
            Assert.True(responses.TryGetProperty(method == "post" ? "201" : "200", out _));
            foreach (var status in new[] { "400", "404", "409", "503", "500" }) Assert.True(responses.TryGetProperty(status, out _));
        }
        Assert.True(paths.GetProperty("/api/laboratory-requests/{requestId}/results").TryGetProperty("get", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { nameof(AnalysisResultInput), nameof(AnalysisResultUpdateInput) })
            Assert.Equal(JsonValueKind.False, schemas.GetProperty(name).GetProperty("additionalProperties").ValueKind);
        Assert.Equal(0, host.State.Resolutions);
    }

    [Fact]
    public async Task OtherEndpoints_RemainAvailableWhenResultsAreDisabled()
    {
        using var host = new Host(enabled: null);
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(0, host.State.Resolutions);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> Problem(HttpResponseMessage response, int status)
    {
        using (response)
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SYN-PRIVATE", text);
            Assert.DoesNotContain("SqlException", text);
            var body = JsonDocument.Parse(text).RootElement.Clone();
            Assert.Equal(status, body.GetProperty("status").GetInt32());
            Assert.True(body.TryGetProperty("traceId", out _));
            return body;
        }
    }

    private sealed class State
    {
        public int Resolutions, Calls;
        public bool MissingRequest;
        public string? ReceivedVersion;
        public Exception? Failure;
        public AnalysisResultDetailDto? Result;
    }
    private sealed class Host(string? enabled = "true", string environment = "Development",
        string? remote = "127.0.0.1", string? local = "127.0.0.1") : WebApplicationFactory<Program>
    {
        public State State { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // Do not write Windows EventLog or external telemetry from in-memory tests.
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { [AnalysisResultLocalAccess.ConfigurationKey] = enabled }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisResultStore>();
                services.AddScoped<IAnalysisResultStore>(provider =>
                {
                    State.Resolutions++;
                    return new FakeStore(State, provider.GetRequiredService<IAnalysisResultActorProvider>());
                });
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden in this HTTP test."));
                // Trusted test-server setup, not an HTTP header or client identity.
                services.AddSingleton<IStartupFilter>(new ConnectionSetup(remote, local));
            });
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }
    private sealed class ConnectionSetup(string? remote, string? local) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
                context.Connection.LocalIpAddress = local is null ? null : IPAddress.Parse(local);
                await continuation();
            });
            next(app);
        };
    }
    private sealed class FakeStore(State state, IAnalysisResultActorProvider actor) : IAnalysisResultStore
    {
        private void Call() { state.Calls++; if (state.Failure is not null) throw state.Failure; }
        public Task<AnalysisResultDetailDto> CreateAsync(Guid requestId, Guid itemId, decimal value, CancellationToken ct)
        {
            Call();
            if (state.Result is not null) throw AnalysisResultException.Conflict();
            state.Result = new(Guid.NewGuid(), requestId, itemId, Guid.NewGuid(), "SYN", "Synthetic", "unit", value,
                null, null, Abnormality.NotEvaluated, DateTimeOffset.UnixEpoch, actor.GetRequiredActor(), null, null, Version);
            return Task.FromResult(state.Result);
        }
        public Task<AnalysisResultDetailDto> UpdateAsync(Guid requestId, Guid itemId, decimal value, byte[] version, CancellationToken ct)
        {
            Call();
            if (state.Result is null) throw AnalysisResultException.NotFound();
            if (Convert.ToBase64String(version) != state.Result.Version) throw AnalysisResultException.Conflict();
            state.ReceivedVersion = Convert.ToBase64String(version);
            state.Result = state.Result! with { Value = value, EnteredBy = actor.GetRequiredActor(),
                Version = Convert.ToBase64String([2, 2, 3, 4, 5, 6, 7, 8]) };
            return Task.FromResult(state.Result);
        }
        public Task<AnalysisResultDetailDto?> GetAsync(Guid requestId, Guid itemId, CancellationToken ct)
        { Call(); return Task.FromResult(state.Result); }
        public Task<AnalysisResultListDto?> ListForRequestAsync(Guid requestId, CancellationToken ct)
        {
            Call();
            return Task.FromResult(state.MissingRequest ? null : new AnalysisResultListDto(requestId,
                [new(ItemId, Guid.NewGuid(), "SYN", "Synthetic", "unit", state.Result),
                    new(Guid.NewGuid(), Guid.NewGuid(), "SYN-EMPTY", "Synthetic unentered", "unit", null)]));
        }
    }
    private sealed class SyntheticDatabaseException : DbException
    { public SyntheticDatabaseException() : base("SYN-PRIVATE SQL password") { } }
}
