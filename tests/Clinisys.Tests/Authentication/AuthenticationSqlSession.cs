using System.Net.Http.Json;
using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Authentication;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Clinisys.Tests.Authentication;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthenticationSqlCollection
{ public const string Name = "Standard Identity SQL integration"; }

// Same rollback-only convention as CatalogApiSession, but REAL cookie authentication.
// No fake handler/store. All request and fresh-manager contexts join the restricted
// test connection/transaction. Sequential HTTP only; this is not cross-session SQL
// concurrency or a demonstration of committed account provisioning.
internal sealed class AuthenticationSqlSession : IAsyncDisposable
{
    internal const string Password = "Synthetic SQL Identity password 07";
    internal ApplicationUser User { get; } = new() { UserName = "sql_" + Guid.NewGuid().ToString("N") };
    private readonly ClinisysDbContext database;
    private readonly IDbContextTransaction transaction;
    private readonly WebApplicationFactory<Program> host;
    private readonly List<HttpClient> clients = [];

    private AuthenticationSqlSession(ClinisysDbContext database, IDbContextTransaction transaction)
    {
        this.database = database;
        this.transaction = transaction;
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<ClinisysDbContext>();
                services.RemoveAll<DbContextOptions<ClinisysDbContext>>();
                services.AddScoped(_ =>
                {
                    var context = SqlServerTestDatabase.CreateContext(database.Database.GetDbConnection());
                    context.Database.SetCommandTimeout(10);
                    context.Database.UseTransaction(transaction.GetDbTransaction());
                    return context;
                });
            });
        });
    }

    internal static async Task<AuthenticationSqlSession> OpenAsync(string role, bool active = true)
    {
        if (!AuthenticationRoles.IsKnown(role)) throw new InvalidOperationException("Unknown synthetic role.");
        // Existing helper demands the env var and validates exact instance/database,
        // Windows auth, no pooling, no fallback and restricted impersonation.
        var db = SqlServerTestDatabase.CreateContext();
        db.Database.SetCommandTimeout(10);
        AuthenticationSqlSession? session = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var tx = await db.Database.BeginTransactionAsync(timeout.Token);
            session = new(db, tx);
            // Resolve through the production registration: standard EF stores/managers.
            using var scope = session.host.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            if (await users.FindByIdAsync(session.User.Id.ToString()) != null
                || await users.FindByNameAsync(session.User.UserName!) != null)
                throw new InvalidOperationException("Synthetic identity collision; no existing account may be reused.");
            if (!await roles.RoleExistsAsync(role)) Require(await roles.CreateAsync(new ApplicationRole { Name = role }));
            Require(await users.CreateAsync(session.User, Password));
            Require(await users.AddToRoleAsync(session.User, role));
            session.User.IsActive = active;
            Require(await users.UpdateSecurityStampAsync(session.User));
            return session;
        }
        catch
        {
            if (session != null) await session.DisposeAsync();
            else await db.DisposeAsync();
            throw;
        }
    }

    internal HttpClient Client()
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(30);
        clients.Add(client);
        return client;
    }

    internal static async Task CsrfAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var token = await client.GetFromJsonAsync<AntiforgeryTokenDto>("/api/auth/antiforgery")
            ?? throw new InvalidOperationException("Missing antiforgery response.");
        client.DefaultRequestHeaders.Add(token.HeaderName, token.RequestToken);
    }

    internal Task<HttpResponseMessage> LoginAsync(HttpClient client, string? password = null) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginInput(User.UserName, password ?? Password));

    internal async Task WithUserAsync(Func<UserManager<ApplicationUser>, RoleManager<ApplicationRole>, ApplicationUser, Task> action)
    {
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(User.Id.ToString()) ?? throw new InvalidOperationException("Synthetic account disappeared.");
        if (user.UserName != User.UserName) throw new InvalidOperationException("Synthetic account ownership changed.");
        await action(users, scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(), user);
    }

    internal static void Require(IdentityResult result)
    { if (!result.Succeeded) throw new InvalidOperationException("Synthetic Identity operation failed."); }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        try { await host.DisposeAsync(); }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await transaction.RollbackAsync(cleanup.Token);
            }
            finally
            {
                try { await transaction.DisposeAsync(); }
                finally { await database.DisposeAsync(); }
            }
        }
        // Never commit/delete/reset. Even an unexpected seed failure rolls back only
        // this transaction. Abrupt connection termination relies on SQL rollback.
    }
}
