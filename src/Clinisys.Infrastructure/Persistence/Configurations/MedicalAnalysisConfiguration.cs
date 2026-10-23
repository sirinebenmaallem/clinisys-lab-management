using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class MedicalAnalysisConfiguration : IEntityTypeConfiguration<MedicalAnalysis>
{
    public void Configure(EntityTypeBuilder<MedicalAnalysis> b)
    {
        b.Entity("MedicalAnalyses");
        b.Text(x => x.Code, 30);
        b.Text(x => x.Name, 200);
        b.Text(x => x.Unit, 30);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Price).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_MedicalAnalyses_Price", "[Price] >= 0"));
    }
}
