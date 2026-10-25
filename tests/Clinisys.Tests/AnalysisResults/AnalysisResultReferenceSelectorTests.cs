using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.AnalysisResults;

public sealed class AnalysisResultReferenceSelectorTests
{
    [Theory]
    [InlineData(0, 0, 1, true)]
    [InlineData(0, 1, 2, false)]
    [InlineData(1, 0, 1, false)]
    [InlineData(1, 1, 2, true)]
    public void Selection_UsesElapsedCalendarDaysAndHalfOpenBounds(int age, int minimum, int maximum, bool expected)
    {
        var fixture = Fixture(new(2024, 2, 29), new DateTimeOffset(2024, 2, 29, 23, 59, 0, TimeSpan.Zero).AddDays(age));
        var selected = Select(fixture, [Range(fixture, Sex.Female, minimum, maximum)]);
        Assert.Equal(expected, selected is not null);
    }

    [Theory]
    [InlineData(2024, 2)]
    [InlineData(2023, 1)]
    public void Age_CorrectlyAccountsForLeapDays(int year, int age)
    {
        var fixture = Fixture(new(year, 2, 28), new(year, 3, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.NotNull(Select(fixture, [Range(fixture, Sex.Female, age, age + 1)]));
        Assert.Null(Select(fixture, [Range(fixture, Sex.Female, 0, age)]));
    }

    [Fact]
    public void Age_UsesUtcDateForPersistedOffsetAndPatientSnapshots()
    {
        var fixture = Fixture(new(2024, 2, 28), new(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        // Simulate a legacy SQL datetimeoffset row. New request registration itself requires UTC.
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!
            .SetValue(fixture.Request, new DateTimeOffset(2024, 3, 1, 0, 30, 0, TimeSpan.FromHours(1)));
        fixture.Patient.Update("Corrected", "Synthetic", new(2024, 1, 1), Sex.Male, phoneNumber: "TEST-ONLY");
        Assert.NotNull(Select(fixture, [Range(fixture, Sex.Female, 1, 2)]));
        Assert.Null(Select(fixture, [Range(fixture, Sex.Male, 1, 2)]));
    }

    [Theory]
    [InlineData(Sex.Female, Sex.Female, true)]
    [InlineData(Sex.Male, Sex.Female, false)]
    [InlineData(Sex.Unspecified, Sex.Female, false)]
    [InlineData(Sex.Female, Sex.Unspecified, false)]
    [InlineData(Sex.Unspecified, Sex.Unspecified, true)]
    public void Sex_RequiresExactMatchWithoutFallback(Sex patientSex, Sex rangeSex, bool expected)
    {
        var fixture = Fixture(sex: patientSex);
        Assert.Equal(expected, Select(fixture, [Range(fixture, rangeSex, 0, null)]) is not null);
    }

    [Fact]
    public void NoApplicableRange_SkipsUnitCheckAndProducesNotEvaluated()
    {
        var fixture = Fixture();
        fixture.Analysis.Update("SYN", "Synthetic", "different-unit", 10);
        foreach (var ranges in new[] { Array.Empty<ReferenceRange>(), new[] { Range(fixture, Sex.Male, 0, null) },
            new[] { new ReferenceRange(Guid.NewGuid(), Sex.Female, 0, null, new(0, 1)) } })
        {
            var interval = Select(fixture, ranges);
            Assert.Null(interval);
            var result = new AnalysisResult(fixture.Item.Id, 0, "synthetic", interval, DateTimeOffset.UnixEpoch);
            Assert.Null(result.ReferenceLowerBoundSnapshot);
            Assert.Null(result.ReferenceUpperBoundSnapshot);
            Assert.Equal(Abnormality.NotEvaluated, result.Abnormality);
        }
    }

    [Theory]
    [InlineData("unit", true)]
    [InlineData("Unit", false)]
    [InlineData("different", false)]
    public void ApplicableRange_RequiresOrdinalExactUnit(string unit, bool compatible)
    {
        var fixture = Fixture();
        fixture.Analysis.Update("SYN", "Synthetic", unit, 10);
        var ranges = new[] { Range(fixture, Sex.Female, 0, null) };
        if (compatible) Assert.Equal(new ReferenceInterval(-2, 2), Select(fixture, ranges));
        else Assert.Equal(AnalysisResultError.Conflict, Assert.Throws<AnalysisResultException>(() => Select(fixture, ranges)).Error);
    }

    [Fact]
    public void MultipleApplicableRanges_AreRejectedWithoutArbitraryChoice()
    {
        var fixture = Fixture();
        var error = Assert.Throws<AnalysisResultException>(() => Select(fixture,
            [Range(fixture, Sex.Female, 0, null), Range(fixture, Sex.Female, 1, null)]));
        Assert.Equal(AnalysisResultError.Conflict, error.Error);
    }

    [Fact]
    public void HistoricalItem_RemainsEligibleAfterAnalysisDeactivation()
    {
        var fixture = Fixture();
        fixture.Analysis.Deactivate();
        Assert.Equal(new ReferenceInterval(-2, 2), Select(fixture, [Range(fixture, Sex.Female, 0, null)]));
    }

    [Fact]
    public void Selection_ReturnsIndependentBoundsSnapshot()
    {
        var fixture = Fixture();
        var range = Range(fixture, Sex.Female, 0, null);
        var interval = Select(fixture, [range]);
        range.Update(Sex.Female, 0, null, new(100, 200));
        Assert.Equal(new ReferenceInterval(-2, 2), interval);
    }

    [Fact]
    public void InconsistentStoredRelationshipsOrAge_AreInternalErrors()
    {
        var fixture = Fixture();
        var other = Fixture();
        Assert.Equal(AnalysisResultError.Internal, Assert.Throws<AnalysisResultException>(() =>
            AnalysisResultReferenceSelector.Select(fixture.Request, other.Item, fixture.Analysis, [])).Error);
        Assert.Equal(AnalysisResultError.Internal, Assert.Throws<AnalysisResultException>(() =>
            AnalysisResultReferenceSelector.Select(fixture.Request, fixture.Item, other.Analysis, [])).Error);
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!
            .SetValue(fixture.Request, new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(AnalysisResultError.Internal, Assert.Throws<AnalysisResultException>(() => Select(fixture, [])).Error);
    }

    private sealed record Data(Patient Patient, LaboratoryRequest Request, MedicalAnalysis Analysis, RequestItem Item);
    private static Data Fixture(DateOnly? birth = null, DateTimeOffset? registered = null, Sex sex = Sex.Female)
    {
        var patient = new Patient("Synthetic", "Test", birth ?? new(2000, 1, 1), sex, phoneNumber: "TEST-ONLY");
        var instant = registered ?? new(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var request = LaboratoryRequest.Register(LaboratoryRequest.FormatNumber(instant.Year, 1), patient, "Synthetic", 0, instant);
        var analysis = new MedicalAnalysis("SYN", "Synthetic", "unit", 10);
        return new(patient, request, analysis, new(request, analysis));
    }
    private static ReferenceRange Range(Data data, Sex sex, int minimum, int? maximum) => new(data.Analysis.Id, sex, minimum, maximum, new(-2, 2));
    private static ReferenceInterval? Select(Data data, IEnumerable<ReferenceRange> ranges) =>
        AnalysisResultReferenceSelector.Select(data.Request, data.Item, data.Analysis, ranges);
}
