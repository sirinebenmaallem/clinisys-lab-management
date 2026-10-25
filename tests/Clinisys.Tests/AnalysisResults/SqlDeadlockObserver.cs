using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.AnalysisResults;

// Test-only observation: never logs SQL, changes exceptions or wraps a reader.
internal sealed class SqlDeadlockObserver : IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly object sync = new();
    private readonly AsyncLocal<object?> operation = new();
    private readonly HashSet<SqlException> deadlocks = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> subscriptions = [];
    private readonly IDisposable allListeners;
    private bool disposed;

    internal SqlDeadlockObserver() => allListeners = DiagnosticListener.AllListeners.Subscribe(this);

    internal int Count { get { lock (sync) return deadlocks.Count; } }

    internal async Task<T> ObserveAsync<T>(Func<Task<T>> action)
    {
        var previous = operation.Value;
        operation.Value = new object();
        try { return await action().ConfigureAwait(false); }
        finally { operation.Value = previous; }
    }

    internal void Observe(Exception exception)
    {
        if (operation.Value is null) return;
        // EF may wrap iteration failures. Count only a real native SQL 1205.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 } sql)
            {
                lock (sync) { if (!disposed) deadlocks.Add(sql); }
                return;
            }
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name != "Microsoft.EntityFrameworkCore") return;
        lock (sync)
        {
            if (!disposed)
                subscriptions.Add(listener.Subscribe(this, name => name == CoreEventId.QueryIterationFailed.Name));
        }
    }

    public void OnNext(KeyValuePair<string, object?> notification)
    {
        if (notification.Key != CoreEventId.QueryIterationFailed.Name
            || notification.Value is not DbContextTypeErrorEventData error) return;
        Observe(error.Exception);
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }

    public void Dispose()
    {
        IDisposable[] active;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            active = subscriptions.ToArray();
        }
        allListeners.Dispose();
        foreach (var subscription in active) subscription.Dispose();
    }
}
