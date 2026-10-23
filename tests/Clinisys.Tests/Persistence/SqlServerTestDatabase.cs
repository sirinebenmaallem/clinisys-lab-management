using System.Data.Common;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinisys.Tests.Persistence;

internal static class SqlServerTestDatabase
{
    internal const string ConnectionVariable = "CLINISYS_SQL_TEST_CONNECTION";
    internal const string DatabaseName = "ClinisysTestDb";
    internal const string PrincipalName = "ClinisysIntegrationTests";

    internal static string ValidateConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.InitialCatalog != DatabaseName || builder.DataSource != ".\\SQLEXPRESS"
            || !builder.IntegratedSecurity || !string.IsNullOrEmpty(builder.AttachDBFilename)
            || !string.IsNullOrEmpty(builder.FailoverPartner) || builder.UserInstance
            || !string.IsNullOrEmpty(builder.UserID) || !string.IsNullOrEmpty(builder.Password))
            throw new InvalidOperationException("SQL tests only allow .\\SQLEXPRESS / ClinisysTestDb with Windows authentication and no file attachment or failover.");

        // Every physical session must enter the restricted execution context afresh.
        builder.Pooling = false;
        builder.ConnectRetryCount = 0;
        builder.Enlist = false;
        builder.ApplicationName = "Clinisys isolated integration tests";
        return builder.ConnectionString;
    }

    internal static ClinisysDbContext CreateContext()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to the isolated test database.");
        return new ClinisysDbContext(new DbContextOptionsBuilder<ClinisysDbContext>()
            .UseSqlServer(ValidateConnectionString(connectionString))
            .AddInterceptors(new RestrictedTestConnectionInterceptor()).Options);
    }

    private sealed class RestrictedTestConnectionInterceptor : DbConnectionInterceptor
    {
        // Run before EF can execute any test query, including on a reopened connection.
        private const string RestrictSessionSql = """
            IF DB_NAME() <> N'ClinisysTestDb'
                THROW 51010, 'Unexpected integration test database.', 1;
            IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID()
                AND (is_trustworthy_on = 1 OR is_db_chaining_on = 1))
                THROW 51011, 'Unsafe integration database security options.', 1;
            EXECUTE AS USER = N'ClinisysIntegrationTests' WITH NO REVERT;
            IF USER_NAME() <> N'ClinisysIntegrationTests'
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'IMPERSONATE ANY USER') = 1
                THROW 51012, 'Integration test principal has unsafe permissions.', 1;
            """;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = RestrictSessionSql;
            try { command.ExecuteNonQuery(); }
            catch { connection.Close(); throw; }
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = RestrictSessionSql;
            try { await command.ExecuteNonQueryAsync(cancellationToken); }
            catch { await connection.CloseAsync(); throw; }
        }
    }
}
