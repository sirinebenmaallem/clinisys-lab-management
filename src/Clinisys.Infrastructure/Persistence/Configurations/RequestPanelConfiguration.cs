using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class RequestPanelConfiguration : IEntityTypeConfiguration<RequestPanel>
{
    public void Configure(EntityTypeBuilder<RequestPanel> b)
    {
        b.Entity("RequestPanels");
        b.Text(x => x.PanelCodeSnapshot, 30);
        b.Text(x => x.PanelNameSnapshot, 200);
        b.HasIndex(x => new { x.LaboratoryRequestId, x.AnalysisPanelId }).IsUnique();
        b.HasOne<LaboratoryRequest>().WithMany().HasForeignKey(x => x.LaboratoryRequestId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AnalysisPanel>().WithMany().HasForeignKey(x => x.AnalysisPanelId).OnDelete(DeleteBehavior.NoAction);
    }
}
