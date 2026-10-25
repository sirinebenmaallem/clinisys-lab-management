using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.AnalysisResults;

// Synthetic diagnostic events only: no SQL provider or connection is instantiated.
public sealed class SqlDeadlockObserverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Native1205_IsObservedAcrossBothPathsAndDeduplicated(bool wrapped, bool commandFirst)
    {
        using var observer = new SqlDeadlockObserver();
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        var sql = SqlFailure(1205);
        Exception error = wrapped ? new InvalidOperationException("Synthetic EF", sql) : sql;
        await observer.ObserveAsync(async () =>
        {
            await Task.Yield();
            if (commandFirst) observer.Observe(error);
            Publish(listener, error);
            observer.Observe(error); // CommandFailedAsync feeds this same method.
            Publish(listener, error);
            Assert.Equal(1, observer.Count);
            observer.Observe(SqlFailure(1205)); // Distinct incidents remain visible.
            return true;
        });
        Assert.Equal(2, observer.Count);
    }

    [Fact]
    public async Task OtherFailures_AreNotProofOfDeadlock()
    {
        using var observer = new SqlDeadlockObserver();
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        await observer.ObserveAsync(() =>
        {
            foreach (var error in new Exception[] { SqlFailure(1222), SqlFailure(-2), SqlFailure(547),
                SqlFailure(2601), new TimeoutException(), new OperationCanceledException(),
                Clinisys.Application.Features.AnalysisResults.AnalysisResultException.Conflict() })
            {
                observer.Observe(error);
                Publish(listener, error);
            }
            return Task.FromResult(true);
        });
        Assert.Equal(0, observer.Count);
    }

    [Fact]
    public async Task ConcurrentOperations_AreIsolatedAcrossAwaitAndTaskRun()
    {
        using var observer = new SqlDeadlockObserver();
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = observer.ObserveAsync(async () =>
        {
            ready.SetResult();
            await release.Task;
            await Task.Run(() => Publish(listener, SqlFailure(1205)));
            return true;
        });
        try
        {
            await ready.Task;
            await Task.Run(() =>
            {
                // Represents the concurrent writer, outside the store's logical scope.
                Publish(listener, SqlFailure(1205));
                observer.Observe(SqlFailure(1205));
            });
            Assert.Equal(0, observer.Count);
        }
        finally { release.TrySetResult(); await observed; }
        Assert.Equal(1, observer.Count);
        Publish(listener, SqlFailure(1205));
        Assert.Equal(1, observer.Count);
    }

    [Fact]
    public async Task NestedScopes_RestoreOuterScopeAndPreserveOriginalException()
    {
        using var observer = new SqlDeadlockObserver();
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        var original = SqlFailure(1205);
        await observer.ObserveAsync(async () =>
        {
            var received = await Assert.ThrowsAsync<SqlException>(() => observer.ObserveAsync<bool>(async () =>
            {
                await Task.Yield();
                Publish(listener, original);
                throw original;
            }));
            Assert.Same(original, received);
            Publish(listener, SqlFailure(1205)); // Outer scope restored after failure.
            return true;
        });
        Publish(listener, SqlFailure(1205));
        observer.Observe(SqlFailure(1205));
        Assert.Equal(2, observer.Count);
    }

    [Fact]
    public async Task Cancellation_RestoresScopeAndPropagatesOriginalException()
    {
        using var observer = new SqlDeadlockObserver();
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var original = new OperationCanceledException(cancellation.Token);
        var received = await Assert.ThrowsAsync<OperationCanceledException>(() => observer.ObserveAsync<bool>(async () =>
        {
            await Task.Yield();
            Publish(listener, original);
            throw original;
        }));
        Assert.Same(original, received);
        Publish(listener, SqlFailure(1205));
        observer.Observe(SqlFailure(1205));
        Assert.Equal(0, observer.Count);
    }

    [Fact]
    public async Task ExistingListener_DisposalAndNewObserver_DoNotLeakScope()
    {
        using var listener = new DiagnosticListener("Microsoft.EntityFrameworkCore");
        using var first = new SqlDeadlockObserver();
        using var next = new SqlDeadlockObserver();
        await first.ObserveAsync(() =>
        {
            listener.Write("OtherEvent", Payload(SqlFailure(1205)));
            Publish(listener, SqlFailure(1205));
            Assert.Equal(1, first.Count);
            Assert.Equal(0, next.Count);
            first.Dispose();
            Publish(listener, SqlFailure(1205));
            return Task.FromResult(true);
        });
        await next.ObserveAsync(() =>
        {
            Publish(listener, SqlFailure(1205));
            return Task.FromResult(true);
        });
        Assert.Equal(1, first.Count);
        Assert.Equal(1, next.Count);
        Publish(listener, SqlFailure(1205));
        Assert.Equal(1, next.Count);
    }

    private static void Publish(DiagnosticListener listener, Exception error) =>
        listener.Write(CoreEventId.QueryIterationFailed.Name!, Payload(error));

    // Exact EF Core 10.0.13 QueryIterationFailed payload: context TYPE, not instance.
    private static DbContextTypeErrorEventData Payload(Exception error) =>
        new(null!, (_, _) => "Synthetic iteration failure", typeof(DbContext), error);

    // Existing test convention; constructs native exception objects without SQL access.
    private static SqlException SqlFailure(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(x => x.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(p => p.ParameterType.IsValueType
            ? Activator.CreateInstance(p.ParameterType) : p.ParameterType == typeof(string) ? "Synthetic" : null).ToArray();
        arguments[0] = number;
        var error = (SqlError)constructor.Invoke(arguments);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static,
            binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!.Invoke(null, [errors, "synthetic"])!;
    }
}
