using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class AnalysisPanelConfiguration : IEntityTypeConfiguration<AnalysisPanel>
{
    public void Configure(EntityTypeBuilder<AnalysisPanel> b)
    {
        b.Entity("AnalysisPanels");
        b.Text(x => x.Code, 30);
        b.Text(x => x.Name, 200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}
