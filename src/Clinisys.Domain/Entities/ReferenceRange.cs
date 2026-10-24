using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class ReferenceRange : Entity
{
    private ReferenceRange() { }

    public ReferenceRange(Guid medicalAnalysisId, Sex sex, int minimumAgeDays, int? maximumAgeDays,
        ReferenceInterval interval)
    {
        MedicalAnalysisId = Guard.Id(medicalAnalysisId, nameof(medicalAnalysisId));
        Update(sex, minimumAgeDays, maximumAgeDays, interval);
    }

    public void Update(Sex sex, int minimumAgeDays, int? maximumAgeDays, ReferenceInterval interval)
    {
        var validSex = Guard.EnumValue(sex, nameof(sex));
        if (minimumAgeDays < 0 || maximumAgeDays <= minimumAgeDays)
            throw new ArgumentOutOfRangeException(nameof(minimumAgeDays), "Age interval must be non-empty and non-negative.");
        ArgumentNullException.ThrowIfNull(interval);
        Sex = validSex;
        MinimumAgeDays = minimumAgeDays;
        MaximumAgeDays = maximumAgeDays;
        LowerBound = interval.LowerBound;
        UpperBound = interval.UpperBound;
    }

    public bool Overlaps(ReferenceRange other) => MedicalAnalysisId == other.MedicalAnalysisId
        && Sex == other.Sex
        && (MaximumAgeDays is null || other.MinimumAgeDays < MaximumAgeDays)
        && (other.MaximumAgeDays is null || MinimumAgeDays < other.MaximumAgeDays);

    public Guid MedicalAnalysisId { get; private set; }
    public Sex Sex { get; private set; }
    public int MinimumAgeDays { get; private set; }
    public int? MaximumAgeDays { get; private set; }
    public decimal LowerBound { get; private set; }
    public decimal UpperBound { get; private set; }

    // Half-open age interval [minimum, maximum); null maximum means no upper age limit.
    public bool AppliesTo(int ageDays, Sex sex) => ageDays >= MinimumAgeDays
        && (MaximumAgeDays is null || ageDays < MaximumAgeDays) && Sex == sex;
}
