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
        Assert.Equal(2, assembly.Migrations.Count);
        var operations = assembly.Migrations.OrderBy(x => x.Key)
            .SelectMany(x => assembly.CreateMigration(x.Value, db.Database.ProviderName!).UpOperations).ToArray();
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(assembly.Migrations.Keys.Order(), (await db.Database.GetAppliedMigrationsAsync()).Order());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var expectedColumns = tables.SelectMany(t => t.Columns.Select(c =>
            $"{t.Name}|{c.Name}|{c.ColumnType}|{(c.IsNullable ? 1 : 0)}"));
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
