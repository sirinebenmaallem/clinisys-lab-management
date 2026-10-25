using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Domain;

// Pure Domain tests: no context, API host or database connection.
public sealed class AnalysisResultEntryTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CorrectionTime = InitialTime.AddMinutes(5);

    [Theory]
    [InlineData(-3, Abnormality.Low)]
    [InlineData(-2, Abnormality.Normal)]
    [InlineData(0, Abnormality.Normal)]
    [InlineData(2, Abnormality.Normal)]
    [InlineData(3, Abnormality.High)]
    public void ExplicitCreation_CapturesUtcActorAndInclusiveReference(decimal value, Abnormality expected)
    {
        var itemId = Guid.NewGuid();
        var result = new AnalysisResult(itemId, value, " synthetic-actor ", new(-2, 2), InitialTime);
        Assert.Equal(itemId, result.RequestItemId);
        Assert.Equal(value, result.Value);
        Assert.Equal("synthetic-actor", result.EnteredBy);
        Assert.Equal(InitialTime, result.EnteredAt);
        Assert.Equal(-2, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal(expected, result.Abnormality);
        Assert.Null(result.ValidatedAt);
        Assert.Null(result.ValidatedBy);
    }

    [Fact]
    public void ExistingConstructor_RemainsCompatibleAndUsesUtc()
    {
        var before = DateTimeOffset.UtcNow;
        var result = new AnalysisResult(Guid.NewGuid(), 0, "synthetic-actor");
        Assert.InRange(result.EnteredAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, result.EnteredAt.Offset);
        Assert.Equal(Abnormality.NotEvaluated, result.Abnormality);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.123456")]
    [InlineData("999999999999.999999")]
    [InlineData("-999999999999.999999")]
    public void Measurements_AcceptZeroNegativeSixDecimalsAndLimits(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = NewResult();
        result.UpdateValue(value, "corrector", CorrectionTime);
        Assert.Equal(value, result.Value);
        Assert.Equal(value, new AnalysisResult(Guid.NewGuid(), value, "actor", null, InitialTime).Value);
    }

    [Theory]
    [InlineData(-3, Abnormality.Low)]
    [InlineData(-2, Abnormality.Normal)]
    [InlineData(0, Abnormality.Normal)]
    [InlineData(2, Abnormality.Normal)]
    [InlineData(3, Abnormality.High)]
    public void Correction_ReevaluatesAndPreservesIdentityAndSnapshots(decimal value, Abnormality expected)
    {
        var result = NewResult();
        var id = result.Id;
        var itemId = result.RequestItemId;
        var version = result.Version.ToArray();
        result.UpdateValue(value, " corrector ", CorrectionTime);
        Assert.Equal(value, result.Value);
        Assert.Equal(expected, result.Abnormality);
        Assert.Equal(id, result.Id);
        Assert.Equal(itemId, result.RequestItemId);
        Assert.Equal(version, result.Version);
        Assert.Equal(-2, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal("corrector", result.EnteredBy);
        Assert.Equal(CorrectionTime, result.EnteredAt);
        Assert.Null(result.ValidatedAt);
        Assert.Null(result.ValidatedBy);
    }

    [Fact]
    public void WithoutReference_CorrectionRemainsNotEvaluated()
    {
        var result = new AnalysisResult(Guid.NewGuid(), 1, "actor", null, InitialTime);
        result.UpdateValue(-2.123456m, "corrector", CorrectionTime);
        Assert.Null(result.ReferenceLowerBoundSnapshot);
        Assert.Null(result.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.NotEvaluated, result.Abnormality);
        Assert.Equal(CorrectionTime, result.EnteredAt);
    }

    [Fact]
    public void EqualBounds_AreInclusiveOnCreationAndCorrection()
    {
        var result = new AnalysisResult(Guid.NewGuid(), -5, "actor", new(-5, -5), InitialTime);
        Assert.Equal(Abnormality.Normal, result.Abnormality);
        result.UpdateValue(-6, "actor", CorrectionTime);
        Assert.Equal(Abnormality.Low, result.Abnormality);
        result.UpdateValue(-5, "actor", CorrectionTime);
        Assert.Equal(Abnormality.Normal, result.Abnormality);
    }

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("1000000000000")]
    [InlineData("-1000000000000")]
    public void InvalidMeasurement_IsRejectedOnCreationAndCorrectionWithoutMutation(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisResult(Guid.NewGuid(), value, "actor", null, InitialTime));
        var result = NewResult();
        AssertUnchanged(result, () => result.UpdateValue(value, "corrector", CorrectionTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingActor_IsRejectedWithoutMutation(string? actor)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AnalysisResult(Guid.NewGuid(), 1, actor!, null, InitialTime));
        var result = NewResult();
        AssertUnchanged(result, () => result.UpdateValue(3, actor!, CorrectionTime));
    }

    [Fact]
    public void LongActor_IsRejectedWithoutMutation()
    {
        var actor = new string('x', 101);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisResult(Guid.NewGuid(), 1, actor, null, InitialTime));
        var result = NewResult();
        AssertUnchanged(result, () => result.UpdateValue(3, actor, CorrectionTime));
    }

    [Fact]
    public void NonUtcInstant_IsRejectedWithoutMutation()
    {
        var nonUtc = CorrectionTime.ToOffset(TimeSpan.FromHours(1));
        Assert.Throws<ArgumentException>(() => new AnalysisResult(Guid.NewGuid(), 1, "actor", null, nonUtc));
        var result = NewResult();
        AssertUnchanged(result, () => result.UpdateValue(3, "corrector", nonUtc));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnyValidationMetadata_BlocksCorrectionWithoutMutation(bool date, bool actor)
    {
        var result = NewResult();
        // Simulate rehydrated states, including inconsistent partial metadata,
        // without introducing a public validation operation in this lot.
        if (date) Set(result, nameof(result.ValidatedAt), InitialTime.AddMinutes(1));
        if (actor) Set(result, nameof(result.ValidatedBy), "synthetic-validator");
        var before = Snapshot(result);
        Assert.Throws<InvalidOperationException>(() => result.UpdateValue(3, "corrector", CorrectionTime));
        Assert.Equal(before, Snapshot(result));
    }

    private static AnalysisResult NewResult() => new(Guid.NewGuid(), 1, "actor", new(-2, 2), InitialTime);
    private static object[] Snapshot(AnalysisResult result) =>
        [result.Id, result.RequestItemId, result.Value, result.ReferenceLowerBoundSnapshot!, result.ReferenceUpperBoundSnapshot!,
            result.Abnormality, result.EnteredAt, result.EnteredBy, result.ValidatedAt!, result.ValidatedBy!, Convert.ToBase64String(result.Version)];
    private static void AssertUnchanged(AnalysisResult result, Action correction)
    {
        var before = Snapshot(result);
        Assert.ThrowsAny<ArgumentException>(correction);
        Assert.Equal(before, Snapshot(result));
    }
    private static void Set(object entity, string name, object value) => entity.GetType().GetProperty(name)!.SetValue(entity, value);
}
