using System.Globalization;
using System.Reflection;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Clinisys.Tests.AnalysisResults;

// Pure calculations and EF metadata/change tracking only: never opens SQL.
public sealed class LdlCalculationTests
{
    private static readonly DateTimeOffset Instant = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly LdlAnalysisCodes Codes = new("SYN-TC", "SYN-HDL", "SYN-TG", "SYN-LDL");
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Theory]
    [InlineData("mg/dL", 200, 50, 150, 120)]
    [InlineData("mg/dL", 0, 0, 0, 0)]
    [InlineData("mmol/L", 5, 1, 2.2, 3)]
    [InlineData("mmol/L", 5.1, 1.2, 1, 3.445455)]
    [InlineData("mg/dL", 200.123456, 50, 100.000001, 130.123456)]
    public void Formula_UsesExactUnitAndSixDecimalAwayFromZeroRounding(string unit, decimal total, decimal hdl, decimal tg, decimal expected)
    {
        var result = Friedewald.Calculate(total, hdl, tg, unit, unit, unit, unit);
        Assert.Equal("Calculated", result.Status);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("mg/dL", 400)]
    [InlineData("mg/dL", 401)]
    [InlineData("mmol/L", 4.5)]
    [InlineData("mmol/L", 5)]
    public void Threshold_IsStrictAndNoValueIsInvented(string unit, decimal tg)
    {
        var outcome = Friedewald.Calculate(1000, 0, tg, unit, unit, unit, unit);
        Assert.Equal("TriglyceridesTooHigh", outcome.Status);
        Assert.Null(outcome.Value);
    }

    [Theory]
    [InlineData("mg/dL", 399.999999)]
    [InlineData("mmol/L", 4.499999)]
    public void JustBelowThreshold_IsAllowed(string unit, decimal tg) =>
        Assert.NotNull(Friedewald.Calculate(1000, 0, tg, unit, unit, unit, unit).Value);

    [Theory]
    [InlineData("g/L", "g/L", "g/L", "g/L")]
    [InlineData("mg/dl", "mg/dl", "mg/dl", "mg/dl")]
    [InlineData("mg/dL", "mmol/L", "mg/dL", "mg/dL")]
    [InlineData("mg/dL", "mg/dL", "mmol/L", "mg/dL")]
    [InlineData("mg/dL", "mg/dL", "mg/dL", "mmol/L")]
    [InlineData("mg/dL ", "mg/dL", "mg/dL", "mg/dL")]
    public void UnknownOrMixedUnits_AreNeverConverted(string total, string hdl, string tg, string ldl)
    {
        var result = Friedewald.Calculate(200, 50, 100, total, hdl, tg, ldl);
        Assert.Equal("IncompatibleUnits", result.Status);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(null, 50, 100)]
    [InlineData(200, null, 100)]
    [InlineData(200, 50, null)]
    public void EachMissingNumericSource_BlocksCalculation(int? total, int? hdl, int? tg)
    {
        var result = Friedewald.Calculate(total, hdl, tg, "mg/dL", "mg/dL", "mg/dL", "mg/dL");
        Assert.Equal("MissingSource", result.Status);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(-1, 0, 0, "InvalidSource")]
    [InlineData(200, -1, 0, "InvalidSource")]
    [InlineData(200, 50, -1, "InvalidSource")]
    [InlineData(50, 100, 100, "InvalidCalculatedValue")]
    public void NegativeSourcesAndNegativeOutput_AreRejected(decimal total, decimal hdl, decimal tg, string status)
    {
        var result = Friedewald.Calculate(total, hdl, tg, "mg/dL", "mg/dL", "mg/dL", "mg/dL");
        Assert.Equal(status, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Mapping_IsExplicitDistinctAndBasedOnHistoricalCodesAndUnits()
    {
        var (request, items, results) = Data();
        Assert.Equal("NotConfigured", LdlCalculation.Evaluate(new(), items, results).Status);
        Assert.False(new LdlAnalysisCodes("A", "A", "B", "C").IsValid);
        Assert.False(new LdlAnalysisCodes("lowercase", "HDL", "TG", "LDL").IsValid);
        Assert.Equal("LdlNotRequested", LdlCalculation.Evaluate(Codes, items[..3], results).Status);
        Assert.Equal("MissingSource", LdlCalculation.Evaluate(Codes, items, results[..2]).Status);
        var duplicate = new RequestItem(request, new MedicalAnalysis("SYN-TC", "Other", "mg/dL", 1));
        Assert.Equal("AmbiguousAnalysis", LdlCalculation.Evaluate(Codes, [.. items, duplicate], results).Status);
        Assert.Equal(120, LdlCalculation.Evaluate(Codes, items, results).Value);
    }

    [Fact]
    public void ExistingManualLdl_IsPreservedEvenWhenSourcesWouldProduceAnotherValue()
    {
        var (_, items, results) = Data();
        var manual = new AnalysisResult(items[3].Id, 99, "manual", null, Instant);
        var outcome = LdlCalculation.Evaluate(Codes, items, [.. results, manual]);
        Assert.Equal("ManualResultPreserved", outcome.Status);
        Assert.Null(outcome.Value);
        Assert.Equal(99, manual.Value);
        Assert.False(manual.IsCalculated);
    }

    [Fact]
    public void SourceCorrection_RecalculatesAndAuditsPreviousAndNewLdl_KeepingBounds()
    {
        var (request, items, results) = Data();
        var initial = LdlCalculation.Evaluate(Codes, items, results);
        var ldl = AnalysisResult.CreateCalculated(items[3].Id, initial.Value!.Value, "operator", new(100, 130), Instant);
        typeof(Clinisys.Domain.Common.Entity).GetProperty(nameof(ldl.Version))!.SetValue(ldl, Version);
        results[0].UpdateValue(220, "corrector", Instant.AddMinutes(1));
        var recalculated = LdlCalculation.Evaluate(Codes, items, [.. results, ldl]);
        Assert.Equal(140, recalculated.Value);
        using var db = Context();
        typeof(AnalysisResultStore).GetMethod("PrepareAuditedCorrection", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, request.Id, ldl, recalculated.Value!.Value, Version, "corrector", Instant.AddMinutes(1)]);
        Assert.True(ldl.IsCalculated);
        Assert.Equal(140, ldl.Value);
        Assert.Equal(100, ldl.ReferenceLowerBoundSnapshot);
        Assert.Equal(130, ldl.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.High, ldl.Abnormality);
        var audit = Assert.Single(db.ChangeTracker.Entries<AuditEntry>()).Entity;
        Assert.Equal(120, decimal.Parse(audit.OldValue!, CultureInfo.InvariantCulture));
        Assert.Equal(140, decimal.Parse(audit.NewValue!, CultureInfo.InvariantCulture));
        Assert.Equal("corrector", audit.Actor);
        Assert.Equal(Instant.AddMinutes(1), audit.OccurredAt);
    }

    [Fact]
    public void InvalidatedAutomaticLdl_IsRemovedWithAudit_ManualAndValidatedValuesAreProtected()
    {
        var (request, items, results) = Data();
        var ldl = AnalysisResult.CreateCalculated(items[3].Id, 120, "operator", null, Instant);
        results[2].UpdateValue(400, "corrector", Instant);
        Assert.Equal("TriglyceridesTooHigh", LdlCalculation.Evaluate(Codes, items, [.. results, ldl]).Status);
        using var db = Context();
        var remove = typeof(AnalysisResultStore).GetMethod("RemoveInvalidCalculatedResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        remove.Invoke(null, [db, request.Id, ldl, "corrector", Instant]);
        Assert.Equal(EntityState.Deleted, db.Entry(ldl).State);
        var audit = Assert.Single(db.ChangeTracker.Entries<AuditEntry>()).Entity;
        Assert.Equal("120", audit.OldValue);
        Assert.Null(audit.NewValue);
        foreach (var protectedResult in new[] { new AnalysisResult(items[3].Id, 5, "manual", null, Instant),
                     AnalysisResult.CreateCalculated(items[3].Id, 120, "operator", null, Instant) })
        {
            if (protectedResult.IsCalculated) protectedResult.Validate("biologist", Instant);
            var ex = Assert.Throws<TargetInvocationException>(() => remove.Invoke(null, [db, request.Id, protectedResult, "actor", Instant]));
            Assert.IsType<AnalysisResultException>(ex.InnerException);
            Assert.Equal(EntityState.Detached, db.Entry(protectedResult).State);
        }
    }

    [Fact]
    public void ValidatedRequestAndResults_CannotBeOrdinarilyChanged()
    {
        var (request, items, _) = Data();
        request.StartProcessing(); request.MarkResultsEntered(true); request.ValidateResults(true, "biologist", Instant);
        var result = AnalysisResult.CreateCalculated(items[3].Id, 120, "operator", null, Instant);
        result.Validate("biologist", Instant);
        Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
        Assert.Throws<InvalidOperationException>(() => result.UpdateValue(99, "corrector", Instant));
        Assert.Equal(120, result.Value);
        request.Deliver(Instant);
        Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
    }

    [Fact]
    public void Migration_AddsOnlyManualSafeProvenanceFlag_AndRefusesDowngrade()
    {
        using var db = Context();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_AddCalculatedAnalysisResult")).Value, db.Database.ProviderName!);
        var operation = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("AnalysisResults", operation.Table);
        Assert.Equal("IsCalculated", operation.Name);
        Assert.Equal("bit", operation.ColumnType);
        Assert.False(operation.IsNullable);
        Assert.Equal(false, operation.DefaultValue);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static ClinisysDbContext Context() => new(new DbContextOptionsBuilder<ClinisysDbContext>()
        .UseSqlServer("Server=offline.invalid;Database=UnitTest;Integrated Security=True").Options);

    private static (LaboratoryRequest, RequestItem[], AnalysisResult[]) Data()
    {
        var request = LaboratoryRequest.Register("LAB-1970-000001",
            new Patient("Synthetic", "Lipid", new(1960, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY"),
            "Synthetic", 0, DateTimeOffset.UnixEpoch);
        var items = new[] { "SYN-TC", "SYN-HDL", "SYN-TG", "SYN-LDL" }
            .Select(code => new RequestItem(request, new MedicalAnalysis(code, "Synthetic lipid", "mg/dL", 1))).ToArray();
        var results = new[] { new AnalysisResult(items[0].Id, 200, "operator", null, Instant),
            new AnalysisResult(items[1].Id, 50, "operator", null, Instant),
            new AnalysisResult(items[2].Id, 150, "operator", null, Instant) };
        return (request, items, results);
    }
}
