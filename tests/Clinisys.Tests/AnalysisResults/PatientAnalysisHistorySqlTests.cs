using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.AnalysisResults;

[Collection(AnalysisResultSqlCollection.Name)]
public sealed class PatientAnalysisHistorySqlTests
{
    [SqlServerFact]
    public async Task History_FiltersPatientAndAnalysis_OrdersByRequestDate_PreservesUnitsAndAbnormality()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var otherPatient = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var requests = await db.LaboratoryRequests.Where(x => x.Id == fixture.Request.Id
            || x.Id == fixture.OtherRequest.Id || x.Id == otherPatient.Request.Id).ToDictionaryAsync(x => x.Id);
        // Deliberately insert the newer request first; the read must sort by date.
        var older = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!.SetValue(requests[fixture.Request.Id], older.AddDays(5));
        foreach (var request in requests.Values) Validate(request, request.Id == fixture.OtherRequest.Id);
        var sameAnalysisOtherPatient = new RequestItem(requests[otherPatient.Request.Id], fixture.Analysis);
        db.RequestItems.Add(sameAnalysisOtherPatient);
        var high = Result(fixture.Item.Id, 3, true, new(-2, 2));
        var low = Result(fixture.ForeignItem.Id, -3, true, new(-2, 2));
        db.AnalysisResults.AddRange(high, low, Result(fixture.EmptyItem.Id, 12, true), Result(sameAnalysisOtherPatient.Id, 100, true));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET UnitSnapshot=N'other-unit' WHERE Id={fixture.Item.Id}");
        db.ChangeTracker.Clear();
        var auditCount = await db.AuditEntries.CountAsync();
        var store = new PatientAnalysisHistoryStore(db);
        var history = (await store.GetAsync(fixture.Patient.Id, fixture.Analysis.Id, default))!;
        Assert.Equal(fixture.Patient.Id, history.PatientId);
        Assert.Equal(fixture.Analysis.Id, history.AnalysisId);
        Assert.Equal(new[] { fixture.OtherRequest.Id, fixture.Request.Id }, history.Points.Select(x => x.RequestId));
        Assert.Equal(new[] { -3m, 3m }, history.Points.Select(x => x.Value));
        Assert.Equal(new[] { "unit", "other-unit" }, history.Points.Select(x => x.Unit));
        Assert.Equal(new[] { Abnormality.Low, Abnormality.High }, history.Points.Select(x => x.Abnormality));
        Assert.All(history.Points, x => Assert.Equal(AnalysisResultSqlFixture.EntryTime, x.ResultAt));
        var anotherAnalysis = (await store.GetAsync(fixture.Patient.Id, fixture.OtherAnalysis.Id, default))!;
        Assert.Equal(12, Assert.Single(anotherAnalysis.Points).Value);
        Assert.Equal(Abnormality.NotEvaluated, anotherAnalysis.Points[0].Abnormality);
        Assert.Equal(100, Assert.Single((await store.GetAsync(otherPatient.Patient.Id, fixture.Analysis.Id, default))!.Points).Value);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(auditCount, await db.AuditEntries.CountAsync());
        await tx.RollbackAsync();
    }

    [SqlServerFact]
    public async Task OnlyValidatedOrDeliveredRequests_WithValidatedResults_AreIncluded()
    {
        foreach (var status in Enum.GetValues<RequestStatus>())
        {
            await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
            await using var db = SqlServerTestDatabase.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var request = await db.LaboratoryRequests.SingleAsync(x => x.Id == fixture.Request.Id);
            if (status >= RequestStatus.InProgress) request.StartProcessing();
            if (status >= RequestStatus.ResultsEntered) request.MarkResultsEntered(true);
            if (status >= RequestStatus.Validated) request.ValidateResults(true, "SYN-VALIDATOR", AnalysisResultSqlFixture.EntryTime.AddMinutes(1));
            if (status == RequestStatus.Delivered) request.Deliver(AnalysisResultSqlFixture.EntryTime.AddMinutes(2));
            db.AnalysisResults.Add(Result(fixture.Item.Id, 1, true));
            // Even on a validated parent, an inconsistent provisional result is excluded.
            db.AnalysisResults.Add(Result(fixture.EmptyItem.Id, 2, false));
            await db.SaveChangesAsync();
            var store = new PatientAnalysisHistoryStore(db);
            var history = (await store.GetAsync(fixture.Patient.Id, fixture.Analysis.Id, default))!;
            Assert.Equal(status >= RequestStatus.Validated ? 1 : 0, history.Points.Count);
            Assert.Empty((await store.GetAsync(fixture.Patient.Id, fixture.OtherAnalysis.Id, default))!.Points);
            await tx.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task MissingResources_ReturnNull_ExistingPairWithoutResults_ReturnsEmpty()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        await using var db = SqlServerTestDatabase.CreateContext();
        var store = new PatientAnalysisHistoryStore(db);
        Assert.Null(await store.GetAsync(Guid.NewGuid(), fixture.Analysis.Id, default));
        Assert.Null(await store.GetAsync(fixture.Patient.Id, Guid.NewGuid(), default));
        Assert.Empty((await store.GetAsync(fixture.Patient.Id, fixture.Analysis.Id, default))!.Points);
    }

    private static void Validate(LaboratoryRequest request, bool delivered)
    {
        request.StartProcessing(); request.MarkResultsEntered(true);
        request.ValidateResults(true, "SYN-VALIDATOR", AnalysisResultSqlFixture.EntryTime.AddMinutes(1));
        if (delivered) request.Deliver(AnalysisResultSqlFixture.EntryTime.AddMinutes(2));
    }
    private static AnalysisResult Result(Guid item, decimal value, bool validated, ReferenceInterval? interval = null)
    {
        var result = new AnalysisResult(item, value, "SYN-ENTRY", interval, AnalysisResultSqlFixture.EntryTime);
        if (validated) result.Validate("SYN-VALIDATOR", AnalysisResultSqlFixture.EntryTime.AddMinutes(1));
        return result;
    }
}
