using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.Entity("Patients");
        b.Text(x => x.FirstName, 100);
        b.Text(x => x.LastName, 100);
        b.Text(x => x.NationalIdentityNumber, 50, false);
        b.Text(x => x.PhoneNumber, 30, false);
        b.Property(x => x.DateOfBirth).HasColumnType("date");
        b.HasIndex(x => x.NationalIdentityNumber).IsUnique().HasFilter("[NationalIdentityNumber] IS NOT NULL");
        b.HasIndex(x => x.PhoneNumber);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Patients_Sex", "[Sex] IN (0,1,2)");
            t.HasCheckConstraint("CK_Patients_Identity", "[NationalIdentityNumber] IS NOT NULL OR [PhoneNumber] IS NOT NULL");
            t.HasCheckConstraint("CK_Patients_DateOfBirth", "[DateOfBirth] > '0001-01-01' AND [DateOfBirth] <= CONVERT(date, SYSUTCDATETIME())");
        });
    }
}
