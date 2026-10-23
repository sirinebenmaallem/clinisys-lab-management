using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class RequestItemConfiguration : IEntityTypeConfiguration<RequestItem>
{
    public void Configure(EntityTypeBuilder<RequestItem> b)
    {
        b.Entity("RequestItems");
        b.Text(x => x.AnalysisCodeSnapshot, 30);
        b.Text(x => x.AnalysisNameSnapshot, 200);
        b.Text(x => x.UnitSnapshot, 30);
        b.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 2);
        b.Property(x => x.DiscountPercentSnapshot).HasPrecision(5, 2);
        b.Ignore(x => x.NetPrice);
        b.HasIndex(x => new { x.LaboratoryRequestId, x.MedicalAnalysisId }).IsUnique();
        b.HasAlternateKey(x => new { x.LaboratoryRequestId, x.Id });
        b.HasOne<LaboratoryRequest>().WithMany().HasForeignKey(x => x.LaboratoryRequestId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<MedicalAnalysis>().WithMany().HasForeignKey(x => x.MedicalAnalysisId).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_RequestItems_Price", "[UnitPriceSnapshot] >= 0");
            t.HasCheckConstraint("CK_RequestItems_Discount", "[DiscountPercentSnapshot] BETWEEN 0 AND 100");
        });
    }
}
