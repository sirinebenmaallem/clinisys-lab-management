[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$previousConnectionString = $env:ConnectionStrings__ClinisysDb
Push-Location $repositoryRoot
try {
    Get-Command sqlcmd -ErrorAction Stop | Out-Null
    # Deliberately independent of development connection settings and ambient overrides.
    $testConnectionString = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
    $migrationFiles = @(Get-ChildItem 'src/Clinisys.Infrastructure/Persistence/Migrations/*_InitialCreate.cs')
    if ($migrationFiles.Count -ne 1 -or $migrationFiles[0].BaseName -notmatch '^\d{14}_InitialCreate$') {
        throw 'Expected exactly one InitialCreate migration.'
    }
    $migrationId = $migrationFiles[0].BaseName

    # Read-only master probe; no DDL or data changes to master or ClinisysDb.
    $exists = & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'ClinisysTestDb') IS NULL THEN 0 ELSE 1 END;"
    if ($LASTEXITCODE -ne 0) { throw 'SQL connection failed. No migration applied.' }
    $probe = ($exists | Out-String).Trim()
    if ($probe -eq '1') {
        $preflight = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'ClinisysTestDb' THROW 51000, 'Unexpected database.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID()
    AND (is_trustworthy_on = 1 OR is_db_chaining_on = 1))
    THROW 51001, 'Unsafe existing database security options. Manual review required.', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND
    (SCHEMA_NAME(schema_id) <> N'dbo' OR name NOT IN
    (N'__EFMigrationsHistory',N'Patients',N'MedicalAnalyses',N'ReferenceRanges',N'AnalysisPanels',
     N'PanelAnalyses',N'LaboratoryRequests',N'RequestPanels',N'RequestItems',N'AnalysisResults',N'AuditEntries')))
    THROW 51002, 'Unrecognized tables. No data will be overwritten.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
        THROW 51003, 'Existing tables without migration history. Manual review required.', 1;
END
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId <> N'$migrationId')
        THROW 51004, 'Unexpected migration history. Downgrades are forbidden.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$migrationId')
        AND EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory')
        THROW 51005, 'Existing schema without InitialCreate. Manual review required.', 1;
END;
"@
        & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d ClinisysTestDb -Q $preflight
        if ($LASTEXITCODE -ne 0) { throw 'Existing test database rejected. No migration applied.' }
    }
    elseif ($probe -ne '0') { throw 'Unexpected probe response. No migration applied.' }

    $env:ConnectionStrings__ClinisysDb = $testConnectionString
    & dotnet ef database update $migrationId --project src/Clinisys.Infrastructure --context ClinisysDbContext
    if ($LASTEXITCODE -ne 0) { throw 'Test database migration failed.' }

    # This no-login database user needs no password or server login. Tests impersonate
    # it WITH NO REVERT on each nonpooled connection before any test command is run.
    $permissions = @'
SET NOCOUNT ON;
IF DB_NAME() <> N'ClinisysTestDb' THROW 51000, 'Unexpected database.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID()
    AND (is_trustworthy_on = 1 OR is_db_chaining_on = 1))
    THROW 51001, 'Unsafe database security options.', 1;
IF USER_ID(N'ClinisysIntegrationTests') IS NULL
    CREATE USER [ClinisysIntegrationTests] WITHOUT LOGIN;
DECLARE @principal int = USER_ID(N'ClinisysIntegrationTests');
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE principal_id = @principal
    AND type = 'S' AND authentication_type = 0)
    THROW 51006, 'Existing test principal is not a no-login user.', 1;
IF EXISTS (SELECT 1 FROM sys.database_role_members WHERE member_principal_id = @principal)
    OR EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id = @principal)
    OR EXISTS (SELECT 1 FROM sys.objects WHERE principal_id = @principal)
    OR EXISTS (SELECT 1 FROM sys.database_principals WHERE owning_principal_id = @principal)
    OR EXISTS (SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id = @principal
        AND NOT ((class = 3 AND major_id = SCHEMA_ID(N'dbo') AND state = 'G'
            AND permission_name IN (N'SELECT',N'INSERT',N'UPDATE',N'DELETE',N'VIEW DEFINITION'))
            OR (class = 0 AND major_id = 0 AND state = 'G' AND permission_name = N'CONNECT')))
    THROW 51007, 'Existing test principal has unexpected permissions or ownership. Manual review required.', 1;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [ClinisysIntegrationTests];
GRANT VIEW DEFINITION ON SCHEMA::dbo TO [ClinisysIntegrationTests];
SELECT DB_NAME() AS TestDatabase, MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory;
SELECT name AS TableName FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;
'@
    & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d ClinisysTestDb -Q $permissions
    if ($LASTEXITCODE -ne 0) { throw 'Test database principal setup/verification failed.' }
    Write-Output 'ClinisysTestDb is ready. Existing application data was not deleted or overwritten.'
}
finally {
    $env:ConnectionStrings__ClinisysDb = $previousConnectionString
    Pop-Location
}
