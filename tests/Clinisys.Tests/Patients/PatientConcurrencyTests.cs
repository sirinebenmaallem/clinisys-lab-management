using System.Collections.Concurrent;
using Clinisys.Application.Features.Patients;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Patients;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.Patients;

public sealed class PatientConcurrencyTests
{
    [SqlServerFact]
    public async Task ConcurrentUpdates_FromSameVersion_OnlyOneSucceedsWithoutMixedFields()
    {
        var patient = Draft("Original", Identity());
        var firstDraft = new Patient("Synthetic first", "First revision", new(2001, 2, 3),
            Sex.Female, Identity(), "SYN-FIRST-PHONE");
        var secondDraft = new Patient("Synthetic second", "Second revision", new(2002, 3, 4),
            Sex.Male, Identity(), "SYN-SECOND-PHONE");
        // A deadline only fails a stuck test; it never determines the ordering of writers.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        try
        {
            // A committed synthetic row is necessary for visibility across independent connections.
            await using (var seed = SqlServerTestDatabase.CreateContext())
            {
                seed.Patients.Add(patient);
                await seed.SaveChangesAsync(ct);
            }
            var initialVersion = patient.Version.ToArray();
            Assert.Equal(8, initialVersion.Length);
            var gate = new BeforeSaveBarrier(initialVersion);
            await using var first = SqlServerTestDatabase.CreateContext(gate);
            await using var second = SqlServerTestDatabase.CreateContext(gate);
            await AssertIndependentSessions(first, second, ct);

            var outcomes = await Task.WhenAll(
                Attempt(() => new PatientStore(first).UpdateAsync(patient.Id, firstDraft, initialVersion.ToArray(), ct), gate),
                Attempt(() => new PatientStore(second).UpdateAsync(patient.Id, secondDraft, initialVersion.ToArray(), ct), gate));

            Assert.Equal(2, gate.ArrivalCount);
            Assert.Equal(2, gate.OriginalVersions.Count);
            Assert.All(gate.OriginalVersions, version => Assert.Equal(initialVersion, version));
            var winnerIndex = Array.FindIndex(outcomes, x => x.Result is not null);
            var winner = Assert.Single(outcomes, x => x.Result is not null).Result!;
            var loser = Assert.Single(outcomes, x => x.Error is not null).Error!;
            Assert.Equal(PatientError.Conflict, loser.Error);
            // Proves the conflict came from the SQL UPDATE concurrency predicate, not the initial version comparison.
            Assert.Equal(1, gate.ConcurrencyFailures);

            await using var verify = SqlServerTestDatabase.CreateContext();
            var stored = await verify.Patients.AsNoTracking().SingleAsync(x => x.Id == patient.Id, ct);
            AssertFields(winnerIndex == 0 ? firstDraft : secondDraft, stored);
            Assert.Equal(patient.Id, stored.Id);
            Assert.False(initialVersion.SequenceEqual(stored.Version));
            Assert.Equal(winner.Version, Convert.ToBase64String(stored.Version));
        }
        finally
        {
            // Only this test's synthetic patient is removed; no request/history rows are created.
            await using var cleanup = SqlServerTestDatabase.CreateContext();
            await cleanup.Patients.Where(x => x.Id == patient.Id).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task ConcurrentCreates_WithSameNormalizedIdentity_OnlyOnePatientIsStored()
    {
        var identity = Identity();
        var firstDraft = Draft("Synthetic first", " " + identity.ToLowerInvariant() + " ");
        var secondDraft = Draft("Synthetic second", identity);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        try
        {
            var gate = new BeforeSaveBarrier();
            await using var first = SqlServerTestDatabase.CreateContext(gate);
            await using var second = SqlServerTestDatabase.CreateContext(gate);
            await AssertIndependentSessions(first, second, ct);
            var outcomes = await Task.WhenAll(
                Attempt(() => new PatientStore(first).CreateAsync(firstDraft, ct), gate),
                Attempt(() => new PatientStore(second).CreateAsync(secondDraft, ct), gate));

            Assert.Equal(2, gate.ArrivalCount);
            var winnerIndex = Array.FindIndex(outcomes, x => x.Result is not null);
            var winner = Assert.Single(outcomes, x => x.Result is not null).Result!;
            Assert.Equal(PatientError.Conflict, Assert.Single(outcomes, x => x.Error is not null).Error!.Error);
            var sqlFailure = Assert.Single(gate.SaveFailures.OfType<DbUpdateException>());
            var sql = Assert.IsType<SqlException>(sqlFailure.InnerException);
            Assert.Contains(sql.Number, new[] { 2601, 2627 });

            await using var verify = SqlServerTestDatabase.CreateContext();
            var stored = Assert.Single(await verify.Patients.AsNoTracking()
                .Where(x => x.NationalIdentityNumber == identity).ToListAsync(ct));
            Assert.Equal(winner.Id, stored.Id);
            AssertFields(winnerIndex == 0 ? firstDraft : secondDraft, stored);
            Assert.Equal(identity, stored.NationalIdentityNumber);
            Assert.Equal(8, stored.Version.Length);
            Assert.Equal(winner.Version, Convert.ToBase64String(stored.Version));
        }
        finally
        {
            await using var cleanup = SqlServerTestDatabase.CreateContext();
            await cleanup.Patients.Where(x => (x.Id == firstDraft.Id || x.Id == secondDraft.Id)
                && x.NationalIdentityNumber == identity).ExecuteDeleteAsync();
        }
    }

    private static string Identity() => $"SYN-{Guid.NewGuid():N}".ToUpperInvariant();
    private static Patient Draft(string firstName, string identity) =>
        new(firstName, "Synthetic concurrency", new(2000, 1, 1), Sex.Unspecified, identity, "SYN-TEST-PHONE");

    private static void AssertFields(Patient expected, Patient actual)
    {
        Assert.Equal(expected.FirstName, actual.FirstName);
        Assert.Equal(expected.LastName, actual.LastName);
        Assert.Equal(expected.DateOfBirth, actual.DateOfBirth);
        Assert.Equal(expected.Sex, actual.Sex);
        Assert.Equal(expected.NationalIdentityNumber, actual.NationalIdentityNumber);
        Assert.Equal(expected.PhoneNumber, actual.PhoneNumber);
    }

    private static async Task AssertIndependentSessions(ClinisysDbContext first, ClinisysDbContext second, CancellationToken ct)
    {
        await first.Database.OpenConnectionAsync(ct);
        await second.Database.OpenConnectionAsync(ct);
        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        var firstSpid = await first.Database.SqlQueryRaw<short>("SELECT @@SPID AS [Value]").SingleAsync(ct);
        var secondSpid = await second.Database.SqlQueryRaw<short>("SELECT @@SPID AS [Value]").SingleAsync(ct);
        Assert.NotEqual(firstSpid, secondSpid);
    }

    private sealed record Outcome(PatientDetailDto? Result, PatientException? Error);

    private static async Task<Outcome> Attempt(Func<Task<PatientDetailDto>> write, BeforeSaveBarrier gate)
    {
        try { return new(await write(), null); }
        catch (PatientException ex) when (ex.Error == PatientError.Conflict)
        {
            // Also releases a peer if a regression rejects this writer before reaching SaveChanges.
            gate.AbortIfIncomplete(ex);
            return new(null, ex);
        }
        catch (Exception ex)
        {
            gate.AbortIfIncomplete(ex);
            throw;
        }
    }

    // Test-only synchronization: both stores must finish loading/validating before either sends SQL writes.
    private sealed class BeforeSaveBarrier(byte[]? expectedVersion = null) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private int _concurrencyFailures;
        public int ArrivalCount => Volatile.Read(ref _arrivals);
        public int ConcurrencyFailures => Volatile.Read(ref _concurrencyFailures);
        public ConcurrentBag<byte[]> OriginalVersions { get; } = [];
        public ConcurrentBag<Exception> SaveFailures { get; } = [];

        public void AbortIfIncomplete(Exception error)
        {
            if (ArrivalCount < 2) _bothReady.TrySetException(error);
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (expectedVersion is not null)
            {
                var entry = Assert.Single(eventData.Context!.ChangeTracker.Entries<Patient>());
                var version = entry.Property(x => x.Version).OriginalValue.ToArray();
                Assert.Equal(expectedVersion, version);
                OriginalVersions.Add(version);
            }
            if (Interlocked.Increment(ref _arrivals) == 2) _bothReady.TrySetResult();
            await _bothReady.Task.WaitAsync(cancellationToken);
            return result;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _concurrencyFailures);
            return ValueTask.FromResult(result); // Observe, never suppress the real EF concurrency exception.
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            SaveFailures.Add(eventData.Exception);
            return Task.CompletedTask;
        }
    }
}
