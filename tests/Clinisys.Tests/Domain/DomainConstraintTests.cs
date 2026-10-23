using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Domain;

public sealed class DomainConstraintTests
{
    [Theory]
    [InlineData(0, Abnormality.Low)]
    [InlineData(1, Abnormality.Normal)]
    [InlineData(5, Abnormality.Normal)]
    [InlineData(10, Abnormality.Normal)]
    [InlineData(11, Abnormality.High)]
    public void ReferenceInterval_EvaluatesInclusiveBounds(decimal value, Abnormality expected) =>
        Assert.Equal(expected, new ReferenceInterval(1, 10).Evaluate(value));

    [Fact]
    public void ReferenceInterval_AllowsNegativeAndEqualBounds()
    {
        Assert.Equal(Abnormality.Normal, new ReferenceInterval(-5, -5).Evaluate(-5));
        Assert.Throws<ArgumentException>(() => new ReferenceInterval(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceInterval(0, 1.0000001m));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(10, 10)]
    [InlineData(10, 9)]
    public void ReferenceRange_RejectsInvalidAgeInterval(int minimum, int? maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReferenceRange(Guid.NewGuid(), Sex.Female, minimum, maximum, new ReferenceInterval(1, 2)));

    [Fact]
    public void ReferenceRange_UsesHalfOpenAgeIntervalAndExactSex()
    {
        var range = new ReferenceRange(Guid.NewGuid(), Sex.Female, 10, 20, new ReferenceInterval(1, 2));
        Assert.False(range.AppliesTo(9, Sex.Female));
        Assert.True(range.AppliesTo(10, Sex.Female));
        Assert.True(range.AppliesTo(19, Sex.Female));
        Assert.False(range.AppliesTo(20, Sex.Female));
        Assert.False(range.AppliesTo(15, Sex.Male));
        Assert.True(new ReferenceRange(Guid.NewGuid(), Sex.Unspecified, 0, null,
            new ReferenceInterval(0, 0)).AppliesTo(int.MaxValue, Sex.Unspecified));
    }

    [Fact]
    public void Constructors_RejectInvalidEnumValuesAndEmptyForeignKeys()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceRange(Guid.NewGuid(), (Sex)99, 0, null, new ReferenceInterval(0, 1)));
        Assert.Throws<ArgumentException>(() => new PanelAnalysis(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new AnalysisResult(Guid.Empty, 1, "test-actor"));
    }

    [Fact]
    public void FinancialValues_RejectNegativeExcessiveAndOverPreciseValues()
    {
        foreach (var price in new[] { -0.01m, 1.001m, 10000000000000000m })
            Assert.Throws<ArgumentOutOfRangeException>(() => new MedicalAnalysis("TEST", "Synthetic analysis", "unit", price));
        foreach (var discount in new[] { -0.01m, 100.01m, 1.001m })
            Assert.Throws<ArgumentOutOfRangeException>(() => new LaboratoryRequest("TEST", CreatePatient(), "test-physician", discount));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 0)]
    [InlineData(12.5, 8.75)]
    public void RequestItem_CapturesFinancialAndCatalogueSnapshots(decimal discount, decimal expectedNet)
    {
        var analysis = new MedicalAnalysis(" test ", "Synthetic analysis", "unit", 10);
        var request = new LaboratoryRequest(" test-request ", CreatePatient(), "test-physician", discount);
        var item = new RequestItem(request, analysis);
        Assert.Equal(analysis.Id, item.MedicalAnalysisId);
        Assert.Equal(request.Id, item.LaboratoryRequestId);
        Assert.Equal("TEST", item.AnalysisCodeSnapshot);
        Assert.Equal(analysis.Name, item.AnalysisNameSnapshot);
        Assert.Equal(analysis.Unit, item.UnitSnapshot);
        Assert.Equal(10, item.UnitPriceSnapshot);
        Assert.Equal(discount, item.DiscountPercentSnapshot);
        Assert.Equal(expectedNet, item.NetPrice);
    }

    [Fact]
    public void NetPrice_RoundsHalfCentsAwayFromZero()
    {
        var request = new LaboratoryRequest("TEST", CreatePatient(), "test-physician", 50);
        Assert.Equal(0.01m, new RequestItem(request, new MedicalAnalysis("TEST", "Synthetic", "unit", 0.01m)).NetPrice);
    }

    [Fact]
    public void NewRequest_IsRegisteredWithoutValidationOrDelivery()
    {
        var patient = CreatePatient();
        var request = new LaboratoryRequest("TEST", patient, "test-physician");
        Assert.Equal(RequestStatus.Registered, request.Status);
        Assert.Equal(patient.Id, request.PatientId);
        Assert.Equal(patient.DateOfBirth, request.PatientDateOfBirthSnapshot);
        Assert.Equal(patient.Sex, request.PatientSexSnapshot);
        Assert.Equal(TimeSpan.Zero, request.RegisteredAt.Offset);
        Assert.Null(request.ValidatedAt);
        Assert.Null(request.ValidatedBy);
        Assert.Null(request.DeliveredAt);
    }

    [Fact]
    public void Result_CapturesReferenceBoundsAndStartsUnvalidated()
    {
        var result = new AnalysisResult(Guid.NewGuid(), 3, "test-actor", new ReferenceInterval(1, 2));
        Assert.Equal(Abnormality.High, result.Abnormality);
        Assert.Equal(1, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Null(result.ValidatedAt);
        Assert.Null(result.ValidatedBy);
        Assert.Equal(Abnormality.NotEvaluated, new AnalysisResult(Guid.NewGuid(), 3, "test-actor").Abnormality);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisResult(Guid.NewGuid(), 0.0000001m, "test-actor"));
    }

    [Fact]
    public void RequiredIdentityAndAuditValues_AreValidated()
    {
        Assert.Throws<ArgumentException>(() => new Patient("Synthetic", "Test", new DateOnly(2000, 1, 1), Sex.Unspecified));
        Assert.Throws<ArgumentException>(() => new MedicalAnalysis(" ", "Synthetic", "unit", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Patient("Synthetic", "Test", DateOnly.MaxValue, Sex.Unspecified, phoneNumber: "TEST"));
        Assert.Throws<ArgumentException>(() => new AuditEntry(Guid.NewGuid(), null, AuditAction.Updated, "test-actor", "Value", null, null));
    }

    private static Patient CreatePatient() =>
        new("Synthetic", "Test", new DateOnly(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY");
}
