using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class PanelAnalysisConfiguration : IEntityTypeConfiguration<PanelAnalysis>
{
    public void Configure(EntityTypeBuilder<PanelAnalysis> b)
    {
        b.Entity("PanelAnalyses");
        b.HasIndex(x => new { x.AnalysisPanelId, x.MedicalAnalysisId }).IsUnique();
        b.HasOne<AnalysisPanel>().WithMany().HasForeignKey(x => x.AnalysisPanelId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<MedicalAnalysis>().WithMany().HasForeignKey(x => x.MedicalAnalysisId).OnDelete(DeleteBehavior.NoAction);
    }
}
