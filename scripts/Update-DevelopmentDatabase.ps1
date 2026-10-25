[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$previousConnectionString = $env:ConnectionStrings__ClinisysDb
Push-Location $repositoryRoot
try {
    Get-Command sqlcmd -ErrorAction Stop | Out-Null
    $configuration = Get-Content 'src/Clinisys.API/appsettings.Development.json' -Raw | ConvertFrom-Json
    $connectionString = $configuration.ConnectionStrings.ClinisysDb
    if ($env:ConnectionStrings__ClinisysDb) {
        $connectionString = $env:ConnectionStrings__ClinisysDb
    }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.set_ConnectionString($connectionString)
    $allowedKeys = @('server', 'database', 'integrated security', 'encrypt', 'trustservercertificate', 'connect timeout')
    foreach ($key in $builder.get_Keys()) {
        if ($key.ToLowerInvariant() -notin $allowedKeys) { throw 'Unsupported connection option. No database changes made.' }
    }
    if ($builder['Server'] -cne '.\SQLEXPRESS' -or $builder['Database'] -cne 'ClinisysDb' -or
        $builder['Integrated Security'] -ine 'True') {
        throw 'Only .\SQLEXPRESS / ClinisysDb with Windows authentication is allowed.'
    }

    $approvedMigrations = @('20261023192710_InitialCreate', '20261024175903_AddLaboratoryRequestCounters')
    $migrationFiles = @(Get-ChildItem 'src/Clinisys.Infrastructure/Persistence/Migrations/*_*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } | Sort-Object BaseName)
    if (Compare-Object $approvedMigrations @($migrationFiles.BaseName)) { throw 'Unrecognized migration files.' }
    $migrationId = $approvedMigrations[-1]

    # Read-only inspection of master. All DDL below is restricted to ClinisysDb.
    $exists = & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'ClinisysDb') IS NULL THEN 0 ELSE 1 END;"
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server connection failed. No migration applied.' }
    if (($exists | Out-String).Trim() -eq '1') {
        $preflight = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'ClinisysDb' THROW 51000, 'Unexpected target database.', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND
    (SCHEMA_NAME(schema_id) <> N'dbo' OR name NOT IN
    (N'__EFMigrationsHistory',N'Patients',N'MedicalAnalyses',N'ReferenceRanges',N'AnalysisPanels',
     N'PanelAnalyses',N'LaboratoryRequests',N'RequestPanels',N'RequestItems',N'AnalysisResults',N'AuditEntries',N'LaboratoryRequestCounters')))
    THROW 51004, 'Unrecognized tables. Manual review required.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
        THROW 51001, 'Existing unrecognized schema. Manual review required.', 1;
END
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId NOT IN
        (N'$($approvedMigrations[0])',N'$migrationId'))
        THROW 51002, 'Unexpected migration history. Refusing migration or downgrade.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$($approvedMigrations[0])')
        AND EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory')
        THROW 51003, 'Existing tables without InitialCreate history. Manual review required.', 1;
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$migrationId')
        AND NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$($approvedMigrations[0])')
        THROW 51005, 'Migration history is not an approved prefix.', 1;
END;
"@
        & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d ClinisysDb -Q $preflight
        if ($LASTEXITCODE -ne 0) { throw 'Database preflight rejected the target. No migration applied.' }
    }
    elseif (($exists | Out-String).Trim() -ne '0') {
        throw 'Unexpected database probe response. No migration applied.'
    }

    $env:ConnectionStrings__ClinisysDb = $connectionString
    & dotnet ef database update $migrationId --project src/Clinisys.Infrastructure --context ClinisysDbContext
    if ($LASTEXITCODE -ne 0) { throw 'EF migration failed.' }
    & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d ClinisysDb -i (Join-Path $PSScriptRoot 'Initialize-LaboratoryRequestCounters.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Counter initialization rejected. Do not start request writers.' }

    & sqlcmd -S '.\SQLEXPRESS' -E -C -l 5 -b -d ClinisysDb -Q "SET NOCOUNT ON; SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory; SELECT name AS TableName FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;"
    if ($LASTEXITCODE -ne 0) { throw 'Post-migration verification failed.' }
}
finally {
    $env:ConnectionStrings__ClinisysDb = $previousConnectionString
    Pop-Location
}
