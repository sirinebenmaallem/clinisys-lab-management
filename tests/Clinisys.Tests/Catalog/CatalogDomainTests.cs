using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Catalog;

public sealed class CatalogDomainTests
{
    [Fact]
    public void InvalidAnalysisUpdate_LeavesAllOriginalFieldsIntact()
    {
        var analysis = new MedicalAnalysis("ORIGINAL", "Original", "unit", 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.Update("NEW", "New", "changed", -1));
        Assert.Equal("ORIGINAL", analysis.Code);
        Assert.Equal("Original", analysis.Name);
        Assert.Equal("unit", analysis.Unit);
        Assert.Equal(10, analysis.Price);
        analysis.Deactivate();
        analysis.Update(" edited ", "Updated", "unit", 5);
        Assert.Equal("EDITED", analysis.Code);
        Assert.False(analysis.IsActive);
    }

    [Fact]
    public void PanelUpdateAndDeactivation_PreserveIdentityAndRejectBlankValues()
    {
        var panel = new AnalysisPanel("ORIGINAL", "Original");
        var id = panel.Id;
        Assert.Throws<ArgumentException>(() => panel.Update("NEW", " "));
        Assert.Equal("ORIGINAL", panel.Code);
        panel.Update(" changed ", "Changed");
        panel.Deactivate();
        Assert.Equal(id, panel.Id);
        Assert.Equal("CHANGED", panel.Code);
        Assert.False(panel.IsActive);
    }

    [Theory]
    [InlineData(0, 10, 10, 20, false)]
    [InlineData(0, 10, 9, 20, true)]
    [InlineData(10, 20, 0, 11, true)]
    [InlineData(10, null, 0, 10, false)]
    [InlineData(10, null, 20, null, true)]
    [InlineData(0, null, 0, null, true)]
    public void AgeOverlap_UsesHalfOpenIntervals(int min, int? max, int otherMin, int? otherMax, bool expected)
    {
        var analysisId = Guid.NewGuid();
        var first = new ReferenceRange(analysisId, Sex.Female, min, max, new ReferenceInterval(0, 1));
        var second = new ReferenceRange(analysisId, Sex.Female, otherMin, otherMax, new ReferenceInterval(2, 3));
        Assert.Equal(expected, first.Overlaps(second));
        Assert.Equal(expected, second.Overlaps(first));
        Assert.False(first.Overlaps(new ReferenceRange(analysisId, Sex.Male, min, max, new ReferenceInterval(0, 1))));
        Assert.False(first.Overlaps(new ReferenceRange(Guid.NewGuid(), Sex.Female, min, max, new ReferenceInterval(0, 1))));
    }

    [Fact]
    public void InvalidRangeUpdate_DoesNotPartiallyChangeTheRange()
    {
        var range = new ReferenceRange(Guid.NewGuid(), Sex.Female, 0, 10, new ReferenceInterval(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Update(Sex.Male, 20, 10, new ReferenceInterval(0, 1)));
        Assert.Equal(Sex.Female, range.Sex);
        Assert.Equal(0, range.MinimumAgeDays);
        Assert.Equal(10, range.MaximumAgeDays);
    }
}
