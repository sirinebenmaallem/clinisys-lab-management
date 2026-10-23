using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.Persistence;

// Opt-in tests use the isolated test database and roll back every data transaction.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLINISYS_SQL_TEST_CONNECTION")))
            Skip = "Prepare ClinisysTestDb and set CLINISYS_SQL_TEST_CONNECTION to run isolated SQL Server tests.";
    }
}

public sealed class SqlServerConstraintTests
{
    [SqlServerFact]
    public async Task UniqueAnalysisCodes_AreEnforcedBySqlServer()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var code = Code();
        db.Add(new MedicalAnalysis(code, "Synthetic test", "unit", 1));
        await db.SaveChangesAsync();
        db.Add(new MedicalAnalysis(code, "Duplicate synthetic test", "unit", 2));
        await AssertSqlError(() => db.SaveChangesAsync(), 2601, 2627);
    }

    [SqlServerFact]
    public async Task UniqueRequestNumbers_AreEnforcedBySqlServer()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = Patient();
        var number = Code();
        db.AddRange(patient, new LaboratoryRequest(number, patient, "test-physician"));
        await db.SaveChangesAsync();
        db.Add(new LaboratoryRequest(number, patient, "test-physician"));
        await AssertSqlError(() => db.SaveChangesAsync(), 2601, 2627);
    }

    [SqlServerFact]
    public async Task DirectSql_RejectsInvalidRangeAndFinancialValues()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var analysis = new MedicalAnalysis(Code(), "Synthetic test", "unit", 1);
        var patient = Patient();
        var request = new LaboratoryRequest(Code(), patient, "test-physician");
        var range = new ReferenceRange(analysis.Id, Sex.Unspecified, 0, null, new ReferenceInterval(1, 2));
        var item = new RequestItem(request, analysis);
        db.AddRange(analysis, patient, request, range, item);
        await db.SaveChangesAsync();
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ReferenceRanges SET LowerBound = 3 WHERE Id = {range.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ReferenceRanges SET MaximumAgeDays = 0 WHERE Id = {range.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price = -1 WHERE Id = {analysis.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RequestItems SET DiscountPercentSnapshot = 101 WHERE Id = {item.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE LaboratoryRequests SET Status = 3 WHERE Id = {request.Id}"), 547);
    }

    [SqlServerFact]
    public async Task HistoricalRelationships_PreventParentDeletionAndKeepSnapshots()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = Patient();
        var analysis = new MedicalAnalysis(Code(), "Synthetic test", "unit", 10);
        var request = new LaboratoryRequest(Code(), patient, "test-physician", 10);
        var item = new RequestItem(request, analysis);
        var result = new AnalysisResult(item.Id, 2, "test-actor", new ReferenceInterval(1, 3));
        var audit = new AuditEntry(request.Id, item.Id, AuditAction.Created, "test-actor", "Value", null, "2");
        db.AddRange(patient, analysis, request, item, result, audit);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price = 99, Unit = N'changed' WHERE Id = {analysis.Id}");
        await db.Entry(item).ReloadAsync();
        Assert.Equal(10, item.UnitPriceSnapshot);
        Assert.Equal(10, item.DiscountPercentSnapshot);
        Assert.Equal(9, item.NetPrice);
        Assert.Equal("unit", item.UnitSnapshot);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Patients WHERE Id = {patient.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM LaboratoryRequests WHERE Id = {request.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM MedicalAnalyses WHERE Id = {analysis.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestItems WHERE Id = {item.Id}"), 547);
        Assert.True(await db.AnalysisResults.AnyAsync(x => x.Id == result.Id));
        Assert.True(await db.AuditEntries.AnyAsync(x => x.Id == audit.Id));
    }

    [SqlServerFact]
    public async Task AuditCannotReferenceAnItemFromAnotherRequest()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = Patient();
        var analysis = new MedicalAnalysis(Code(), "Synthetic test", "unit", 1);
        var first = new LaboratoryRequest(Code(), patient, "test-physician");
        var second = new LaboratoryRequest(Code(), patient, "test-physician");
        var item = new RequestItem(first, analysis);
        db.AddRange(patient, analysis, first, second, item);
        await db.SaveChangesAsync();
        db.Add(new AuditEntry(second.Id, item.Id, AuditAction.Updated, "test-actor", "Value", "1", "2"));
        await AssertSqlError(() => db.SaveChangesAsync(), 547);
    }

    [SqlServerFact]
    public async Task DecimalValuesAndInitialStatus_SurviveSqlRoundTrip()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = Patient();
        var analysis = new MedicalAnalysis(Code(), "Synthetic precision test", "unit", 123456.78m);
        var request = new LaboratoryRequest(Code(), patient, "test-physician", 12.34m);
        var item = new RequestItem(request, analysis);
        var range = new ReferenceRange(analysis.Id, Sex.Unspecified, 0, null, new ReferenceInterval(-0.333333m, 0.999999m));
        var result = new AnalysisResult(item.Id, 0.123456m, "test-actor", new ReferenceInterval(range.LowerBound, range.UpperBound));
        db.AddRange(patient, analysis, request, item, range, result);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(123456.78m, (await db.MedicalAnalyses.SingleAsync(x => x.Id == analysis.Id)).Price);
        var storedRequest = await db.LaboratoryRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(12.34m, storedRequest.DiscountPercent);
        Assert.Equal(RequestStatus.Registered, storedRequest.Status);
        Assert.Null(storedRequest.ValidatedAt);
        Assert.Null(storedRequest.DeliveredAt);
        var storedItem = await db.RequestItems.SingleAsync(x => x.Id == item.Id);
        Assert.Equal(123456.78m, storedItem.UnitPriceSnapshot);
        Assert.Equal(12.34m, storedItem.DiscountPercentSnapshot);
        var storedResult = await db.AnalysisResults.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(0.123456m, storedResult.Value);
        Assert.Equal(-0.333333m, storedResult.ReferenceLowerBoundSnapshot);
        Assert.Equal(0.999999m, storedResult.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.Normal, storedResult.Abnormality);
        Assert.Equal(-0.333333m, (await db.ReferenceRanges.SingleAsync(x => x.Id == range.Id)).LowerBound);
    }

    [SqlServerFact]
    public async Task UniqueRelationsRangesAndIdentity_AreEnforcedBySqlServer()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var identity = Code();
        var patient = new Patient("Synthetic", "Test", new DateOnly(2000, 1, 1), Sex.Unspecified, identity);
        var analysis = new MedicalAnalysis(Code(), "Synthetic relation test", "unit", 1);
        var panel = new AnalysisPanel(Code(), "Synthetic panel");
        var request = new LaboratoryRequest(Code(), patient, "test-physician");
        var item = new RequestItem(request, analysis);
        db.AddRange(patient, analysis, panel, request, item,
            new PanelAnalysis(panel.Id, analysis.Id), new RequestPanel(request.Id, panel),
            new ReferenceRange(analysis.Id, Sex.Unspecified, 0, null, new ReferenceInterval(0, 1)),
            new AnalysisResult(item.Id, 1, "test-actor"));
        await db.SaveChangesAsync();

        var duplicates = new Clinisys.Domain.Common.Entity[]
        {
            new Patient("Synthetic", "Duplicate", new DateOnly(2000, 1, 1), Sex.Unspecified, identity),
            new AnalysisPanel(panel.Code, "Duplicate panel"),
            new PanelAnalysis(panel.Id, analysis.Id),
            new RequestPanel(request.Id, panel),
            new RequestItem(request, analysis),
            new ReferenceRange(analysis.Id, Sex.Unspecified, 0, null, new ReferenceInterval(0, 1)),
            new AnalysisResult(item.Id, 2, "test-actor")
        };
        foreach (var duplicate in duplicates)
        {
            // Test the database index independently of EF's one-to-one graph fixup.
            db.ChangeTracker.Clear();
            db.Add(duplicate);
            await AssertSqlError(() => db.SaveChangesAsync(), 2601, 2627);
            db.Entry(duplicate).State = EntityState.Detached;
        }
    }

    [SqlServerFact]
    public async Task AuditValues_ArePersistedAndRequiredFieldsAreEnforced()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = Patient();
        var request = new LaboratoryRequest(Code(), patient, "test-physician");
        var audit = new AuditEntry(request.Id, null, AuditAction.Updated, "synthetic-actor", "Value", "before", "after");
        db.AddRange(patient, request, audit);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.AuditEntries.SingleAsync(x => x.Id == audit.Id);
        Assert.Equal(request.Id, stored.LaboratoryRequestId);
        Assert.Null(stored.RequestItemId);
        Assert.Equal("synthetic-actor", stored.Actor);
        Assert.Equal("before", stored.OldValue);
        Assert.Equal("after", stored.NewValue);
        Assert.Equal(audit.OccurredAt, stored.OccurredAt);
        Assert.Equal(AuditAction.Updated, stored.Action);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditEntries SET OldValue = NULL, NewValue = NULL WHERE Id = {audit.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditEntries SET Actor = N' ' WHERE Id = {audit.Id}"), 547);
        await AssertSqlError(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditEntries SET LaboratoryRequestId = {Guid.NewGuid()} WHERE Id = {audit.Id}"), 547);
    }

    private static ClinisysDbContext CreateContext()
    {
        return SqlServerTestDatabase.CreateContext();
    }

    private static async Task AssertSqlError(Func<Task> action, params int[] numbers)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        var sql = Assert.IsType<SqlException>(exception is DbUpdateException ? exception.InnerException : exception);
        Assert.Contains(sql.Number, numbers);
    }

    private static string Code() => $"T{Guid.NewGuid():N}"[..25];
    private static Patient Patient() => new("Synthetic", "Test", new DateOnly(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY");
}
