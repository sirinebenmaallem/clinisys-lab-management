using System.Collections.Concurrent;
using System.Data.Common;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.AnalysisResults;

[Collection(AnalysisResultSqlCollection.Name)]
public sealed class AnalysisResultSqlConcurrencyTests
{
    [SqlServerFact]
    public Task TwoCreates_IndependentConnections_AcceptExactlyOneResult() => Race(update: false);

    [SqlServerFact]
    public Task TwoCorrections_WithSameInitialVersion_AcceptOnlyWinnerValues() => Race(update: true);

    private static async Task Race(bool update)
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var seed = SqlServerTestDatabase.CreateContext();
        var initial = update ? await AnalysisResultSqlFixture.Store(seed).CreateAsync(fixture.Request.Id, fixture.Item.Id, 0, default) : null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var gate = new RaceGate();
        await using var first = SqlServerTestDatabase.CreateContext(gate);
        await using var second = SqlServerTestDatabase.CreateContext(gate);
        Task<Outcome[]>? operations = null;
        try
        {
            // Both clients hold the SAME initial token before either write starts.
            var token = initial is null ? null : Convert.FromBase64String(initial.Version);
            operations = Task.WhenAll(
                Attempt(() => update
                    ? AnalysisResultSqlFixture.Store(first, "SYN-FIRST").UpdateAsync(fixture.Request.Id, fixture.Item.Id, 11, token!.ToArray(), timeout.Token)
                    : AnalysisResultSqlFixture.Store(first, "SYN-FIRST").CreateAsync(fixture.Request.Id, fixture.Item.Id, 11, timeout.Token)),
                Attempt(() => update
                    ? AnalysisResultSqlFixture.Store(second, "SYN-SECOND").UpdateAsync(fixture.Request.Id, fixture.Item.Id, 22, token!.ToArray(), timeout.Token)
                    : AnalysisResultSqlFixture.Store(second, "SYN-SECOND").CreateAsync(fixture.Request.Id, fixture.Item.Id, 22, timeout.Token)));
            var outcomes = await operations.WaitAsync(timeout.Token);
            var winner = Assert.Single(outcomes, x => x.Result is not null).Result!;
            var loser = Assert.Single(outcomes, x => x.Error is not null).Error;
            Assert.Equal(AnalysisResultError.Conflict, Assert.IsType<AnalysisResultException>(loser).Error);
            Assert.Equal(2, gate.Attempts);
            Assert.Equal(2, gate.Sessions.Distinct().Count());
            Assert.Equal(2, gate.Contexts.Distinct().Count());
            Assert.Equal(winner.Value == 11 ? "SYN-FIRST" : "SYN-SECOND", winner.EnteredBy);
            Assert.Contains(winner.Value, new[] { 11m, 22m });
            await using var verify = SqlServerTestDatabase.CreateContext();
            var stored = await verify.AnalysisResults.AsNoTracking().SingleAsync(x => x.RequestItemId == fixture.Item.Id, timeout.Token);
            Assert.Equal(winner.Value, stored.Value);
            Assert.Equal(winner.EnteredBy, stored.EnteredBy);
            Assert.Equal(Convert.FromBase64String(winner.Version), stored.Version);
            if (initial is not null) Assert.NotEqual(initial.Version, winner.Version);
            else Assert.Equal(8, stored.Version.Length);
        }
        finally
        {
            timeout.Cancel();
            if (operations is not null) await operations; // Attempt captures failures; drain before fixture cleanup.
        }
    }

    [SqlServerFact]
    public Task RealDeadlock_OwnedTransaction_ConflictsWithoutReplay() => Deadlock(borrowed: false);

    [SqlServerFact]
    public Task RealDeadlock_CallerTransaction_DoesNotRollbackToSavepointOrReplay() => Deadlock(borrowed: true);

    private static async Task Deadlock(bool borrowed)
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var seen = new ParentSeen();
        var probe = new AnalysisResultSqlTransactionTests.TransactionProbe();
        await using var writer = SqlServerTestDatabase.CreateContext();
        await writer.Database.OpenConnectionAsync(timeout.Token);
        await writer.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH", timeout.Token);
        await using var transaction = await writer.Database.BeginTransactionAsync(timeout.Token);
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=16 WHERE Id={fixture.Analysis.Id}", timeout.Token);
        await using var source = SqlServerTestDatabase.CreateContext(seen, probe);
        await using var caller = borrowed ? await source.Database.BeginTransactionAsync(timeout.Token) : null;
        Task<AnalysisResultDetailDto>? creation = null;
        try
        {
            creation = seen.ObserveAsync(() => AnalysisResultSqlFixture.Store(source)
                .CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, timeout.Token));
            await seen.Ready.Task.WaitAsync(timeout.Token);
            // External writer reverses the production order: analysis X -> parent X.
            // Store owns parent U and needs analysis U, forcing a real SQL 1205 victim.
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET PrescribingPhysician=N'Synthetic changed' WHERE Id={fixture.Request.Id}", timeout.Token);
            await transaction.CommitAsync(timeout.Token);
            await AnalysisResultSqlTests.Error(() => creation.WaitAsync(timeout.Token), AnalysisResultError.Conflict);
            Assert.Equal(1, seen.Count);
            Assert.Equal(1, seen.NativeDeadlocks);
            Assert.Equal(0, probe.SavepointRollbacks);
            Assert.Equal(0, probe.Commits);
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.False(await verify.AnalysisResults.AnyAsync(x => x.RequestItemId == fixture.Item.Id, timeout.Token));
        }
        finally
        {
            // Drain inside the resource lifetime; never abandon active SQL for cleanup.
            timeout.Cancel();
            if (creation is not null) { try { await creation; } catch { } }
        }
    }

    [SqlServerFact]
    public async Task ParentLock_IsFirstAndBlocksCompetingWorkflowWriterUntilCommit()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pause = new LockOrderPause("parent");
        await using var source = SqlServerTestDatabase.CreateContext(pause);
        Task<AnalysisResultDetailDto>? creation = null;
        try
        {
            creation = AnalysisResultSqlFixture.Store(source).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, timeout.Token);
            await pause.Ready.Task.WaitAsync(timeout.Token);
            Assert.Equal(new[] { "parent" }, pause.Order);
            await using var writer = SqlServerTestDatabase.CreateContext();
            await writer.Database.OpenConnectionAsync(timeout.Token);
            await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0", timeout.Token);
            Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE LaboratoryRequests SET Status=1 WHERE Id={fixture.Request.Id}", timeout.Token))).Number);
            pause.Release();
            await creation.WaitAsync(timeout.Token);
            Assert.Equal(new[] { "parent", "item", "result", "analysis", "ranges" }, pause.Order);
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET Status=1 WHERE Id={fixture.Request.Id}", timeout.Token);
        }
        finally
        {
            pause.Release();
            timeout.Cancel();
            if (creation is not null) { try { await creation; } catch { } }
        }
    }

    [SqlServerFact]
    public async Task CatalogueLocks_KeepUnitAndReferenceStableUntilResultCommit()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        var range = await fixture.AddRangeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pause = new LockOrderPause("ranges");
        await using var source = SqlServerTestDatabase.CreateContext(pause);
        Task<AnalysisResultDetailDto>? creation = null;
        try
        {
            creation = AnalysisResultSqlFixture.Store(source).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, timeout.Token);
            await pause.Ready.Task.WaitAsync(timeout.Token);
            await using var writer = SqlServerTestDatabase.CreateContext();
            await writer.Database.OpenConnectionAsync(timeout.Token);
            await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0", timeout.Token);
            Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE MedicalAnalyses SET Unit=N'changed' WHERE Id={fixture.Analysis.Id}", timeout.Token))).Number);
            Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReferenceRanges SET LowerBound=100, UpperBound=200 WHERE Id={range.Id}", timeout.Token))).Number);
            pause.Release();
            var result = await creation.WaitAsync(timeout.Token);
            Assert.Equal(-2, result.ReferenceLowerBoundSnapshot);
            Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        }
        finally
        {
            pause.Release();
            timeout.Cancel();
            if (creation is not null) { try { await creation; } catch { } }
        }
    }

    private sealed record Outcome(AnalysisResultDetailDto? Result, Exception? Error);
    private static async Task<Outcome> Attempt(Func<Task<AnalysisResultDetailDto>> write)
    { try { return new(await write(), null); } catch (Exception error) { return new(null, error); } }

    private static string? Step(DbCommand command)
    {
        foreach (var (table, name) in new[] { ("LaboratoryRequests", "parent"), ("RequestItems", "item"),
            ("AnalysisResults", "result"), ("MedicalAnalyses", "analysis"), ("ReferenceRanges", "ranges") })
            if (command.CommandText.Contains("dbo." + table + " WITH (", StringComparison.Ordinal)) return name;
        return null;
    }

    private sealed class RaceGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource secondAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int attempts, locked;
        public int Attempts => attempts;
        public ConcurrentBag<int> Sessions { get; } = [];
        public ConcurrentBag<Guid> Contexts { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Step(command) == "parent")
            {
                Assert.Equal("ClinisysTestDb", command.Connection!.Database);
                Sessions.Add(((SqlConnection)command.Connection).ServerProcessId);
                Contexts.Add(eventData.Context!.ContextId.InstanceId);
                if (Interlocked.Increment(ref attempts) == 2) secondAttempt.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Step(command) == "parent" && Interlocked.Increment(ref locked) == 1)
                await secondAttempt.Task.WaitAsync(cancellationToken); // first transaction holds parent while competitor attempts it
            return result;
        }
    }

    private sealed class ParentSeen : DbCommandInterceptor, IDisposable
    {
        private readonly SqlDeadlockObserver deadlocks = new();
        public int Count;
        public int NativeDeadlocks => deadlocks.Count;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> ObserveAsync<T>(Func<Task<T>> action) => deadlocks.ObserveAsync(action);
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Step(command) == "parent") { Count++; Ready.TrySetResult(); }
            return ValueTask.FromResult(result);
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            deadlocks.Observe(eventData.Exception);
            return Task.CompletedTask;
        }
        public void Dispose() => deadlocks.Dispose();
    }

    private sealed class LockOrderPause(string step) : DbCommandInterceptor
    {
        public List<string> Order { get; } = [];
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var current = Step(command);
            if (current is not null) Order.Add(current);
            if (current == step)
            {
                Ready.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
