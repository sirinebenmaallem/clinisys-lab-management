using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class LaboratoryRequestCounterConfiguration : IEntityTypeConfiguration<LaboratoryRequestCounter>
{
    public void Configure(EntityTypeBuilder<LaboratoryRequestCounter> b)
    {
        b.ToTable("LaboratoryRequestCounters", table =>
        {
            table.HasCheckConstraint("CK_LaboratoryRequestCounters_Year", "[Year] BETWEEN 1 AND 9999");
            table.HasCheckConstraint("CK_LaboratoryRequestCounters_LastValue", "[LastValue] BETWEEN 1 AND 999999");
        });
        b.HasKey(x => x.Year);
        b.Property(x => x.Year).ValueGeneratedNever();
    }
}
