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
        var stage = "création des rôles";
        try
        {
            foreach (var role in AuthenticationRoles.All)
                if (!await roles.RoleExistsAsync(role)) Require(await roles.CreateAsync(new ApplicationRole { Name = role }));
            stage = "recherche du compte";
            var user = await users.FindByNameAsync(name);
            if (operation == "create")
            {
                if (user != null) throw new AdministrationInputException("Ce compte existe déjà ; utilisez une opération administrative adaptée.");
                user = new ApplicationUser { UserName = name };
                stage = "création du compte et validation du mot de passe";
                Require(await users.CreateAsync(user, Password()));
            }
            if (user == null) throw new AdministrationInputException("Compte introuvable.");
            if (operation is "create" or "roles")
            {
                stage = "attribution des rôles";
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
            {
                stage = "réinitialisation du mot de passe";
                Require(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), Password()));
            }
            if (operation == "unlock")
            {
                Require(await users.SetLockoutEndDateAsync(user, null));
                Require(await users.ResetAccessFailedCountAsync(user));
            }
            stage = "mise à jour du compte et révocation des sessions";
            Require(await users.UpdateSecurityStampAsync(user));
            stage = "commit (vérifier la persistance avant toute nouvelle tentative en cas d’échec)";
            await tx.CommitAsync();
            Console.WriteLine("Administrative operation completed; existing sessions revoked.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Échec administratif — étape : {stage}. {SafeFailureReason(error)}");
            Environment.ExitCode = 1;
            // Transaction disposal rolls back; never print credentials, tokens or SQL errors.
        }
    }

    private static void Require(IdentityResult result)
    {
        if (result.Succeeded) return;
        // Never display Identity descriptions: they can embed account input.
        var reasons = result.Errors.Select(error => error.Code switch
        {
            "InvalidPassword" or "PasswordTooShort" => "Mot de passe refusé : longueur requise de 15 à 128 caractères, espaces compris.",
            "DuplicateUserName" => "Nom d’utilisateur déjà utilisé.",
            "InvalidUserName" => "Nom d’utilisateur invalide : 3 à 64 caractères ASCII, lettres/chiffres puis . _ - autorisés.",
            "ConcurrencyFailure" => "Le compte a été modifié simultanément ; vérifiez son état avant de réessayer.",
            "InvalidRole" or "InvalidRoleName" or "DuplicateRoleName" or "UserAlreadyInRole" or "UserNotInRole" => "Attribution de rôle Identity refusée.",
            _ => "Opération refusée par Identity ; vérifier la configuration et les entrées."
        }).Distinct();
        throw new AdministrationInputException(string.Join(" ", reasons));
    }

    private static string SafeFailureReason(Exception error)
    {
        if (error is AdministrationInputException) return error.Message;
        if (error is AuthenticationException { Field: "password" })
            return "Mot de passe invalide : saisissez entre 15 et 128 caractères. La saisie est masquée ; les espaces sont conservés.";
        if (error is AuthenticationException { Field: "roles" })
            return "Rôles attendus : Secretary, Technician, Biologist (noms exacts séparés par des virgules).";
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is SqlException sql)
                return $"Échec de persistance SQL (code {sql.Number}). Vérifier les permissions et le schéma ; aucun détail SQL sensible n’est affiché.";
        return "Échec technique ; vérifier la configuration locale. Aucun détail sensible n’est affiché.";
    }

    private sealed class AdministrationInputException(string message) : Exception(message);

    private static string Password()
    {
        Console.WriteLine("Password (input hidden, 15–128 characters, spaces preserved):");
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
