using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Clinisys.Tests.Persistence;

public sealed class SqlServerSchemaTests
{
    [SqlServerFact]
    public async Task ActualSchema_MatchesApprovedMigrationChainColumnsKeysRelationshipsAndChecks()
    {
        await using var db = SqlServerTestDatabase.CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(new[] { "20261023192710_InitialCreate", "20261024175903_AddLaboratoryRequestCounters",
            "20261025202446_AddAuthenticationIdentity", "20261025225017_FixAuthenticationUserNameCheck" }, assembly.Migrations.Keys.Order(StringComparer.Ordinal));
        var operations = assembly.Migrations.OrderBy(x => x.Key)
            .SelectMany(x => assembly.CreateMigration(x.Value, db.Database.ProviderName!).UpOperations).ToArray();
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "AuthRoleClaims", "AuthRoles", "AuthUserClaims", "AuthUserLogins", "AuthUserRoles", "AuthUserTokens", "AuthUsers" },
            tables.Where(t => t.Name.StartsWith("Auth", StringComparison.Ordinal)).Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tables.Where(t => t.Name.StartsWith("Auth", StringComparison.Ordinal)), t => Assert.Equal("dbo", t.Schema));
        Assert.Equal(assembly.Migrations.Keys.Order(), (await db.Database.GetAppliedMigrationsAsync()).Order());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var expectedColumns = tables.SelectMany(t => t.Columns.Select(c =>
            $"{t.Name}|{c.Name}|{(c.ColumnType == "datetimeoffset" ? "datetimeoffset(7)" : c.ColumnType)}|{(c.IsNullable ? 1 : 0)}"));
        var actualColumns = await ReadRows(db, """
            SELECT t.name, c.name,
                CASE WHEN ty.name = 'timestamp' THEN 'rowversion'
                     WHEN ty.name IN ('nvarchar','varchar') THEN CONCAT(ty.name,'(',
                         CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10),c.max_length / CASE WHEN ty.name='nvarchar' THEN 2 ELSE 1 END) END,')')
                     WHEN ty.name = 'decimal' THEN CONCAT('decimal(',c.precision,',',c.scale,')')
                     WHEN ty.name = 'datetimeoffset' THEN CONCAT('datetimeoffset(',c.scale,')')
                     ELSE ty.name END,
                CONVERT(int,c.is_nullable)
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            WHERE t.is_ms_shipped=0 AND t.name <> '__EFMigrationsHistory';
            """);
        EqualRows(expectedColumns, actualColumns);

        var expectedKeys = tables.Select(t => $"{t.Name}|{t.PrimaryKey!.Name}|{string.Join(',', t.PrimaryKey.Columns)}|")
            .Concat(tables.SelectMany(t => t.UniqueConstraints.Select(k => $"{t.Name}|{k.Name}|{string.Join(',', k.Columns)}|")))
            .Concat(operations.OfType<CreateIndexOperation>().Where(i => i.IsUnique)
                .Select(i => $"{i.Table}|{i.Name}|{string.Join(',', i.Columns)}|{Normalize(i.Filter ?? "")}"));
        var actualKeys = await ReadRows(db, """
            SELECT t.name, i.name, STRING_AGG(CONVERT(nvarchar(max),c.name),',') WITHIN GROUP (ORDER BY ic.key_ordinal),
                REPLACE(REPLACE(REPLACE(REPLACE(UPPER(COALESCE(i.filter_definition,'')),' ',''),'(',''),')',''),CHAR(13)+CHAR(10),'')
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.is_unique=1 AND ic.key_ordinal>0 AND t.name <> '__EFMigrationsHistory'
            GROUP BY t.name,i.name,i.filter_definition;
            """);
        EqualRows(expectedKeys, actualKeys);

        // Include nonunique Identity FK/email indexes, not only uniqueness constraints.
        var expectedIdentityIndexes = operations.OfType<CreateIndexOperation>()
            .Where(i => i.Table.StartsWith("Auth", StringComparison.Ordinal))
            .Select(i => $"{i.Table}|{i.Name}|{string.Join(',', i.Columns)}|{(i.IsUnique ? 1 : 0)}|{Normalize(i.Filter ?? "")}|0");
        var actualIdentityIndexes = await ReadRows(db, """
            SELECT t.name,i.name,STRING_AGG(CONVERT(nvarchar(max),c.name),',') WITHIN GROUP (ORDER BY ic.key_ordinal),
                CONVERT(int,i.is_unique),
                REPLACE(REPLACE(REPLACE(REPLACE(UPPER(COALESCE(i.filter_definition,'')),' ',''),'(',''),')',''),CHAR(13)+CHAR(10),''),
                CONVERT(int,i.is_disabled)
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name LIKE 'Auth%' AND i.is_primary_key=0 AND ic.key_ordinal>0
            GROUP BY t.name,i.name,i.is_unique,i.filter_definition,i.is_disabled;
            """);
        EqualRows(expectedIdentityIndexes, actualIdentityIndexes);

        EqualRows(new[] { "AuthRoles|dbo", "AuthRoleClaims|dbo", "AuthUsers|dbo", "AuthUserClaims|dbo",
            "AuthUserLogins|dbo", "AuthUserRoles|dbo", "AuthUserTokens|dbo" }, await ReadRows(db, """
            SELECT name,SCHEMA_NAME(schema_id) FROM sys.tables WHERE name LIKE 'Auth%';
            """));
        var defaults = await ReadRows(db, """
            SELECT c.name,d.definition
            FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
            WHERE c.object_id=OBJECT_ID(N'dbo.AuthUsers') AND c.name IN ('IsActive','LockoutEnabled');
            """);
        EqualRows(new[] { "IsActive|0", "LockoutEnabled|1" }, defaults.Select(row =>
        {
            var separator = row.IndexOf('|');
            // SQL Server can store EF's bit literal CAST as an equivalent CONVERT.
            // Accept only these literal forms; do not execute arbitrary default SQL.
            var expression = Normalize(row[(separator + 1)..]).Replace("[", "").Replace("]", "");
            var bit = expression switch
            {
                "0" or "CAST0ASBIT" or "CONVERTBIT,0" => "0",
                "1" or "CAST1ASBIT" or "CONVERTBIT,1" => "1",
                _ => expression
            };
            return row[..separator] + "|" + bit;
        }));
        EqualRows(new[] { "AuthRoles|Name|Latin1_General_100_BIN2", "AuthRoles|NormalizedName|Latin1_General_100_BIN2",
            "AuthUsers|UserName|Latin1_General_100_BIN2", "AuthUsers|NormalizedUserName|Latin1_General_100_BIN2" }, await ReadRows(db, """
            SELECT t.name,c.name,c.collation_name FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            WHERE (t.name='AuthUsers' AND c.name IN ('UserName','NormalizedUserName'))
               OR (t.name='AuthRoles' AND c.name IN ('Name','NormalizedName'));
            """));

        var expectedForeignKeys = tables.SelectMany(t => t.ForeignKeys.Select(fk =>
            $"{t.Name}|{fk.Name}|{string.Join(',', fk.Columns)}|{fk.PrincipalTable}|{string.Join(',', fk.PrincipalColumns!)}|0|0|0|0"));
        var actualForeignKeys = await ReadRows(db, """
            SELECT OBJECT_NAME(f.parent_object_id),f.name,
                STRING_AGG(CONVERT(nvarchar(max),COL_NAME(fc.parent_object_id,fc.parent_column_id)),',') WITHIN GROUP (ORDER BY fc.constraint_column_id),
                OBJECT_NAME(f.referenced_object_id),
                STRING_AGG(CONVERT(nvarchar(max),COL_NAME(fc.referenced_object_id,fc.referenced_column_id)),',') WITHIN GROUP (ORDER BY fc.constraint_column_id),
                f.delete_referential_action,f.update_referential_action,CONVERT(int,f.is_disabled),CONVERT(int,f.is_not_trusted)
            FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            GROUP BY f.parent_object_id,f.name,f.referenced_object_id,f.delete_referential_action,f.update_referential_action,f.is_disabled,f.is_not_trusted;
            """);
        EqualRows(expectedForeignKeys, actualForeignKeys);

        var expectedChecks = tables.SelectMany(t => t.CheckConstraints.Select(c => $"{t.Name}|{c.Name}|0|0"));
        var actualChecks = await ReadRows(db, """
            SELECT OBJECT_NAME(parent_object_id),name,CONVERT(int,is_disabled),CONVERT(int,is_not_trusted)
            FROM sys.check_constraints;
            """);
        EqualRows(expectedChecks, actualChecks);
    }

    [SqlServerFact]
    public async Task RestrictedSession_CannotAccessDevelopmentDatabaseOrEscalatePrivileges()
    {
        await using var db = SqlServerTestDatabase.CreateContext();
        await db.Database.OpenConnectionAsync();
        var identity = await ReadRows(db, """
            SELECT DB_NAME(),USER_NAME(),HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),
                HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER'),IS_ROLEMEMBER('db_owner');
            """);
        Assert.Equal(new[] { "ClinisysTestDb|ClinisysIntegrationTests|0|0|0" }, identity);

        // Read-only cross-database probes verify the permission barrier without any DML.
        var exception = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            ReadRows(db, "SELECT COUNT(*) FROM ClinisysDb.sys.tables;"));
        Assert.Equal(916, exception.Number);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync("REVERT;"));
        Assert.Equal(new[] { "ClinisysIntegrationTests" }, await ReadRows(db, "SELECT USER_NAME();"));

        // A newly opened physical session must be restricted again too.
        await db.Database.CloseConnectionAsync();
        await db.Database.OpenConnectionAsync();
        Assert.Equal(new[] { "ClinisysIntegrationTests" }, await ReadRows(db, "SELECT USER_NAME();"));
    }

    private static async Task<string[]> ReadRows(ClinisysDbContext db, string sql)
    {
        // Keep the same connection so the session restriction is maintained between probes.
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString())));
        return rows.ToArray();
    }

    private static string Normalize(string value) => value.Replace(" ", "").Replace("(", "").Replace(")", "").ToUpperInvariant();
    private static void EqualRows(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedRows = expected.Order(StringComparer.Ordinal).ToArray();
        var actualRows = actual.Order(StringComparer.Ordinal).ToArray();
        Assert.True(expectedRows.SequenceEqual(actualRows),
            $"Missing: {string.Join(';', expectedRows.Except(actualRows))}. Unexpected: {string.Join(';', actualRows.Except(expectedRows))}.");
    }
}
