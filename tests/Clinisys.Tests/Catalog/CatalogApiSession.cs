using Clinisys.Tests.Authentication;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinisys.Tests.Catalog;

// Real controllers + Application + SQL Server; all HTTP writes share a rollback-only
// transaction on the already restricted test connection. No development DB fallback.
internal sealed class CatalogApiSession : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IDbContextTransaction _transaction;
    internal ClinisysDbContext Database { get; }
    internal HttpClient Client { get; }

    private CatalogApiSession(ClinisysDbContext database, IDbContextTransaction transaction)
    {
        Database = database;
        _transaction = transaction;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.ConfigureServices(services =>
            {
                services.AddTestAuthentication("Secretary", "Biologist", "Technician");
                services.RemoveAll<ClinisysDbContext>();
                services.RemoveAll<DbContextOptions<ClinisysDbContext>>();
                services.AddScoped(_ =>
                {
                    var context = SqlServerTestDatabase.CreateContext(database.Database.GetDbConnection());
                    context.Database.UseTransaction(transaction.GetDbTransaction());
                    return context;
                });
            });
        });
        Client = _factory.AuthenticatedClient();
    }

    internal static async Task<CatalogApiSession> OpenAsync()
    {
        var db = SqlServerTestDatabase.CreateContext();
        try { return new(db, await db.Database.BeginTransactionAsync()); }
        catch { await db.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await Database.DisposeAsync();
    }
}
