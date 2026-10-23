using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.Entity("AuditEntries");
        b.Text(x => x.Actor, 100);
        b.Text(x => x.FieldName, 100);
        b.Text(x => x.OldValue, 4000, false);
        b.Text(x => x.NewValue, 4000, false);
        b.Property(x => x.OccurredAt).HasColumnType("datetimeoffset(7)");
        b.HasIndex(x => new { x.LaboratoryRequestId, x.OccurredAt });
        b.HasOne<LaboratoryRequest>().WithMany().HasForeignKey(x => x.LaboratoryRequestId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RequestItem>().WithMany()
            .HasForeignKey(x => new { x.LaboratoryRequestId, x.RequestItemId })
            .HasPrincipalKey(x => new { x.LaboratoryRequestId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AuditEntries_Action", "[Action] IN (0,1,2,3,4)");
            t.HasCheckConstraint("CK_AuditEntries_Values", "[OldValue] IS NOT NULL OR [NewValue] IS NOT NULL");
        });
    }
}
