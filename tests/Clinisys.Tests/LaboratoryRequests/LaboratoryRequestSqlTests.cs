using System.Collections.Concurrent;
using System.Data.Common;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.AnalysisResults;
using Clinisys.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.LaboratoryRequests;

// Written for the approved migrated test schema. Never migrate or create a database from tests.
public sealed class LaboratoryRequestSqlTests
{
    [SqlServerFact]
    public Task ConfirmedDeadlock_ReplaysWholeOwnedCreationWithFreshContext() => Deadlock(borrowed: false);

    [SqlServerFact]
    public Task ConfirmedDeadlock_InCallerTransaction_DoesNotRetryOrRollbackToSavepoint() => Deadlock(borrowed: true);

    private static async Task Deadlock(bool borrowed)
    {
        var year = borrowed ? 7109 : 7106;
        var patient = Patient();
        var analysis = Analysis();
        var panel = new AnalysisPanel(Code(), "Synthetic panel");
        await Seed(year, patient, analysis, panel, new PanelAnalysis(panel.Id, analysis.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task<LaboratoryRequestDetailDto>? creation = null;
        using var seen = new PanelSeen();
        try
        {
            await using var writer = SqlServerTestDatabase.CreateContext();
            await writer.Database.OpenConnectionAsync(timeout.Token);
            await writer.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH", timeout.Token);
            await using var transaction = await writer.Database.BeginTransactionAsync(timeout.Token);
            // Deliberately reverse the production lock order in this competing test-only writer.
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=11 WHERE Id={analysis.Id}", timeout.Token);
            await using var source = SqlServerTestDatabase.CreateContext(seen);
            await using var callerTransaction = borrowed ? await source.Database.BeginTransactionAsync(timeout.Token) : null;
            try
            {
                creation = seen.ObserveAsync(() => new LaboratoryRequestStore(source, new FixedClock(year)).CreateAsync(
                    new(patient.Id,"Synthetic",PanelIds:[panel.Id]),timeout.Token));
                await seen.Ready.Task.WaitAsync(timeout.Token);
                // Store owns panel U and needs analysis S; this writer owns analysis X and needs panel X.
                // SQL Server must choose the lower-priority store transaction as deadlock victim.
                await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE AnalysisPanels SET Name=N'Changed synthetic panel' WHERE Id={panel.Id}", timeout.Token);
                await transaction.CommitAsync(timeout.Token);
                // Only a successful commit releases the retry, before it acquires any panel lock.
                seen.WriterCommitted();
                if (borrowed)
                {
                    // A savepoint rollback on the dead transaction would instead produce CommitUnknown.
                    var error = await Assert.ThrowsAsync<LaboratoryRequestException>(() => creation.WaitAsync(timeout.Token));
                    Assert.Equal(LaboratoryRequestError.Conflict, error.Error);
                    Assert.Equal(1, seen.NativeDeadlocks);
                    Assert.Single(seen.Contexts);
                    await using var verify = SqlServerTestDatabase.CreateContext();
                    Assert.False(await verify.LaboratoryRequests.AnyAsync(x => x.PatientId == patient.Id, timeout.Token));
                    Assert.False(await verify.LaboratoryRequestCounters.AnyAsync(x => x.Year == year, timeout.Token));
                    return;
                }
                var result = await creation.WaitAsync(timeout.Token);
                Assert.Equal(2, seen.Contexts.Distinct().Count());
                Assert.Equal(1, seen.NativeDeadlocks);
                Assert.Equal(11, result.Total);
                Assert.Equal("Changed synthetic panel", Assert.Single(result.Panels).PanelNameSnapshot);
                Assert.Equal($"LAB-{year}-000001", result.Number);
                await using var persisted = SqlServerTestDatabase.CreateContext();
                var stored = Assert.Single(await persisted.LaboratoryRequests.AsNoTracking()
                    .Where(x => x.PatientId == patient.Id).ToListAsync(timeout.Token));
                Assert.Equal(result.Id, stored.Id);
                Assert.Equal(result.Number, stored.Number);
                Assert.Single(await persisted.RequestItems.Where(x => x.LaboratoryRequestId == result.Id).ToListAsync(timeout.Token));
                Assert.Single(await persisted.RequestPanels.Where(x => x.LaboratoryRequestId == result.Id).ToListAsync(timeout.Token));
                Assert.Equal(1, (await persisted.LaboratoryRequestCounters.AsNoTracking()
                    .SingleAsync(x => x.Year == year, timeout.Token)).LastValue);
            }
            finally
            {
                // Cancel/drain BEFORE disposing the caller transaction, contexts or writer.
                // Do not abandon an active SQL task to begin fixture cleanup.
                timeout.Cancel();
                if (creation is not null) { try { await creation; } catch { } }
            }
        }
        finally
        {
            await Cleanup(year,patient,analysis,panel);
        }
    }

    [SqlServerFact]
    public async Task LostCommitAcknowledgement_DoesNotReplayOrReportFalseRollback()
    {
        const int year = 7107;
        var patient = Patient();
        var analysis = Analysis();
        await Seed(year,patient,analysis);
        var acknowledgement = new LoseAcknowledgement();
        try
        {
            await using var source = SqlServerTestDatabase.CreateContext(acknowledgement);
            var error = await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
                new LaboratoryRequestStore(source,new FixedClock(year)).CreateAsync(
                    new(patient.Id,"Synthetic",AnalysisIds:[analysis.Id]),default));
            Assert.Equal(LaboratoryRequestError.CommitUnknown,error.Error);
            Assert.Equal(1,acknowledgement.Commits);
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Single(await verify.LaboratoryRequests.Where(x => x.PatientId == patient.Id).ToListAsync());
            Assert.Equal(1,(await verify.LaboratoryRequestCounters.SingleAsync(x => x.Year == year)).LastValue);
        }
        finally { await Cleanup(year,patient,analysis); }
    }

    [SqlServerFact]
    public async Task IndependentConcurrentCreates_AllocateFirstAndSecondNumbersAtomically()
    {
        const int year = 7101;
        var patient = Patient();
        var analysis = Analysis();
        await Seed(year, patient, analysis);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var gate = new PatientGate(2);
            await using var first = SqlServerTestDatabase.CreateContext(gate);
            await using var second = SqlServerTestDatabase.CreateContext(gate);
            var input = new LaboratoryRequestInput(patient.Id, "Synthetic physician", AnalysisIds: [analysis.Id]);
            var results = await Task.WhenAll(
                new LaboratoryRequestStore(first, new FixedClock(year)).CreateAsync(input, timeout.Token),
                new LaboratoryRequestStore(second, new FixedClock(year)).CreateAsync(input, timeout.Token));
            Assert.Equal(2, gate.ServerSessions.Distinct().Count());
            Assert.Equal(new[] { $"LAB-{year}-000001", $"LAB-{year}-000002" }, results.Select(x => x.Number).Order());
            Assert.All(results, x => Assert.Equal(new FixedClock(year).GetUtcNow(), x.RegisteredAt));
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Equal(2, (await verify.LaboratoryRequestCounters.SingleAsync(x => x.Year == year)).LastValue);
            Assert.Equal(2, await verify.LaboratoryRequests.CountAsync(x => x.PatientId == patient.Id));
        }
        finally { await Cleanup(year, patient, analysis); }
    }

    [SqlServerFact]
    public async Task SaveFailure_RollsBackCounterInOwnedAndBorrowedTransactions()
    {
        const int year = 7102;
        var patient = Patient();
        var analysis = Analysis();
        await Seed(year, patient, analysis);
        try
        {
            var input = new LaboratoryRequestInput(patient.Id, "Synthetic", AnalysisIds: [analysis.Id]);
            await using (var owned = SqlServerTestDatabase.CreateContext(new FailRequestSave()))
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new LaboratoryRequestStore(owned, new FixedClock(year)).CreateAsync(input, default));
            await using (var borrowed = SqlServerTestDatabase.CreateContext(new FailRequestSave()))
            {
                await using var transaction = await borrowed.Database.BeginTransactionAsync();
                var tracked = await borrowed.Patients.SingleAsync(x => x.Id == patient.Id);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new LaboratoryRequestStore(borrowed, new FixedClock(year)).CreateAsync(input, default));
                Assert.Equal(EntityState.Unchanged, borrowed.Entry(tracked).State);
                Assert.DoesNotContain(borrowed.ChangeTracker.Entries(), x => x.Entity is LaboratoryRequest or RequestItem or RequestPanel);
                await transaction.CommitAsync(); // Must not commit a stranded counter increment.
            }
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.False(await verify.LaboratoryRequests.AnyAsync(x => x.PatientId == patient.Id));
            Assert.False(await verify.LaboratoryRequestCounters.AnyAsync(x => x.Year == year));
            var result = await new LaboratoryRequestStore(verify, new FixedClock(year)).CreateAsync(input, default);
            Assert.Equal($"LAB-{year}-000001", result.Number);
        }
        finally { await Cleanup(year, patient, analysis); }
    }

    [SqlServerFact]
    public async Task NewYearAndNewStore_KeepCountersIndependentAndRejectExhaustion()
    {
        const int year = 7103;
        var patient = Patient();
        var analysis = Analysis();
        // Refuse pre-existing high-water marks before entering any cleanup scope.
        await using (var preflight = SqlServerTestDatabase.CreateContext())
            await AssertYearsAvailable(preflight, year, year + 1);
        await Seed(year, patient, analysis);
        var createdFollowingYear = false;
        try
        {
            await using var context = SqlServerTestDatabase.CreateContext();
            var input = new LaboratoryRequestInput(patient.Id, "Synthetic", AnalysisIds: [analysis.Id]);
            var first = await new LaboratoryRequestStore(context, new FixedClock(year)).CreateAsync(input, default);
            var second = await new LaboratoryRequestStore(context, new FixedClock(year)).CreateAsync(input, default);
            var followingYear = await new LaboratoryRequestStore(context, new FixedClock(year + 1)).CreateAsync(input, default);
            createdFollowingYear = true;
            Assert.Equal($"LAB-{year}-000001", first.Number);
            Assert.Equal($"LAB-{year}-000002", second.Number);
            Assert.Equal($"LAB-{year + 1}-000001", followingYear.Number);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequestCounters SET LastValue = 999999 WHERE [Year] = {year}");
            Assert.Equal(LaboratoryRequestError.Conflict, (await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
                new LaboratoryRequestStore(context, new FixedClock(year)).CreateAsync(input, default))).Error);
            Assert.Equal(999999, (await context.LaboratoryRequestCounters.AsNoTracking().SingleAsync(x => x.Year == year)).LastValue);
            Assert.Equal(3, await context.LaboratoryRequests.CountAsync(x => x.PatientId == patient.Id));
        }
        finally
        {
            await Cleanup(year, patient, analysis);
            if (createdFollowingYear)
            {
                await using var cleanup = SqlServerTestDatabase.CreateContext();
                await cleanup.LaboratoryRequestCounters.Where(x => x.Year == year + 1 &&
                    !cleanup.LaboratoryRequests.Any(r => r.Number.StartsWith("LAB-7104-"))).ExecuteDeleteAsync();
            }
        }
    }

    [SqlServerFact]
    public async Task YearPreflight_RejectsAndPreservesExistingCounter()
    {
        await using var context = SqlServerTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        const int year = 7108;
        await AssertYearsAvailable(context, year);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT LaboratoryRequestCounters ([Year], LastValue) VALUES ({year}, 42)");
        try
        {
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => AssertYearsAvailable(context, year));
            Assert.Equal(42, (await context.LaboratoryRequestCounters.AsNoTracking().SingleAsync(x => x.Year == year)).LastValue);
        }
        finally { await transaction.RollbackAsync(); }
    }

    private static async Task AssertYearsAvailable(ClinisysDbContext context, params int[] years) =>
        Assert.False(await context.LaboratoryRequestCounters.AnyAsync(x => years.Contains(x.Year)));

    [SqlServerFact]
    public async Task HeldReads_BlockCatalogueCompositionPriceAndPatientChangesUntilRegistration()
    {
        const int year = 7105;
        var patient = Patient();
        var analysis = Analysis();
        var extra = Analysis();
        var panel = new AnalysisPanel(Code(), "Synthetic panel");
        await Seed(year, patient, analysis, extra, panel, new PanelAnalysis(panel.Id, analysis.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var gate = new PatientGate(1, manualRelease: true);
        Task<LaboratoryRequestDetailDto>? creation = null;
        try
        {
            await using var source = SqlServerTestDatabase.CreateContext(gate);
            creation = new LaboratoryRequestStore(source, new FixedClock(year)).CreateAsync(
                new(patient.Id, "Synthetic", AnalysisIds: [analysis.Id], PanelIds: [panel.Id]), timeout.Token);
            await gate.Ready.Task.WaitAsync(timeout.Token);
            await using var writer = SqlServerTestDatabase.CreateContext();
            await writer.Database.OpenConnectionAsync(timeout.Token);
            await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0", timeout.Token);
            // Nonblocking SQL lock probes, not timing-based sleeps: each must fail because the read is still protected.
            await Locked(() => writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=99 WHERE Id={analysis.Id}", timeout.Token));
            await Locked(() => writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE Patients SET Sex=2 WHERE Id={patient.Id}", timeout.Token));
            var link = new PanelAnalysis(panel.Id, extra.Id);
            await Locked(() => writer.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT PanelAnalyses (Id,AnalysisPanelId,MedicalAnalysisId) VALUES ({link.Id},{panel.Id},{extra.Id})", timeout.Token));
            gate.Release();
            var result = await creation;
            Assert.Single(result.Items);
            Assert.Equal(10, result.Total);
            Assert.Equal(Sex.Female, result.PatientSexSnapshot);
            await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1", timeout.Token);
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=99 WHERE Id={analysis.Id}", timeout.Token);
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE Patients SET Sex=2 WHERE Id={patient.Id}", timeout.Token);
            var historical = await new LaboratoryRequestStore(writer, new FixedClock(year)).GetAsync(result.Id, timeout.Token);
            Assert.Equal(10, historical!.Total);
            Assert.Equal(Sex.Female, historical.PatientSexSnapshot);
        }
        finally
        {
            gate.Release();
            if (creation is not null) { try { await creation; } catch { /* Cleanup must run even after an assertion failure. */ } }
            await Cleanup(year, patient, analysis, extra, panel);
        }
    }

    private static async Task Locked(Func<Task> write) =>
        Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(write)).Number);
    private static string Code() => $"SYN{Guid.NewGuid():N}"[..25];
    private static Patient Patient() => new("Synthetic", "Request SQL", new(2000,1,1), Sex.Female, phoneNumber: "TEST-ONLY");
    private static MedicalAnalysis Analysis() => new(Code(), "Synthetic", "unit", 10);
    private static async Task Seed(int year, Patient patient, params object[] others)
    {
        await using var context = SqlServerTestDatabase.CreateContext();
        Assert.False(await context.LaboratoryRequestCounters.AnyAsync(x => x.Year == year));
        context.Add(patient);
        context.AddRange(others);
        await context.SaveChangesAsync();
    }
    private static async Task Cleanup(int year, Patient patient, params object[] catalogue)
    {
        await using var context = SqlServerTestDatabase.CreateContext();
        var ids = context.LaboratoryRequests.Where(x => x.PatientId == patient.Id).Select(x => x.Id);
        await context.RequestPanels.Where(x => ids.Contains(x.LaboratoryRequestId)).ExecuteDeleteAsync();
        await context.RequestItems.Where(x => ids.Contains(x.LaboratoryRequestId)).ExecuteDeleteAsync();
        await context.LaboratoryRequests.Where(x => x.PatientId == patient.Id).ExecuteDeleteAsync();
        await context.Patients.Where(x => x.Id == patient.Id).ExecuteDeleteAsync();
        foreach (var panel in catalogue.OfType<AnalysisPanel>())
        {
            await context.PanelAnalyses.Where(x => x.AnalysisPanelId == panel.Id).ExecuteDeleteAsync();
            await context.AnalysisPanels.Where(x => x.Id == panel.Id).ExecuteDeleteAsync();
        }
        foreach (var analysis in catalogue.OfType<MedicalAnalysis>())
            await context.MedicalAnalyses.Where(x => x.Id == analysis.Id).ExecuteDeleteAsync();
        var prefix = $"LAB-{year}-";
        await context.LaboratoryRequestCounters.Where(x => x.Year == year &&
            !context.LaboratoryRequests.Any(r => r.Number.StartsWith(prefix))).ExecuteDeleteAsync();
    }

    internal sealed class FixedClock(int year) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(year,12,31,23,59,59,TimeSpan.Zero); }
    private sealed class FailRequestSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<LaboratoryRequest>().Any()) throw new InvalidOperationException("Synthetic failure after allocation.");
            return ValueTask.FromResult(result);
        }
    }
    internal sealed class RetryAfterWriterCommit
    {
        private int attempts;
        private readonly TaskCompletionSource committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task BeforeAttemptAsync(CancellationToken ct) => Interlocked.Increment(ref attempts) == 1
            ? Task.CompletedTask : committed.Task.WaitAsync(ct);
        internal void WriterCommitted() => committed.TrySetResult();
    }

    private sealed class PanelSeen : DbCommandInterceptor, IDisposable
    {
        private readonly RetryAfterWriterCommit retry = new();
        private readonly SqlDeadlockObserver deadlocks = new();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<Guid> Contexts { get; } = [];
        public int NativeDeadlocks => deadlocks.Count;
        public Task<T> ObserveAsync<T>(Func<Task<T>> action) => deadlocks.ObserveAsync(action);
        public void WriterCommitted() => retry.WriterCommitted();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("dbo.AnalysisPanels WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal))
                await retry.BeforeAttemptAsync(cancellationToken);
            return result;
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData eventData,
            DbDataReader result,CancellationToken cancellationToken=default)
        {
            if(command.CommandText.Contains("dbo.AnalysisPanels WITH (UPDLOCK,HOLDLOCK)",StringComparison.Ordinal))
            { Contexts.Add(eventData.Context!.ContextId.InstanceId); Ready.TrySetResult(); }
            return ValueTask.FromResult(result);
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        { deadlocks.Observe(eventData.Exception); return Task.CompletedTask; }
        public void Dispose() => deadlocks.Dispose();
    }
    private sealed class LoseAcknowledgement : DbTransactionInterceptor
    {
        public int Commits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData eventData,
            CancellationToken cancellationToken=default)
        { Commits++; throw new SyntheticCommitException(); }
    }
    private sealed class SyntheticCommitException : DbException { }
    private sealed class PatientGate(int participants, bool manualRelease = false) : DbCommandInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<int> ServerSessions { get; } = [];
        public void Release() => _release.TrySetResult();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("dbo.Patients WITH (HOLDLOCK)", StringComparison.Ordinal))
            {
                Assert.Equal("ClinisysTestDb", command.Connection!.Database);
                ServerSessions.Add(((SqlConnection)command.Connection).ServerProcessId);
                if (Interlocked.Increment(ref _arrivals) == participants)
                { Ready.TrySetResult(); if (!manualRelease) Release(); }
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}

// No SQL: tests only the synchronization primitive used by the scenario above.
public sealed class LaboratoryRequestRetryGateTests
{
    [Fact]
    public async Task RetryWaitsForCommit_WhileFirstAttemptIsNotBlocked()
    {
        var gate = new LaboratoryRequestSqlTests.RetryAfterWriterCommit();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await gate.BeforeAttemptAsync(timeout.Token);
        var retry = gate.BeforeAttemptAsync(timeout.Token);
        Assert.False(retry.IsCompleted);
        gate.WriterCommitted();
        await retry;
        await gate.BeforeAttemptAsync(timeout.Token);
    }

    [Fact]
    public async Task CancellationReleasesWaitingRetry_WithoutPretendingWriterCommitted()
    {
        var gate = new LaboratoryRequestSqlTests.RetryAfterWriterCommit();
        using var cancellation = new CancellationTokenSource();
        await gate.BeforeAttemptAsync(cancellation.Token);
        var retry = gate.BeforeAttemptAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var next = gate.BeforeAttemptAsync(timeout.Token);
        Assert.False(next.IsCompleted);
        gate.WriterCommitted();
        await next;
    }

    [Fact]
    public async Task AlreadyCommittedSignal_IsNotLostAndDoesNotAffectAnotherGate()
    {
        var first = new LaboratoryRequestSqlTests.RetryAfterWriterCommit();
        var other = new LaboratoryRequestSqlTests.RetryAfterWriterCommit();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        first.WriterCommitted();
        first.WriterCommitted();
        await first.BeforeAttemptAsync(timeout.Token);
        await first.BeforeAttemptAsync(timeout.Token);
        await other.BeforeAttemptAsync(timeout.Token);
        var waiting = other.BeforeAttemptAsync(timeout.Token);
        Assert.False(waiting.IsCompleted);
        other.WriterCommitted();
        await waiting;
    }
}
