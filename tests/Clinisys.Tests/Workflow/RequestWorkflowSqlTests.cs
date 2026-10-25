using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.AnalysisResults;
using Clinisys.Tests.Authentication;
using Clinisys.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.Workflow;

// Existing opt-in guard, restricted connections and owned synthetic fixture.
// HTTP uses the existing test-only role handler; production policies, CSRF,
// commands and SQL stores remain real. Identity cookie behavior has separate tests.
[Collection(AnalysisResultSqlCollection.Name)]
public sealed class RequestWorkflowSqlTests
{
    private static readonly string Actor = AuthenticatedApiTestSupport.ActorId.ToString();

    [SqlServerFact]
    public async Task FullLifecycle_PersistsValidationLocksResultsAndAuditsOnlyOwnedChanges()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var host = new Host(AuthenticationRoles.Biologist);
        using var client = host.AuthenticatedClient();
        await using var db = Context();
        var original = await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
        var version = Convert.ToBase64String(original.Version);
        version = await Post(client, fixture, "start", version, HttpStatusCode.OK, ct);
        var results = AnalysisResultSqlFixture.Store(db, Actor);
        var first = await results.CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, ct);
        await results.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 2,
            Convert.FromBase64String(first.Version), ct);
        await results.CreateAsync(fixture.Request.Id, fixture.EmptyItem.Id, 0, ct);
        version = await Post(client, fixture, "complete-entry", version, HttpStatusCode.OK, ct);
        version = await Post(client, fixture, "validate", version, HttpStatusCode.OK, ct);

        var validated = await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
        Assert.Equal(RequestStatus.Validated, validated.Status);
        Assert.Equal(Actor, validated.ValidatedBy);
        Assert.NotNull(validated.ValidatedAt);
        var itemIds = new[] { fixture.Item.Id, fixture.EmptyItem.Id };
        var storedResults = await db.AnalysisResults.AsNoTracking().Where(x => itemIds.Contains(x.RequestItemId)).ToListAsync(ct);
        Assert.Equal(2, storedResults.Count);
        Assert.All(storedResults, r =>
        {
            Assert.Equal(Actor, r.ValidatedBy);
            Assert.Equal(validated.ValidatedAt, r.ValidatedAt);
        });
        var firstStored = Assert.Single(storedResults, r => r.RequestItemId == fixture.Item.Id);
        var error = await Assert.ThrowsAsync<AnalysisResultException>(() => results.UpdateAsync(
            fixture.Request.Id, fixture.Item.Id, 99, firstStored.Version, ct));
        Assert.Equal(AnalysisResultError.Conflict, error.Error);
        version = await Post(client, fixture, "deliver", version, HttpStatusCode.OK, ct);
        await Post(client, fixture, "start", version, HttpStatusCode.Conflict, ct);
        await Post(client, fixture, "validate", version, HttpStatusCode.Conflict, ct);
        var delivered = await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
        Assert.Equal(RequestStatus.Delivered, delivered.Status);
        Assert.True(delivered.DeliveredAt >= delivered.ValidatedAt);
        Assert.Equal(original.Number, delivered.Number);
        Assert.Equal(original.PatientDateOfBirthSnapshot, delivered.PatientDateOfBirthSnapshot);
        Assert.Equal(original.PatientSexSnapshot, delivered.PatientSexSnapshot);
        Assert.Equal(2, await db.AnalysisResults.Where(x => x.Id == firstStored.Id).Select(x => x.Value).SingleAsync(ct));

        var audits = await db.AuditEntries.AsNoTracking().Where(x => x.LaboratoryRequestId == fixture.Request.Id).ToListAsync(ct);
        Assert.Equal(7, audits.Count); // two creations, one correction, four transitions
        Assert.All(audits, a => Assert.Equal(Actor, a.Actor));
        var correction = Assert.Single(audits, a => a.Action == AuditAction.Updated);
        Assert.Equal(1m, decimal.Parse(correction.OldValue!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2m, decimal.Parse(correction.NewValue!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(fixture.Item.Id, correction.RequestItemId);
        var validation = Assert.Single(audits, a => a.Action == AuditAction.Validated);
        Assert.Equal(validated.ValidatedAt, validation.OccurredAt);
        Assert.Equal("ResultsEntered", validation.OldValue);
        Assert.Equal("Validated", validation.NewValue);
        Assert.Single(audits, a => a.Action == AuditAction.Delivered);
    }

    [SqlServerFact]
    public async Task MissingResultsAndIllegalTransitions_DoNotPersistPartialValidation()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        await using var db = Context();
        var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id, ct);
        var store = Store(db);
        foreach (var action in new[] { RequestWorkflowAction.CompleteEntry, RequestWorkflowAction.Validate, RequestWorkflowAction.Deliver })
        {
            var ex = await Assert.ThrowsAsync<LaboratoryRequestException>(() => store.TransitionAsync(request.Id, action, request.Version, ct));
            Assert.Equal(LaboratoryRequestError.Conflict, ex.Error);
        }
        Assert.False(await db.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == request.Id, ct));
        request.StartProcessing();
        await db.SaveChangesAsync(ct);
        var incomplete = await Assert.ThrowsAsync<LaboratoryRequestException>(() => store.TransitionAsync(
            request.Id, RequestWorkflowAction.CompleteEntry, request.Version, ct));
        Assert.Equal(LaboratoryRequestError.Conflict, incomplete.Error);
        // Simulate a legacy/inconsistent ResultsEntered request to prove validation
        // itself checks completeness, independently of the preceding transition.
        request.MarkResultsEntered(true);
        await db.SaveChangesAsync(ct);
        await AnalysisResultSqlFixture.Store(db, Actor).CreateAsync(request.Id, fixture.Item.Id, 1, ct);
        var before = request.Version.ToArray();
        var failed = await Assert.ThrowsAsync<LaboratoryRequestException>(() => store.TransitionAsync(
            request.Id, RequestWorkflowAction.Validate, before, ct));
        Assert.Equal(LaboratoryRequestError.Conflict, failed.Error);
        await using var verify = Context();
        var persisted = await verify.LaboratoryRequests.SingleAsync(x => x.Id == request.Id, ct);
        Assert.Equal(before, persisted.Version);
        Assert.Equal(RequestStatus.ResultsEntered, persisted.Status);
        Assert.Null(persisted.ValidatedAt);
        Assert.Null(persisted.ValidatedBy);
        var result = await verify.AnalysisResults.SingleAsync(x => x.RequestItemId == fixture.Item.Id, ct);
        Assert.Null(result.ValidatedAt);
        Assert.Null(result.ValidatedBy);
        Assert.False(await verify.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == request.Id && x.Action == AuditAction.Validated, ct));
    }

    [SqlServerFact]
    public async Task SecretaryAndTechnician_CannotValidate_EvenWhenAllResultsExist()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var db = Context();
        var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id, ct);
        request.StartProcessing(); request.MarkResultsEntered(true);
        await db.SaveChangesAsync(ct);
        await AnalysisResultSqlFixture.Store(db, Actor).CreateAsync(request.Id, fixture.Item.Id, 1, ct);
        await AnalysisResultSqlFixture.Store(db, Actor).CreateAsync(request.Id, fixture.EmptyItem.Id, 2, ct);
        foreach (var role in new[] { AuthenticationRoles.Secretary, AuthenticationRoles.Technician })
        {
            using var host = new Host(role);
            using var client = host.AuthenticatedClient();
            await Post(client, fixture, "validate", Convert.ToBase64String(request.Version), HttpStatusCode.Forbidden, ct);
        }
        await using var verify = Context();
        Assert.Null((await verify.LaboratoryRequests.SingleAsync(x => x.Id == request.Id, ct)).ValidatedAt);
        Assert.False(await verify.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == request.Id && x.Action == AuditAction.Validated, ct));
    }

    [SqlServerFact]
    public async Task IndependentConnections_SameVersion_OneTransitionAndNoLostChanges()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        await using var first = Context();
        await using var second = Context();
        await first.Database.OpenConnectionAsync(ct);
        await second.Database.OpenConnectionAsync(ct);
        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        var initial = await first.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(RequestWorkflowDto? Result, Exception? Error)> Attempt(ClinisysDbContext context)
        {
            await gate.Task.WaitAsync(ct);
            try { return (await Store(context).TransitionAsync(initial.Id, RequestWorkflowAction.Start, initial.Version.ToArray(), ct), null); }
            catch (Exception ex) { return (null, ex); }
        }
        var left = Attempt(first);
        var right = Attempt(second);
        gate.SetResult();
        // Await both operations before contexts or fixture can be disposed.
        var outcomes = await Task.WhenAll(left, right);
        var success = Assert.Single(outcomes, x => x.Error is null).Result!;
        var failure = Assert.Single(outcomes, x => x.Error is not null).Error;
        Assert.Equal(LaboratoryRequestError.Conflict, Assert.IsType<LaboratoryRequestException>(failure).Error);
        Assert.NotEqual(Convert.ToBase64String(initial.Version), success.Version);
        await using var verify = Context();
        var persisted = await verify.LaboratoryRequests.SingleAsync(x => x.Id == initial.Id, ct);
        Assert.Equal(RequestStatus.InProgress, persisted.Status);
        Assert.Equal(success.Version, Convert.ToBase64String(persisted.Version));
        Assert.Equal(initial.Number, persisted.Number);
        Assert.Equal(initial.DiscountPercent, persisted.DiscountPercent);
        var audit = Assert.Single(await verify.AuditEntries.Where(x => x.LaboratoryRequestId == initial.Id).ToListAsync(ct));
        Assert.Equal("Registered", audit.OldValue);
        Assert.Equal("InProgress", audit.NewValue);
    }

    [SqlServerFact]
    public async Task CallerRollback_RemovesTransitionAndAudit_WithoutStoreCommit()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        await using (var db = Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            var initial = await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
            var started = await Store(db).TransitionAsync(initial.Id, RequestWorkflowAction.Start, initial.Version, ct);
            Assert.Equal(RequestStatus.InProgress, started.Status);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.True(await db.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == initial.Id, ct));
            var conflict = await Assert.ThrowsAsync<LaboratoryRequestException>(() => Store(db).TransitionAsync(
                initial.Id, RequestWorkflowAction.Validate, Convert.FromBase64String(started.Version), ct));
            Assert.Equal(LaboratoryRequestError.Conflict, conflict.Error);
            Assert.Equal(RequestStatus.InProgress, (await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == initial.Id, ct)).Status);
            await transaction.RollbackAsync(ct);
        }
        await using var verify = Context();
        Assert.Equal(RequestStatus.Registered, (await verify.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id, ct)).Status);
        Assert.False(await verify.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == fixture.Request.Id, ct));
    }

    private static ClinisysDbContext Context()
    {
        var db = SqlServerTestDatabase.CreateContext();
        db.Database.SetCommandTimeout(10);
        return db;
    }
    private static RequestWorkflowStore Store(ClinisysDbContext db) => new(db, TimeProvider.System, new SyntheticActor());
    private sealed class SyntheticActor : IAnalysisResultActorProvider { public string GetRequiredActor() => Actor; }

    private static async Task<string> Post(HttpClient client, AnalysisResultSqlFixture fixture, string action,
        string version, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync($"/api/laboratory-requests/{fixture.Request.Id}/{action}", new { version }, ct);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (expected != HttpStatusCode.OK) return version;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("version").GetString()!;
    }

    private sealed class Host(string role) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddTestAuthentication(role);
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped(_ => Context());
            });
        }
    }
}
