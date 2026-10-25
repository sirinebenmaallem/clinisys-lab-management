using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Authentication;

internal static class IdentityModelConfiguration
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    internal static void Configure(ModelBuilder model)
    {
        // Pin the final relational shape even when no Identity service provider
        // exists (design-time factory, direct and independent test contexts).
        model.Ignore<IdentityUserPasskey<Guid>>();
        model.Entity<ApplicationUser>(b =>
        {
            b.ToTable("AuthUsers", "dbo", t =>
            {
                t.HasCheckConstraint("CK_AuthUsers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                t.HasCheckConstraint("CK_AuthUsers_UserName", "DATALENGTH([UserName]) BETWEEN 6 AND 128 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND LEFT([UserName],1) LIKE N'[a-zA-Z0-9]' AND [UserName] NOT LIKE N'%[^-a-zA-Z0-9._]%'");
                t.HasCheckConstraint("CK_AuthUsers_NormalizedUserName", "[NormalizedUserName] = UPPER([UserName]) AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName])");
                t.HasCheckConstraint("CK_AuthUsers_AccessFailedCount", "[AccessFailedCount] >= 0");
            });
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserName).HasMaxLength(64).IsRequired().UseCollation(BinaryCollation);
            b.Property(x => x.NormalizedUserName).HasMaxLength(64).IsRequired().UseCollation(BinaryCollation);
            b.HasIndex(x => x.NormalizedUserName).HasDatabaseName("UX_AuthUsers_NormalizedUserName").IsUnique().HasFilter(null);
            b.HasIndex(x => x.NormalizedEmail).HasDatabaseName("IX_AuthUsers_NormalizedEmail");
            b.Property(x => x.PhoneNumber).HasMaxLength(256);
            b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            b.Property(x => x.ConcurrencyStamp).HasMaxLength(64).IsRequired().IsConcurrencyToken();
            b.Property(x => x.IsActive).HasDefaultValue(false).IsRequired();
            b.Property(x => x.LockoutEnabled).HasDefaultValue(true).IsRequired();
        });
        model.Entity<ApplicationRole>(b =>
        {
            b.ToTable("AuthRoles", "dbo", t =>
            {
                t.HasCheckConstraint("CK_AuthRoles_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                t.HasCheckConstraint("CK_AuthRoles_Name", "([Name] = N'Secretary' AND DATALENGTH([Name]) = 18) OR ([Name] = N'Technician' AND DATALENGTH([Name]) = 20) OR ([Name] = N'Biologist' AND DATALENGTH([Name]) = 18)");
                t.HasCheckConstraint("CK_AuthRoles_NormalizedName", "[NormalizedName] = UPPER([Name]) AND DATALENGTH([NormalizedName]) = DATALENGTH([Name])");
            });
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(32).IsRequired().UseCollation(BinaryCollation);
            b.Property(x => x.NormalizedName).HasMaxLength(32).IsRequired().UseCollation(BinaryCollation);
            b.Property(x => x.ConcurrencyStamp).HasMaxLength(64).IsRequired().IsConcurrencyToken();
            b.HasIndex(x => x.NormalizedName).HasDatabaseName("UX_AuthRoles_NormalizedName").IsUnique().HasFilter(null);
        });
        model.Entity<IdentityUserRole<Guid>>().ToTable("AuthUserRoles", "dbo");
        model.Entity<IdentityUserClaim<Guid>>().ToTable("AuthUserClaims", "dbo");
        model.Entity<IdentityRoleClaim<Guid>>().ToTable("AuthRoleClaims", "dbo");
        model.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("AuthUserLogins", "dbo");
            b.Property(x => x.LoginProvider).HasMaxLength(128);
            b.Property(x => x.ProviderKey).HasMaxLength(128);
        });
        model.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("AuthUserTokens", "dbo");
            b.Property(x => x.LoginProvider).HasMaxLength(128);
            b.Property(x => x.Name).HasMaxLength(128);
        });
        var identityTypes = new HashSet<Type>
        {
            typeof(ApplicationUser), typeof(ApplicationRole), typeof(IdentityUserRole<Guid>),
            typeof(IdentityUserClaim<Guid>), typeof(IdentityRoleClaim<Guid>),
            typeof(IdentityUserLogin<Guid>), typeof(IdentityUserToken<Guid>)
        };
        foreach (var type in model.Model.GetEntityTypes().Where(e => identityTypes.Contains(e.ClrType)))
            foreach (var fk in type.GetForeignKeys()) fk.DeleteBehavior = DeleteBehavior.NoAction;
    }
}
