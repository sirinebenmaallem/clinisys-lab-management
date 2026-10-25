using System.Net;
using System.Net.Http.Json;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.Workflow;

// Real routing, role policies, CSRF and commands. In-memory domain-backed persistence
// double; any accidental production DbContext resolution fails before connecting.
public sealed class RequestWorkflowApiTests
{
    [Theory]
    [InlineData(null, "start", 401)]
    [InlineData("Secretary", "start", 403)]
    [InlineData("Technician", "start", 200)]
    [InlineData("Biologist", "start", 200)]
    [InlineData("Secretary", "complete-entry", 403)]
    [InlineData("Technician", "complete-entry", 200)]
    [InlineData("Biologist", "complete-entry", 200)]
    [InlineData("Secretary", "validate", 403)]
    [InlineData("Technician", "validate", 403)]
    [InlineData("Biologist", "validate", 200)]
    [InlineData("Secretary", "deliver", 200)]
    [InlineData("Technician", "deliver", 403)]
    [InlineData("Biologist", "deliver", 200)]
    public async Task Roles_EnforcedByRealPolicies(string? role, string action, int expected)
    {
        using var host = new Host(role);
        host.State.PrepareFor(action);
        using var client = host.AuthenticatedClient();
        using var response = await client.PostAsJsonAsync(host.Route(action), new { version = host.State.Token });
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(expected == 200 ? 1 : 0, host.State.Calls);
        if (action == "validate" && expected == 200)
            Assert.Equal(AuthenticatedApiTestSupport.ActorId.ToString(), host.State.Request.ValidatedBy);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete-entry")]
    [InlineData("validate")]
    [InlineData("deliver")]
    public async Task Writes_RequireCsrf(string action)
    {
        using var host = new Host("Biologist");
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        using var response = await client.PostAsJsonAsync(host.Route(action), new { version = host.State.Token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.State.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("AQID")]
    public async Task InvalidVersions_RejectBeforeStore(string? version)
    {
        using var host = new Host("Biologist");
        using var client = host.AuthenticatedClient();
        using var response = await client.PostAsJsonAsync(host.Route("start"), new { version });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.State.Calls);
    }

    [Fact]
    public async Task Workflow_CompletenessVersionAndTerminalStates()
    {
        using var host = new Host("Biologist");
        using var client = host.AuthenticatedClient();
        async Task<HttpResponseMessage> Send(string action, string? token = null) =>
            await client.PostAsJsonAsync(host.Route(action), new { version = token ?? host.State.Token });
        using var earlyDelivery = await Send("deliver");
        Assert.Equal(HttpStatusCode.Conflict, earlyDelivery.StatusCode);
        var initialVersion = host.State.Token;
        using var started = await Send("start");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        Assert.NotEqual(initialVersion, body.RootElement.GetProperty("version").GetString());
        using var stale = await Send("complete-entry", initialVersion);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        host.State.Complete = false;
        using var incomplete = await Send("complete-entry");
        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        host.State.Complete = true;
        using var entered = await Send("complete-entry");
        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        host.State.Complete = false;
        using var notValidatable = await Send("validate");
        Assert.Equal(HttpStatusCode.Conflict, notValidatable.StatusCode);
        Assert.Null(host.State.Request.ValidatedAt);
        host.State.Complete = true;
        using var validated = await Send("validate");
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        Assert.Throws<InvalidOperationException>(host.State.Request.EnsureResultsEditable);
        using var repeated = await Send("validate");
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var delivered = await Send("deliver");
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        using var restart = await Send("start");
        Assert.Equal(HttpStatusCode.Conflict, restart.StatusCode);
        Assert.Equal(RequestStatus.Delivered, host.State.Request.Status);
    }

    [Fact]
    public async Task MissingRequestAndForbiddenClientFields()
    {
        using var host = new Host("Biologist");
        using var client = host.AuthenticatedClient();
        using var missing = await client.PostAsJsonAsync($"/api/laboratory-requests/{Guid.NewGuid()}/start",
            new { version = host.State.Token });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var field in new[] { "actor", "validatedBy", "validatedAt", "status" })
        {
            using var response = await client.PostAsJsonAsync(host.Route("start"),
                new Dictionary<string, string> { ["version"] = host.State.Token, [field] = "SYN-PRIVATE" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("SYN-PRIVATE", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task UnknownCommit_IsSanitizedAndNeverReplayed()
    {
        using var host = new Host("Biologist");
        host.State.Failure = LaboratoryRequestException.WorkflowUnknown();
        using var client = host.AuthenticatedClient();
        using var response = await client.PostAsJsonAsync(host.Route("start"), new { version = host.State.Token });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Equal(1, host.State.Calls);
    }

    private sealed class Host(string? role) : WebApplicationFactory<Program>
    {
        public State State { get; } = new();
        public string Route(string action) => $"/api/laboratory-requests/{State.Request.Id}/{action}";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddTestAuthentication(role is null ? [] : [role]);
                services.RemoveAll<IRequestWorkflowStore>();
                services.AddScoped<IRequestWorkflowStore>(sp => new Store(State, sp.GetRequiredService<IAnalysisResultActorProvider>()));
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden."));
            });
        }
    }
    private sealed class State
    {
        public LaboratoryRequest Request { get; } = LaboratoryRequest.Register("LAB-2026-000001",
            new Patient("Synthetic", "Workflow", new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY"),
            "Synthetic", 0, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public long Revision = 1;
        public string Token => Convert.ToBase64String(BitConverter.GetBytes(Revision));
        public bool Complete = true;
        public int Calls;
        public Exception? Failure;
        public void PrepareFor(string action)
        {
            if (action != "start") Request.StartProcessing();
            if (action is "validate" or "deliver") Request.MarkResultsEntered(true);
            if (action == "deliver") Request.ValidateResults(true, "synthetic-biologist", DateTimeOffset.UtcNow);
        }
    }
    private sealed class Store(State state, IAnalysisResultActorProvider actor) : IRequestWorkflowStore
    {
        public Task<RequestWorkflowDto> TransitionAsync(Guid requestId, RequestWorkflowAction action, byte[] version, CancellationToken ct)
        {
            state.Calls++;
            if (state.Failure is not null) throw state.Failure;
            if (requestId != state.Request.Id) throw LaboratoryRequestException.NotFound();
            if (Convert.ToBase64String(version) != state.Token) throw LaboratoryRequestException.WorkflowConflict();
            var now = DateTimeOffset.UtcNow;
            try
            {
                switch (action)
                {
                    case RequestWorkflowAction.Start: state.Request.StartProcessing(); break;
                    case RequestWorkflowAction.CompleteEntry: state.Request.MarkResultsEntered(state.Complete); break;
                    case RequestWorkflowAction.Validate: state.Request.ValidateResults(state.Complete, actor.GetRequiredActor(), now); break;
                    case RequestWorkflowAction.Deliver: state.Request.Deliver(now); break;
                }
            }
            catch (InvalidOperationException) { throw LaboratoryRequestException.WorkflowConflict(); }
            state.Revision++;
            return Task.FromResult(new RequestWorkflowDto(requestId, state.Request.Status, state.Token,
                state.Request.ValidatedAt, state.Request.ValidatedBy, state.Request.DeliveredAt));
        }
    }
}
