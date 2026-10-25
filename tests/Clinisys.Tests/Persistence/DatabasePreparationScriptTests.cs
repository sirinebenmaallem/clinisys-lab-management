using System.Text.RegularExpressions;

namespace Clinisys.Tests.Persistence;

// Source-level assertions only: never starts PowerShell, sqlcmd or EF tooling.
// These checks do not claim to execute/validate T-SQL behavior on a real database.
public sealed class DatabasePreparationScriptTests
{
    private static readonly string[] Migrations = ["20261023192710_InitialCreate",
        "20261024175903_AddLaboratoryRequestCounters", "20261025202446_AddAuthenticationIdentity",
        "20261025225017_FixAuthenticationUserNameCheck"];
    private static readonly string[] Tables = ["__EFMigrationsHistory", "Patients", "MedicalAnalyses", "ReferenceRanges",
        "AnalysisPanels", "PanelAnalyses", "LaboratoryRequests", "RequestPanels", "RequestItems", "AnalysisResults",
        "AuditEntries", "LaboratoryRequestCounters", "AuthUsers", "AuthRoles", "AuthUserRoles", "AuthUserClaims",
        "AuthRoleClaims", "AuthUserLogins", "AuthUserTokens"];

    [Theory]
    [InlineData("Prepare-IntegrationTestDatabase.ps1", "ClinisysTestDb")]
    [InlineData("Update-DevelopmentDatabase.ps1", "ClinisysDb")]
    public void Script_DeclaresOnlyOrderedApprovedMigrationsAndExactTableAllowlist(string file, string target)
    {
        var script = Read(file);
        var declaration = Regex.Match(script, @"\$approvedMigrations\s*=\s*@\(([^)]+)\)").Groups[1].Value;
        Assert.Equal(Migrations, Regex.Matches(declaration, "'([^']+)'").Select(m => m.Groups[1].Value));
        Assert.Contains("$migrationFiles.Count -ne $approvedMigrations.Count", script);
        Assert.Contains("[StringComparison]::Ordinal", script);
        Assert.Contains("$approvedMigrations[$migrationIndex], $migrationFiles[$migrationIndex].BaseName", script);
        var allowlist = Regex.Match(script, @"name NOT IN\s*\((.*?)\)\)\)", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(Tables, Regex.Matches(allowlist, "N'([^']+)'").Select(m => m.Groups[1].Value));
        Assert.Contains("SCHEMA_NAME(schema_id) <> N'dbo'", script);
        Assert.Contains($"IF DB_NAME() <> N'{target}'", script);
        Assert.Contains("$migrationId = $approvedMigrations[-1]", script);
        Assert.Contains("$env:ConnectionStrings__ClinisysDb = $previousConnectionString", script);
    }

    [Theory]
    [InlineData("Prepare-IntegrationTestDatabase.ps1")]
    [InlineData("Update-DevelopmentDatabase.ps1")]
    public void HistoryGuard_RejectsUnknownIdsHolesAndDuplicateOrInconsistentCanonicalOrder(string file)
    {
        var script = Read(file);
        // Check the real SQL predicate, not a separate C# imitation of the algorithm.
        var sql = Regex.Replace(script, @"\s+", " ");
        Assert.Contains("MigrationId nvarchar(150) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE", sql);
        Assert.Contains("INSERT INTO @approved (Ordinal, MigrationId) VALUES $approvedMigrationValues;", sql);
        Assert.Contains("WHERE a.Ordinal IS NULL) THROW 51004", sql);
        Assert.Contains("ROW_NUMBER() OVER (ORDER BY MigrationId COLLATE Latin1_General_100_BIN2) AS AppliedOrdinal", sql);
        Assert.Contains("h.MigrationId COLLATE Latin1_General_100_BIN2 = a.MigrationId AND DATALENGTH(h.MigrationId) = DATALENGTH(a.MigrationId)", sql);
        Assert.Contains("WHERE a.Ordinal IS NULL OR h.AppliedOrdinal <> a.Ordinal) THROW 51008", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory) AND EXISTS (SELECT 1 FROM sys.tables", sql);
        Assert.DoesNotContain("N'$($approvedMigrations[0])',N'$migrationId'", sql);
        Assert.Contains("$($migrationIndex + 1)", script);
        Assert.Contains("$($approvedMigrations[$migrationIndex])", script);
        Assert.DoesNotMatch(@"(?i)\b(DROP|TRUNCATE)\s+(TABLE|DATABASE)\b|\bDELETE\s+FROM\b|\bHasData\b", script);
        Assert.DoesNotContain("AuthUsers", script[(script.IndexOf("& dotnet ef", StringComparison.Ordinal))..]
            .Split("$permissions")[0]); // No account provisioning in the migration execution path.
    }

    [Fact]
    public void TargetAndRestrictedPrincipalGuards_RemainSeparated()
    {
        var test = Read("Prepare-IntegrationTestDatabase.ps1");
        Assert.Contains("Server=.\\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5", test);
        Assert.Contains("& sqlcmd -S '.\\SQLEXPRESS' -E -C -l 5 -b -d ClinisysTestDb", test);
        Assert.Contains("CREATE USER [ClinisysIntegrationTests] WITHOUT LOGIN", test);
        Assert.Contains("Existing test principal has unexpected permissions or ownership", test);
        Assert.Contains("is_trustworthy_on = 1 OR is_db_chaining_on = 1", test);
        Assert.DoesNotContain("appsettings.Development.json", test);
        var development = Read("Update-DevelopmentDatabase.ps1");
        Assert.Contains("$builder['Server'] -cne '.\\SQLEXPRESS'", development);
        Assert.Contains("$builder['Database'] -cne 'ClinisysDb'", development);
        Assert.Contains("$builder['Integrated Security'] -ine 'True'", development);
        Assert.Contains("Unsupported connection option", development);
        Assert.DoesNotContain("-d ClinisysTestDb", development);
        Assert.DoesNotContain("-d ClinisysDb", test);
    }

    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Clinisys.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "scripts", file));
    }
}
