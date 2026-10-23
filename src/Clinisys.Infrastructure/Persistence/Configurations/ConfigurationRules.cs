using System.Linq.Expressions;
using Clinisys.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinisys.Infrastructure.Persistence.Configurations;

internal static class ConfigurationRules
{
    public static void Entity<T>(this EntityTypeBuilder<T> builder, string table) where T : Entity
    {
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).IsRowVersion();
    }

    public static void Text<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, string?>> property,
        int length, bool required = true) where T : class
    {
        builder.Property(property).HasMaxLength(length).IsRequired(required);
        var name = ((MemberExpression)property.Body).Member.Name;
        var table = builder.Metadata.GetTableName();
        builder.ToTable(t => t.HasCheckConstraint($"CK_{table}_{name}",
            required ? $"LEN(LTRIM(RTRIM([{name}]))) > 0" : $"[{name}] IS NULL OR LEN(LTRIM(RTRIM([{name}]))) > 0"));
    }
}
