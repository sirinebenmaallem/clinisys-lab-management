using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.AnalysisResults;

public sealed class AnalysisResultReferencePreviewTests
{
    [Theory]
    [InlineData(Sex.Female, 1, "10", "20")]
    [InlineData(Sex.Female, 2, "-2", "2")]
    [InlineData(Sex.Female, 3, null, null)]
    [InlineData(Sex.Male, 2, "100", "200")]
    [InlineData(Sex.Unspecified, 2, null, null)]
    public void Preview_ReusesSexAndHalfOpenAgeSelection(Sex sex, int ageDays, string? lower, string? upper)
    {
        var (request, analysis, item) = Fixture(sex, ageDays);
        ReferenceRange[] ranges = [new(analysis.Id, Sex.Female, 0, 2, new(10, 20)),
            new(analysis.Id, Sex.Female, 2, 3, new(-2, 2)), new(analysis.Id, Sex.Male, 0, null, new(100, 200))];
        var preview = AnalysisResultReferenceSelector.Preview(request, item, analysis, ranges);
        Assert.Equal(lower is null ? "NoReference" : "Applicable", preview.Status);
        Assert.Equal(lower, preview.LowerBound);
        Assert.Equal(upper, preview.UpperBound);
        var selected = AnalysisResultReferenceSelector.Select(request, item, analysis, ranges);
        Assert.Equal(selected?.LowerBound.ToString(System.Globalization.CultureInfo.InvariantCulture), preview.LowerBound);
    }

    [Fact]
    public void Preview_UsesUtcRegistrationSnapshotRatherThanCurrentPatient()
    {
        var patient = new Patient("Synthetic", "Preview", new(2024, 2, 28), Sex.Female, phoneNumber: "TEST-ONLY");
        var request = LaboratoryRequest.Register("LAB-2024-000001", patient, "Synthetic", 0, new(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        patient.Update("Synthetic", "Changed", new(2000, 1, 1), Sex.Male, phoneNumber: "TEST-ONLY");
        var analysis = new MedicalAnalysis("PREVIEW", "Synthetic", "unit", 1);
        var item = new RequestItem(request, analysis);
        var preview = AnalysisResultReferenceSelector.Preview(request, item, analysis,
            [new(analysis.Id, Sex.Female, 2, 3, new(-2, 2))]);
        Assert.Equal(new AnalysisResultReferenceDto("Applicable", "-2", "2"), preview);
    }

    [Fact]
    public void IncompatibleOrAmbiguousReference_IsUnavailable_AndWriteSelectorStillRejects()
    {
        var (request, analysis, item) = Fixture(Sex.Female, 2);
        ReferenceRange[] ranges = [new(analysis.Id, Sex.Female, 2, 3, new(-2, 2))];
        var otherUnit = new MedicalAnalysis("OTHER", "Synthetic", "other-unit", 1);
        // Preserve identity to model a catalogue unit change after item registration.
        typeof(MedicalAnalysis).GetProperty(nameof(MedicalAnalysis.Unit))!.SetValue(analysis, otherUnit.Unit);
        Assert.Equal("Unavailable", AnalysisResultReferenceSelector.Preview(request, item, analysis, ranges).Status);
        Assert.Throws<AnalysisResultException>(() => AnalysisResultReferenceSelector.Select(request, item, analysis, ranges));
        Assert.Equal("NoReference", AnalysisResultReferenceSelector.Preview(request, item, analysis, []).Status);
        typeof(MedicalAnalysis).GetProperty(nameof(MedicalAnalysis.Unit))!.SetValue(analysis, item.UnitSnapshot);
        Assert.Equal("Unavailable", AnalysisResultReferenceSelector.Preview(request, item, analysis, [ranges[0], ranges[0]]).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorrectionPreview_PreservesOriginalReferenceIncludingNoReference(bool withReference)
    {
        var (request, analysis, item) = Fixture(Sex.Female, 2);
        var result = new AnalysisResult(item.Id, 1, "Synthetic", withReference ? new(-2, 2) : null);
        var preview = AnalysisResultReferenceSelector.Preview(request, item, analysis,
            [new(analysis.Id, Sex.Female, 0, null, new(100, 200))], result);
        Assert.Equal(withReference ? new AnalysisResultReferenceDto("Applicable", "-2", "2") : new("NoReference"), preview);
    }

    private static (LaboratoryRequest, MedicalAnalysis, RequestItem) Fixture(Sex sex, int ageDays)
    {
        var registered = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var patient = new Patient("Synthetic", "Preview", DateOnly.FromDateTime(registered.UtcDateTime).AddDays(-ageDays), sex, phoneNumber: "TEST-ONLY");
        var request = LaboratoryRequest.Register("LAB-2024-000001", patient, "Synthetic", 0, registered);
        var analysis = new MedicalAnalysis("PREVIEW", "Synthetic", "unit", 1);
        return (request, analysis, new(request, analysis));
    }
}
