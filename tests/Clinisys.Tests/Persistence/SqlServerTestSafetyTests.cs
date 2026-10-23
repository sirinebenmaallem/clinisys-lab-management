using Microsoft.Data.SqlClient;

namespace Clinisys.Tests.Persistence;

public sealed class SqlServerTestSafetyTests
{
    [Theory]
    [InlineData(".\\SQLEXPRESS", "ClinisysDb")]
    [InlineData(".\\SQLEXPRESS", "master")]
    [InlineData(".\\SQLEXPRESS", "UnrelatedDatabase")]
    [InlineData(".\\SQLEXPRESS", "ClinisysTestDbCopy")]
    [InlineData("remote\\SQLEXPRESS", "ClinisysTestDb")]
    public void Guard_RejectsUnsafeTargetsBeforeOpeningConnection(string server, string database) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerTestDatabase.ValidateConnectionString(
            new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = database, IntegratedSecurity = true }.ConnectionString));

    [Theory]
    [InlineData("Integrated Security=False;User ID=test;Password=test-only")]
    [InlineData("AttachDbFilename=C:\\test-only.mdf")]
    [InlineData("Failover Partner=remote")]
    [InlineData("User Instance=True")]
    public void Guard_RejectsAlternativeAuthenticationAndRouting(string options) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerTestDatabase.ValidateConnectionString(
            "Server=.\\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;" + options));

    [Fact]
    public void Guard_AcceptsOnlyTheTestDatabaseAndDisablesSessionReuse()
    {
        var builder = new SqlConnectionStringBuilder(SqlServerTestDatabase.ValidateConnectionString(
            "Server=.\\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Pooling=True;ConnectRetryCount=3;Enlist=True"));
        Assert.Equal("ClinisysTestDb", builder.InitialCatalog);
        Assert.False(builder.Pooling);
        Assert.False(builder.Enlist);
        Assert.Equal(0, builder.ConnectRetryCount);
    }
}
