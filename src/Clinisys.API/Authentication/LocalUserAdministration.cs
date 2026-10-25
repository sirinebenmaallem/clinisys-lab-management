using System.Security.Principal;
using System.Text;
using Clinisys.Application.Features.Authentication;
using Clinisys.Infrastructure.Authentication;
using Clinisys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.API.Authentication;

// Local elevated Windows console only. Never called by an HTTP endpoint or startup seed.
public static class LocalUserAdministration
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException("An interactive elevated Windows console is required.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Local administrative authorization is required.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinisysDbContext>();
        var connection = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (connection.DataSource != ".\\SQLEXPRESS" || !connection.IntegratedSecurity
            || connection.InitialCatalog is not ("ClinisysDb" or "ClinisysTestDb")
            || connection.UserInstance || !string.IsNullOrEmpty(connection.UserID) || !string.IsNullOrEmpty(connection.Password)
            || !string.IsNullOrEmpty(connection.AttachDBFilename) || !string.IsNullOrEmpty(connection.FailoverPartner))
            throw new InvalidOperationException("Unexpected local administration target.");
        Console.WriteLine($"Target: {connection.DataSource} / {connection.InitialCatalog}. Type the database name to confirm:");
        if (Console.ReadLine() != connection.InitialCatalog) return;
        Console.WriteLine("Operation: create / activate / deactivate / reset-password / roles / unlock");
        var operation = Console.ReadLine();
        if (operation is not ("create" or "activate" or "deactivate" or "reset-password" or "roles" or "unlock")) return;
        Console.WriteLine("Username:");
        var name = AuthenticationRules.ValidateUserName(Console.ReadLine());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var role in AuthenticationRoles.All)
                if (!await roles.RoleExistsAsync(role)) Require(await roles.CreateAsync(new ApplicationRole { Name = role }));
            var user = await users.FindByNameAsync(name);
            if (operation == "create")
            {
                if (user != null) throw new InvalidOperationException();
                user = new ApplicationUser { UserName = name };
                Require(await users.CreateAsync(user, Password()));
            }
            if (user == null) throw new InvalidOperationException();
            if (operation is "create" or "roles")
            {
                Console.WriteLine("Roles (comma-separated exact business names):");
                var selected = AuthenticationRules.ValidateRoles((Console.ReadLine() ?? "").Split(',', StringSplitOptions.TrimEntries));
                var previous = await users.GetRolesAsync(user);
                Require(await users.RemoveFromRolesAsync(user, previous.Except(selected)));
                Require(await users.AddToRolesAsync(user, selected.Except(previous)));
            }
            if (operation is "create" or "activate")
            {
                AuthenticationRules.ValidateRoles(await users.GetRolesAsync(user));
                user.IsActive = true;
            }
            if (operation == "deactivate") user.IsActive = false;
            if (operation == "reset-password")
                Require(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), Password()));
            if (operation == "unlock")
            {
                Require(await users.SetLockoutEndDateAsync(user, null));
                Require(await users.ResetAccessFailedCountAsync(user));
            }
            Require(await users.UpdateSecurityStampAsync(user));
            await tx.CommitAsync();
            Console.WriteLine("Administrative operation completed; existing sessions revoked.");
        }
        catch
        {
            Console.Error.WriteLine("Administrative operation failed. Review the target before retrying.");
            Environment.ExitCode = 1;
            // Transaction disposal rolls back; never print credentials, tokens or SQL errors.
        }
    }

    private static void Require(IdentityResult result)
    { if (!result.Succeeded) throw new InvalidOperationException("Identity operation failed."); }

    private static string Password()
    {
        Console.WriteLine("Password (input hidden):");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar) && password.Length < AuthenticationRules.MaximumPasswordLength)
                password.Append(key.KeyChar);
        }
        Console.WriteLine();
        return AuthenticationRules.ValidatePassword(password.ToString());
    }
}
