using System.Data.Common;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.AnalysisResults;

// WRITE ONLY in E1: these opt-in tests must not run before explicit E2 approval.
[Collection(AnalysisResultSqlCollection.Name)]
public sealed class AnalysisResultSqlTests
{
    [SqlServerFact]
    public async Task FixtureCleanup_PreservesSyntheticRowsThatPredateTheFixtureBeingCleaned()
    {
        await using var sentinel = await AnalysisResultSqlFixture.CreateAsync();
        var sentinelRange = await sentinel.AddRangeAsync();
        await using var source = SqlServerTestDatabase.CreateContext();
        var original = await AnalysisResultSqlFixture.Store(source).CreateAsync(sentinel.Request.Id, sentinel.Item.Id, 1, default);
        await using (var subject = await AnalysisResultSqlFixture.CreateAsync())
        {
            await subject.AddRangeAsync();
            await AnalysisResultSqlFixture.Store(source).CreateAsync(subject.Request.Id, subject.Item.Id, 2, default);
        }
        // The sentinel is synthetic, but pre-exists the subject's seed/cleanup.
        // No actual pre-existing patient data is read or used as a test fixture.
        await using var verify = SqlServerTestDatabase.CreateContext();
        Assert.Equal(original, await AnalysisResultSqlFixture.Store(verify).GetAsync(sentinel.Request.Id, sentinel.Item.Id, default));
        Assert.True(await verify.Patients.AnyAsync(x => x.Id == sentinel.Patient.Id));
        Assert.True(await verify.LaboratoryRequests.AnyAsync(x => x.Id == sentinel.OtherRequest.Id));
        Assert.True(await verify.RequestItems.AnyAsync(x => x.Id == sentinel.EmptyItem.Id));
        Assert.True(await verify.RequestItems.AnyAsync(x => x.Id == sentinel.ForeignItem.Id));
        Assert.True(await verify.MedicalAnalyses.AnyAsync(x => x.Id == sentinel.Analysis.Id));
        Assert.True(await verify.MedicalAnalyses.AnyAsync(x => x.Id == sentinel.OtherAnalysis.Id));
        Assert.Equal(-2, (await verify.ReferenceRanges.AsNoTracking().SingleAsync(x => x.Id == sentinelRange.Id)).LowerBound);
    }

    [SqlServerFact]
    public async Task Create_PersistsMetadataReferencesAndReadsEnteredAndUnenteredItems()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync();
        await using var source = SqlServerTestDatabase.CreateContext();
        var store = AnalysisResultSqlFixture.Store(source);
        var parentVersion = (await source.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id)).Version;
        var created = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 0.123456m, default);
        Assert.Equal(0.123456m, created.Value);
        Assert.Equal(-2, created.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, created.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.Normal, created.Abnormality);
        Assert.Equal(AnalysisResultSqlFixture.EntryTime, created.EnteredAt);
        Assert.Equal("SYN-ENTRY", created.EnteredBy);
        Assert.Null(created.ValidatedAt);
        Assert.Null(created.ValidatedBy);
        Assert.Equal(8, Convert.FromBase64String(created.Version).Length);
        await using var verify = SqlServerTestDatabase.CreateContext();
        var persisted = await verify.AnalysisResults.AsNoTracking().SingleAsync(x => x.RequestItemId == fixture.Item.Id);
        Assert.Equal(created.Value, persisted.Value);
        Assert.Equal(Convert.FromBase64String(created.Version), persisted.Version);
        var reader = AnalysisResultSqlFixture.Store(verify);
        Assert.Equal(created, await reader.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        Assert.Null(await reader.GetAsync(fixture.Request.Id, fixture.EmptyItem.Id, default));
        await store.CreateAsync(fixture.OtherRequest.Id, fixture.ForeignItem.Id, 1, default);
        Assert.Null(await reader.GetAsync(fixture.Request.Id, fixture.ForeignItem.Id, default));
        var list = await reader.ListForRequestAsync(fixture.Request.Id, default);
        Assert.Equal(2, list!.Items.Count);
        Assert.Equal(created, Assert.Single(list.Items, x => x.RequestItemId == fixture.Item.Id).Result);
        Assert.Null(Assert.Single(list.Items, x => x.RequestItemId == fixture.EmptyItem.Id).Result);
        Assert.Equal(list.Items.OrderBy(x => x.AnalysisCodeSnapshot).ThenBy(x => x.RequestItemId).Select(x => x.RequestItemId),
            list.Items.Select(x => x.RequestItemId));
        Assert.Null(await reader.ListForRequestAsync(Guid.NewGuid(), default));
        var parent = await verify.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id);
        Assert.Equal(RequestStatus.Registered, parent.Status);
        Assert.Equal(parentVersion, parent.Version);
    }

    [SqlServerFact]
    public async Task Selection_UsesStoredAgeAndSexRatherThanCorrectedPatient()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync(Sex.Female, 0, 2, 10, 20); // age 2 excluded
        await fixture.AddRangeAsync(Sex.Female, 2, 3, -2, 2);
        await fixture.AddRangeAsync(Sex.Male, 0, null, 100, 200);
        await using var db = SqlServerTestDatabase.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Patients SET Sex=2, DateOfBirth='20000101' WHERE Id={fixture.Patient.Id}");
        var result = await AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, 2, default);
        Assert.Equal(-2, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.Normal, result.Abnormality);
    }

    [SqlServerFact]
    public async Task Selection_UsesUtcRegistrationDateAcrossLeapDayWithPersistedOffset()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync(Sex.Female, 1, 2, -2, 2);
        await fixture.AddRangeAsync(Sex.Female, 2, 3, 100, 200);
        await using var db = SqlServerTestDatabase.CreateContext();
        var offsetInstant = new DateTimeOffset(2024, 3, 1, 0, 30, 0, TimeSpan.FromHours(1));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET RegisteredAt={offsetInstant} WHERE Id={fixture.Request.Id}");
        var result = await AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, 0, default);
        Assert.Equal(-2, result.ReferenceLowerBoundSnapshot); // UTC February 29: age 1, not local March 1: age 2
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.Normal, result.Abnormality);
    }

    [SqlServerFact]
    public async Task UnspecifiedSex_HasNoFallback_AndCorrectionDoesNotSelectNewlyAddedRange()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync(Sex.Female);
        await fixture.AddRangeAsync(Sex.Male);
        await using var db = SqlServerTestDatabase.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET PatientSexSnapshot=0 WHERE Id={fixture.Request.Id}");
        var store = AnalysisResultSqlFixture.Store(db);
        var created = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 0, default);
        Assert.Equal(Abnormality.NotEvaluated, created.Abnormality);
        await fixture.AddRangeAsync(Sex.Unspecified);
        var corrected = await store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 99, Convert.FromBase64String(created.Version), default);
        Assert.Null(corrected.ReferenceLowerBoundSnapshot);
        Assert.Null(corrected.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.NotEvaluated, corrected.Abnormality);
    }

    [SqlServerFact]
    public async Task NoApplicableReference_AllowsNotEvaluatedDespiteChangedCatalogueUnit()
    {
        foreach (var withInapplicableRange in new[] { false, true })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Unit=N'other-unit' WHERE Id={fixture.Analysis.Id}");
            if (withInapplicableRange) await fixture.AddRangeAsync(Sex.Male, 0, null);
            var result = await AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, -1, default);
            Assert.Null(result.ReferenceLowerBoundSnapshot);
            Assert.Null(result.ReferenceUpperBoundSnapshot);
            Assert.Equal(Abnormality.NotEvaluated, result.Abnormality);
            Assert.Equal("unit", result.UnitSnapshot);
        }
    }

    [SqlServerFact]
    public async Task AmbiguousReferencesOrIncompatibleUnit_ConflictWithoutResult()
    {
        foreach (var ambiguous in new[] { true, false })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            if (!ambiguous)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Unit=N'other-unit' WHERE Id={fixture.Analysis.Id}");
            await fixture.AddRangeAsync(Sex.Female, 0, null);
            if (ambiguous) await fixture.AddRangeAsync(Sex.Female, 1, null); // valid SQL, overlapping catalogue corruption
            await Error(() => AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default), AnalysisResultError.Conflict);
            Assert.False(await db.AnalysisResults.AnyAsync(x => x.RequestItemId == fixture.Item.Id));
        }
    }

    [SqlServerFact]
    public async Task HistoricItem_RemainsEligibleAfterCatalogueDeactivation()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await fixture.AddRangeAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET IsActive=0 WHERE Id={fixture.Analysis.Id}");
        var result = await AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, 3, default);
        Assert.Equal(Abnormality.High, result.Abnormality);
    }

    [SqlServerFact]
    public async Task ForeignItemDuplicatePostAndMissingPut_AreRejectedWithoutUpsert()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        var store = AnalysisResultSqlFixture.Store(db);
        await Error(() => store.CreateAsync(fixture.Request.Id, fixture.ForeignItem.Id, 1, default), AnalysisResultError.NotFound);
        await Error(() => store.CreateAsync(Guid.NewGuid(), fixture.Item.Id, 1, default), AnalysisResultError.NotFound);
        await Error(() => store.UpdateAsync(fixture.Request.Id, fixture.EmptyItem.Id, 1, new byte[8], default), AnalysisResultError.NotFound);
        var first = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 0, default);
        await Error(() => store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 10, default), AnalysisResultError.Conflict);
        var duplicate = new AnalysisResult(fixture.Item.Id, 10, "SYN-DUPLICATE");
        db.AnalysisResults.Add(duplicate);
        var uniqueError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(uniqueError.InnerException).Number, new[] { 2601, 2627 });
        db.Entry(duplicate).State = EntityState.Detached;
        Assert.Equal(first, await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        Assert.Single(await db.AnalysisResults.Where(x => x.RequestItemId == fixture.Item.Id || x.RequestItemId == fixture.ForeignItem.Id || x.RequestItemId == fixture.EmptyItem.Id).ToListAsync());
    }

    [SqlServerFact]
    public async Task Corrections_PreserveBoundsRejectStaleTokensAndVersionEvenIdenticalValues()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        var range = await fixture.AddRangeAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        var first = await AnalysisResultSqlFixture.Store(db).CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ReferenceRanges SET LowerBound=100, UpperBound=200 WHERE Id={range.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Unit=N'changed', IsActive=0 WHERE Id={fixture.Analysis.Id}");
        var recorder = new ReadRecorder();
        await using var source = SqlServerTestDatabase.CreateContext(recorder);
        var store = AnalysisResultSqlFixture.Store(source, "SYN-CORRECTOR", AnalysisResultSqlFixture.EntryTime.AddMinutes(1));
        var updated = await store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 3, Convert.FromBase64String(first.Version), default);
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(-2, updated.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, updated.ReferenceUpperBoundSnapshot);
        Assert.Equal("unit", updated.UnitSnapshot);
        Assert.Equal(Abnormality.High, updated.Abnormality);
        Assert.Equal("SYN-CORRECTOR", updated.EnteredBy);
        Assert.Equal(AnalysisResultSqlFixture.EntryTime.AddMinutes(1), updated.EnteredAt);
        Assert.NotEqual(first.Version, updated.Version);
        await Error(() => store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 99, Convert.FromBase64String(first.Version), default), AnalysisResultError.Conflict);
        Assert.Equal(updated, await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        var unchanged = await store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 3, Convert.FromBase64String(updated.Version), default);
        Assert.NotEqual(updated.Version, unchanged.Version);
        Assert.Equal(3, unchanged.Value);
        await db.ReferenceRanges.Where(x => x.Id == range.Id).ExecuteDeleteAsync();
        var afterRemoval = await store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, -3, Convert.FromBase64String(unchanged.Version), default);
        Assert.Equal(-2, afterRemoval.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, afterRemoval.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.Low, afterRemoval.Abnormality);
        Assert.DoesNotContain(recorder.Commands, x => x.Contains("MedicalAnalyses", StringComparison.Ordinal)
            || x.Contains("ReferenceRanges", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task AllEditableParentStates_AllowEntryAndCorrectionWithoutTransition()
    {
        foreach (var status in new[] { RequestStatus.Registered, RequestStatus.InProgress, RequestStatus.ResultsEntered })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET Status={(int)status} WHERE Id={fixture.Request.Id}");
            var store = AnalysisResultSqlFixture.Store(db);
            var created = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 0, default);
            await store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 1, Convert.FromBase64String(created.Version), default);
            Assert.Equal(status, (await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id)).Status);
        }
    }

    [SqlServerFact]
    public async Task ValidatedAndDeliveredParentStates_BlockCreateAndCorrection()
    {
        foreach (var delivered in new[] { false, true })
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            var store = AnalysisResultSqlFixture.Store(db);
            var created = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default);
            var date = AnalysisResultSqlFixture.EntryTime.AddMinutes(1);
            // Fixture state only: no official validation endpoint/workflow is invoked.
            if (delivered)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET Status=4, ValidatedAt={date}, ValidatedBy=N'SYN-VALIDATOR', DeliveredAt={date} WHERE Id={fixture.Request.Id}");
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET Status=3, ValidatedAt={date}, ValidatedBy=N'SYN-VALIDATOR' WHERE Id={fixture.Request.Id}");
            await Error(() => store.CreateAsync(fixture.Request.Id, fixture.EmptyItem.Id, 1, default), AnalysisResultError.Conflict);
            await Error(() => store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 2, Convert.FromBase64String(created.Version), default), AnalysisResultError.Conflict);
            Assert.Equal(created, await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        }
    }

    [SqlServerFact]
    public async Task ValidatedResult_BlocksCorrectionAndSqlRejectsPartialValidationMetadata()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        var store = AnalysisResultSqlFixture.Store(db);
        var created = await store.CreateAsync(fixture.Request.Id, fixture.Item.Id, 1, default);
        var date = AnalysisResultSqlFixture.EntryTime.AddMinutes(1);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AnalysisResults SET ValidatedAt={date} WHERE Id={created.Id}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE LaboratoryRequests SET ValidatedAt={date} WHERE Id={fixture.Request.Id}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AnalysisResults SET ValidatedBy=N'SYN-VALIDATOR' WHERE Id={created.Id}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE LaboratoryRequests SET DeliveredAt={date} WHERE Id={fixture.Request.Id}"))).Number);
        Assert.Equal(created, await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AnalysisResults SET ValidatedAt={date}, ValidatedBy=N'SYN-VALIDATOR' WHERE Id={created.Id}");
        var validated = (await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default))!;
        await Error(() => store.UpdateAsync(fixture.Request.Id, fixture.Item.Id, 2, Convert.FromBase64String(validated.Version), default), AnalysisResultError.Conflict);
        Assert.Equal(validated, await store.GetAsync(fixture.Request.Id, fixture.Item.Id, default));
    }

    internal static async Task Error(Func<Task> action, AnalysisResultError expected) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<AnalysisResultException>(action)).Error);

    private sealed class ReadRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
