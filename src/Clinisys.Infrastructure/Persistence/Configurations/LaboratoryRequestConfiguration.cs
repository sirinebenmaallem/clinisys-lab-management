using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class LaboratoryRequestConfiguration : IEntityTypeConfiguration<LaboratoryRequest>
{
    public void Configure(EntityTypeBuilder<LaboratoryRequest> b)
    {
        b.Entity("LaboratoryRequests");
        b.Text(x => x.Number, 30);
        b.Text(x => x.PrescribingPhysician, 200);
        b.Text(x => x.ValidatedBy, 100, false);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.RegisteredAt);
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.PatientDateOfBirthSnapshot).HasColumnType("date");
        b.Property(x => x.DiscountPercent).HasPrecision(5, 2);
        b.Property(x => x.RegisteredAt).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.ValidatedAt).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.DeliveredAt).HasColumnType("datetimeoffset(7)");
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_LaboratoryRequests_Status", "[Status] IN (0,1,2,3,4)");
            t.HasCheckConstraint("CK_LaboratoryRequests_Discount", "[DiscountPercent] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_LaboratoryRequests_PatientSnapshot", "[PatientSexSnapshot] IN (0,1,2) AND [PatientDateOfBirthSnapshot] > '0001-01-01' AND [PatientDateOfBirthSnapshot] <= CONVERT(date,[RegisteredAt])");
            t.HasCheckConstraint("CK_LaboratoryRequests_Validation", "([Status] IN (0,1,2) AND [ValidatedAt] IS NULL AND [ValidatedBy] IS NULL AND [DeliveredAt] IS NULL) OR ([Status] IN (3,4) AND [ValidatedAt] IS NOT NULL AND [ValidatedBy] IS NOT NULL AND [ValidatedAt] >= [RegisteredAt] AND (([Status] = 3 AND [DeliveredAt] IS NULL) OR ([Status] = 4 AND [DeliveredAt] IS NOT NULL AND [DeliveredAt] >= [ValidatedAt])))");
        });
    }
}
