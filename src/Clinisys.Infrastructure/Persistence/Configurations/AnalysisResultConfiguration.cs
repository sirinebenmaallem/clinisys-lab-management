using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class AnalysisResultConfiguration : IEntityTypeConfiguration<AnalysisResult>
{
    public void Configure(EntityTypeBuilder<AnalysisResult> b)
    {
        b.Entity("AnalysisResults");
        b.Text(x => x.EnteredBy, 100);
        b.Text(x => x.ValidatedBy, 100, false);
        b.Property(x => x.Value).HasPrecision(18, 6);
        b.Property(x => x.ReferenceLowerBoundSnapshot).HasPrecision(18, 6);
        b.Property(x => x.ReferenceUpperBoundSnapshot).HasPrecision(18, 6);
        b.Property(x => x.EnteredAt).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.ValidatedAt).HasColumnType("datetimeoffset(7)");
        b.HasOne<RequestItem>().WithOne().HasForeignKey<AnalysisResult>(x => x.RequestItemId).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AnalysisResults_ReferenceAndAbnormality", "([ReferenceLowerBoundSnapshot] IS NULL AND [ReferenceUpperBoundSnapshot] IS NULL AND [Abnormality] = 0) OR ([ReferenceLowerBoundSnapshot] IS NOT NULL AND [ReferenceUpperBoundSnapshot] IS NOT NULL AND [ReferenceLowerBoundSnapshot] <= [ReferenceUpperBoundSnapshot] AND (([Abnormality] = 1 AND [Value] BETWEEN [ReferenceLowerBoundSnapshot] AND [ReferenceUpperBoundSnapshot]) OR ([Abnormality] = 2 AND [Value] < [ReferenceLowerBoundSnapshot]) OR ([Abnormality] = 3 AND [Value] > [ReferenceUpperBoundSnapshot])))");
            t.HasCheckConstraint("CK_AnalysisResults_Validation", "([ValidatedAt] IS NULL AND [ValidatedBy] IS NULL) OR ([ValidatedAt] IS NOT NULL AND [ValidatedBy] IS NOT NULL AND [ValidatedAt] >= [EnteredAt])");
        });
    }
}
