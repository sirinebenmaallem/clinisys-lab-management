using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class ReferenceRangeConfiguration : IEntityTypeConfiguration<ReferenceRange>
{
    public void Configure(EntityTypeBuilder<ReferenceRange> b)
    {
        b.Entity("ReferenceRanges");
        b.Property(x => x.LowerBound).HasPrecision(18, 6);
        b.Property(x => x.UpperBound).HasPrecision(18, 6);
        b.HasOne<MedicalAnalysis>().WithMany().HasForeignKey(x => x.MedicalAnalysisId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.MedicalAnalysisId, x.Sex, x.MinimumAgeDays, x.MaximumAgeDays }).IsUnique().HasFilter(null);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReferenceRanges_Sex", "[Sex] IN (0,1,2)");
            t.HasCheckConstraint("CK_ReferenceRanges_Age", "[MinimumAgeDays] >= 0 AND ([MaximumAgeDays] IS NULL OR [MaximumAgeDays] > [MinimumAgeDays])");
            t.HasCheckConstraint("CK_ReferenceRanges_Bounds", "[LowerBound] <= [UpperBound]");
        });
    }
}
