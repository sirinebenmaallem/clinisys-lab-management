using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Transactions;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.Workflow;

public sealed class RequestWorkflowStoreUnitTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];
    private sealed class Actor(string value) : IRequestWorkflowActorProvider
    { public string GetRequiredActor() => value; }
    private sealed class ForbiddenConnection : DbConnectionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData data, InterceptionResult result, CancellationToken ct = default)
        { Attempts++; throw new InvalidOperationException("SQL is forbidden in unit tests."); }
    }
    private static ClinisysDbContext Context(ForbiddenConnection guard, bool retry = false) =>
        new(new DbContextOptionsBuilder<ClinisysDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=UnitTest;Integrated Security=True",
                sql => { if (retry) sql.EnableRetryOnFailure(); }).AddInterceptors(guard).Options);

    [Fact]
    public async Task InvalidInputsActorRetriesAndAmbientTransactions_RejectBeforeAnyConnection()
    {
        var guard = new ForbiddenConnection();
        await using var db = Context(guard);
        var store = new RequestWorkflowStore(db, TimeProvider.System, new Actor(Guid.NewGuid().ToString()));
        foreach (var version in new byte[][] { [], new byte[7], new byte[9] })
            await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
                store.TransitionAsync(Guid.NewGuid(), RequestWorkflowAction.Start, version, default));
        await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
            store.TransitionAsync(Guid.Empty, RequestWorkflowAction.Start, Version, default));
        await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
            store.TransitionAsync(Guid.NewGuid(), (RequestWorkflowAction)99, Version, default));
        foreach (var actor in new[] { "", "LOCAL-UNAUTHENTICATED", Guid.Empty.ToString() })
            await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
                new RequestWorkflowStore(db, TimeProvider.System, new Actor(actor))
                    .TransitionAsync(Guid.NewGuid(), RequestWorkflowAction.Start, Version, default));
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.TransitionAsync(Guid.NewGuid(), RequestWorkflowAction.Start, Version, default));
        await using var retryDb = Context(guard, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RequestWorkflowStore(retryDb, TimeProvider.System, new Actor(Guid.NewGuid().ToString()))
                .TransitionAsync(Guid.NewGuid(), RequestWorkflowAction.Start, Version, default));
        Assert.Equal(0, guard.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Correction_AddsOnlyItsOwnAuditAndPreservesExistingEntries(bool sameValue)
    {
        using var db = Context(new ForbiddenConnection());
        var requestId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var instant = DateTimeOffset.UnixEpoch.AddMinutes(5);
        var result = new AnalysisResult(itemId, 1.123456m, "old-actor", new(0, 10), DateTimeOffset.UnixEpoch);
        typeof(Clinisys.Domain.Common.Entity).GetProperty(nameof(result.Version))!.SetValue(result, Version);
        var previousAudit = new AuditEntry(requestId, itemId, AuditAction.Created, "old-actor", "Value", null, "1.123456");
        db.Attach(previousAudit);
        var value = sameValue ? 1.123456m : 0m;
        InvokeCorrection(db, requestId, result, value, Version, "new-actor", instant);
        Assert.Equal(EntityState.Unchanged, db.Entry(previousAudit).State);
        var added = Assert.Single(db.ChangeTracker.Entries<AuditEntry>(), e => e.State == EntityState.Added).Entity;
        Assert.Equal(AuditAction.Updated, added.Action);
        Assert.Equal(requestId, added.LaboratoryRequestId);
        Assert.Equal(itemId, added.RequestItemId);
        Assert.Equal("new-actor", added.Actor);
        Assert.Equal(instant, added.OccurredAt);
        Assert.Equal("1.123456", added.OldValue);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), added.NewValue);
        Assert.Equal(0, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(10, result.ReferenceUpperBoundSnapshot);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("validated")]
    [InlineData("invalid")]
    public void FailedCorrection_AddsNoAuditAndChangesNothing(string reason)
    {
        using var db = Context(new ForbiddenConnection());
        var result = new AnalysisResult(Guid.NewGuid(), 1, "old", null, DateTimeOffset.UnixEpoch);
        typeof(Clinisys.Domain.Common.Entity).GetProperty(nameof(result.Version))!.SetValue(result, Version);
        if (reason == "validated") result.Validate("biologist", DateTimeOffset.UnixEpoch);
        Assert.Throws<AnalysisResultException>(() => InvokeCorrection(db, Guid.NewGuid(), result,
            reason == "invalid" ? 0.0000001m : 2, reason == "stale" ? new byte[8] : Version,
            "new", DateTimeOffset.UnixEpoch));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(1, result.Value);
        Assert.Equal("old", result.EnteredBy);
    }

    [Fact]
    public void WorkflowFailure_DoesNotTreatUnknownCommitAsRetryableConflict()
    {
        var unknown = LaboratoryRequestException.WorkflowUnknown();
        var translated = typeof(RequestWorkflowStore).GetMethod("Translate", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Same(unknown, translated.Invoke(null, [unknown]));
        var conflict = Assert.IsType<LaboratoryRequestException>(
            translated.Invoke(null, [new DbUpdateConcurrencyException()]));
        Assert.Equal(LaboratoryRequestError.Conflict, conflict.Error);
    }

    private static void InvokeCorrection(params object[] arguments)
    {
        try { typeof(AnalysisResultStore).GetMethod("PrepareAuditedCorrection", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
}
