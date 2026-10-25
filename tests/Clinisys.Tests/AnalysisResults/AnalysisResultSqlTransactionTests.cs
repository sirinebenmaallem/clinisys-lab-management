using System.Data;
using System.Data.Common;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain.Entities;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.AnalysisResults;

[Collection(AnalysisResultSqlCollection.Name)]
public sealed class AnalysisResultSqlTransactionTests
{
    [SqlServerFact]
    public async Task FailureAfterActualSave_RollsBackOwnedCreateAndCorrection()
    {
        foreach (var update in new[] { false, true })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var seed = SqlServerTestDatabase.CreateContext();
            var original = update ? await AnalysisResultSqlFixture.Store(seed).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default) : null;
            var failure = new FailAfterSave();
            await using var source = SqlServerTestDatabase.CreateContext(failure);
            var store = AnalysisResultSqlFixture.Store(source);
            await Assert.ThrowsAsync<SyntheticSaveFailure>(() => update
                ? store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 9, Convert.FromBase64String(original!.Version), default)
                : store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 9, default));
            Assert.Equal(1, failure.Saves);
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Equal(original, await AnalysisResultSqlFixture.Store(verify).GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        }
    }

    [SqlServerFact]
    public async Task CallerTransaction_IsNotCommittedAndRollbackUndoesSuccessfulResult()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        var probe = new TransactionProbe();
        await using var caller = SqlServerTestDatabase.CreateContext(probe);
        await using var transaction = await caller.Database.BeginTransactionAsync();
        var trackedPatient = await caller.Patients.SingleAsync(x => x.Id == fixture.Patient.Id);
        var trackedParent = await caller.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id);
        var created = await AnalysisResultSqlFixture.Store(caller).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default);
        Assert.Same(transaction, caller.Database.CurrentTransaction);
        Assert.Equal(0, probe.Commits);
        Assert.True(probe.Savepoints > 0);
        Assert.Equal(created.Id, (await caller.AnalysisResults.AsNoTracking().SingleAsync(x => x.RequestItemId == fixture.Item.Id)).Id);
        Assert.Equal(EntityState.Unchanged, caller.Entry(trackedPatient).State);
        Assert.Equal(EntityState.Unchanged, caller.Entry(trackedParent).State);
        Assert.DoesNotContain(caller.ChangeTracker.Entries(), x => x.Entity is AnalysisResult);
        await transaction.RollbackAsync();
        await using var verify = SqlServerTestDatabase.CreateContext();
        Assert.False(await verify.AnalysisResults.AnyAsync(x => x.RequestItemId == fixture.Item.Id));
    }

    [SqlServerFact]
    public async Task CallerSavepoint_RestoresFailedOperationButPreservesEarlierCallerWorkAndTrackedEntities()
    {
        foreach (var update in new[] { false, true })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var seed = SqlServerTestDatabase.CreateContext();
            var original = update ? await AnalysisResultSqlFixture.Store(seed).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default) : null;
            var probe = new TransactionProbe();
            var failure = new FailAfterSave();
            await using var caller = SqlServerTestDatabase.CreateContext(probe, failure);
            await using var transaction = await caller.Database.BeginTransactionAsync();
            var patient = await caller.Patients.SingleAsync(x => x.Id == fixture.Patient.Id);
            patient.Update("SYN-BEFORE-SAVEPOINT", patient.LastName, patient.DateOfBirth, patient.Sex, phoneNumber: "TEST-ONLY");
            await caller.SaveChangesAsync(); // unrelated caller work before the store savepoint
            AnalysisResult? trackedResult = update ? await caller.AnalysisResults.SingleAsync(x => x.Id == original!.Id) : null;
            var store = AnalysisResultSqlFixture.Store(caller);
            await Assert.ThrowsAsync<SyntheticSaveFailure>(() => update
                ? store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 9, Convert.FromBase64String(original!.Version), default)
                : store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 9, default));
            Assert.Equal(1, failure.Saves);
            Assert.True(probe.SavepointRollbacks > 0);
            Assert.Equal(0, probe.Commits);
            Assert.Same(transaction, caller.Database.CurrentTransaction);
            Assert.Equal(EntityState.Unchanged, caller.Entry(patient).State);
            if (trackedResult is not null)
            {
                Assert.Equal(EntityState.Unchanged, caller.Entry(trackedResult).State);
                Assert.Equal(1, trackedResult.Value);
            }
            else Assert.DoesNotContain(caller.ChangeTracker.Entries(), x => x.Entity is AnalysisResult);
            await transaction.CommitAsync(); // proves the failed result is not stranded in the caller transaction
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Equal("SYN-BEFORE-SAVEPOINT", (await verify.Patients.AsNoTracking().SingleAsync(x => x.Id == fixture.Patient.Id)).FirstName);
            Assert.Equal(original, await AnalysisResultSqlFixture.Store(verify).GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        }
    }

    [SqlServerFact]
    public async Task CallerIsolationAndPendingChanges_AreRejectedWithoutLosingCallerState()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using (var caller = SqlServerTestDatabase.CreateContext())
        {
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultSqlFixture.Store(caller)
                .CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default));
            Assert.Same(transaction, caller.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        }
        await using (var caller = SqlServerTestDatabase.CreateContext())
        {
            await using var transaction = await caller.Database.BeginTransactionAsync();
            var patient = await caller.Patients.SingleAsync(x => x.Id == fixture.Patient.Id);
            patient.Update("SYN-PENDING", patient.LastName, patient.DateOfBirth, patient.Sex, phoneNumber: "TEST-ONLY");
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultSqlFixture.Store(caller)
                .CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default));
            Assert.Equal(EntityState.Modified, caller.Entry(patient).State);
            Assert.Equal("SYN-PENDING", patient.FirstName);
            await transaction.RollbackAsync();
        }
        await using var verify = SqlServerTestDatabase.CreateContext();
        Assert.False(await verify.AnalysisResults.AnyAsync(x => x.RequestItemId == fixture.Item.Id));
        Assert.Equal(fixture.Patient.FirstName, (await verify.Patients.AsNoTracking().SingleAsync(x => x.Id == fixture.Patient.Id)).FirstName);
    }

    [SqlServerFact]
    public async Task CommitFailureBeforeOrAfterActualCommit_IsUnknownAndNeverReplayed()
    {
        foreach (var afterCommit in new[] { false, true })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            var acknowledgement = new LoseCommitAcknowledgement(afterCommit);
            await using var source = SqlServerTestDatabase.CreateContext(acknowledgement);
            await AnalysisResultSqlTests.Error(() => AnalysisResultSqlFixture.Store(source)
                .CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default), AnalysisResultError.CommitUnknown);
            Assert.Equal(1, acknowledgement.Attempts);
            Assert.Equal(afterCommit ? 1 : 0, acknowledgement.Commits);
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Equal(afterCommit ? 1 : 0, await verify.AnalysisResults.CountAsync(x => x.RequestItemId == fixture.Item.Id));
        }
    }

    private sealed class SyntheticSaveFailure : Exception { }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AnalysisResult>().Any())
            {
                Saves++;
                throw new SyntheticSaveFailure(); // AFTER real INSERT/UPDATE, still inside the store transaction
            }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class TransactionProbe : DbTransactionInterceptor
    {
        public int Commits, Savepoints, SavepointRollbacks;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Commits++; return Task.CompletedTask; }
        public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
        { Savepoints++; return Task.CompletedTask; }
        public override Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
        { SavepointRollbacks++; return Task.CompletedTask; }
    }

    private sealed class SyntheticCommitFailure : DbException { }
    private sealed class LoseCommitAcknowledgement(bool afterCommit) : DbTransactionInterceptor
    {
        public int Attempts, Commits;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (!afterCommit) throw new SyntheticCommitFailure();
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Commits++; throw new SyntheticCommitFailure(); }
    }
}
