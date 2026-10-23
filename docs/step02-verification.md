# Step 02 verification — 2026-10-23

## Observed database status

Read-only SQL inspection confirmed that the development database `ClinisysDb` already
existed and had `20261023192710_InitialCreate` / EF 10.0.13 in its migration history.
This conclusion comes from the database itself, not from a successful .NET build.
This verification pass made no schema/data changes to the development database.

The separate `ClinisysTestDb` was created on `.\SQLEXPRESS` and migrated with the same
InitialCreate. Repeated preparation detected it was up to date without recreating
tables. No database/table was dropped, and no existing application data was overwritten.
Both databases have ten application tables plus `__EFMigrationsHistory`.
After all tests both had zero application rows and one migration-history row.

## Verified implementation

- Existing domain entities, DbContext, entity configurations and migration reviewed.
- EF reported no pending model changes; no migration was rewritten or added.
- Actual test database columns, SQL types/nullability, primary/unique keys, unique
  index filters and all eleven FK definitions matched InitialCreate.
- All FKs use NO ACTION, are enabled and trusted. CHECK constraints are enabled and
  trusted and their table/name set matches the migration.
- SQL behavior tests verified codes/request numbers/CIN and relation uniqueness,
  invalid range/financial values, exact decimal round trips, initial status, preserved
  snapshots, parent deletion restrictions and audit relationships/required fields.
- The test guard rejects the development database, unrelated/system databases,
  alternative servers, SQL authentication, file attachment and failover routing.
- SQL test sessions use a no-login database user with DML and schema metadata access
  only. Cross-database read access to ClinisysDb failed with SQL error 916. REVERT
  failed as expected; a reopened connection was restricted again.
- Development configuration still uses Windows authentication and encrypted local
  SQL Server access with no embedded password; sensitive-data logging is not enabled.

## Final results

`dotnet build Clinisys.slnx --no-restore`: success, zero warnings/errors.

`dotnet test Clinisys.slnx --no-build` with the isolated connection variable:
**45 passed, zero failed, zero skipped**, including ten SQL integration tests.
The original test suite had 30 tests; fifteen meaningful checks were added for
isolation, actual schema consistency, uniqueness, decimals, initial status and audit.
With no SQL connection variable, the ten SQL tests are intentionally skipped.

During validation, new test assertions were corrected for SQL Server's refusal of
REVERT and EF's one-to-one relationship tracking. Schema metadata access required
VIEW DEFINITION in the test schema. SQL Server's automatically granted CONNECT
permission is recognized by the setup script. All final checks passed afterward.

## Remaining protections and security limitations

- Database immutability after validation/delivery is **not implemented**. No application
  trigger exists in ClinisysDb; status resets and direct edits are not comprehensively
  prevented by CHECK constraints/private setters/rowversion.
- Audit values have referential/content constraints but no automatic capture,
  append-only enforcement or authenticated actor guarantees. A permitted SQL writer
  can still alter/delete an audit row.
- Workflow transitions, result completeness on validation and biologist authorization
  are not implemented at this database-model step.
- Overlapping distinct age/sex reference ranges remain possible, as documented.
- The local Windows development/migration account is a SQL Server sysadmin. A separate
  least-privilege application identity is still required before deployment. Its rights
  were not changed by this work. Test sessions explicitly reduce their execution
  context before queries; this does not constrain an administrator using other tools.
- `TrustServerCertificate=True` is confined to local development/test settings;
  deployment needs a validated server certificate and operational access controls.

The model/migration baseline is ready for a Step 02 commit with these limits clearly
documented. It is not a production-ready medical-record protection implementation.
No CRUD endpoints, new business modules, commits or pushes were introduced.

## Reproduce

From the repository root (SQL Server/sqlcmd installed and instance running):

```powershell
dotnet tool restore
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Prepare-IntegrationTestDatabase.ps1
$env:CLINISYS_SQL_TEST_CONNECTION = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test Clinisys.slnx --no-build
Remove-Item Env:CLINISYS_SQL_TEST_CONNECTION

$env:ConnectionStrings__ClinisysDb = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet ef migrations has-pending-model-changes --project src/Clinisys.Infrastructure --no-build
Remove-Item Env:ConnectionStrings__ClinisysDb

sqlcmd -S '.\SQLEXPRESS' -E -C -b -d ClinisysDb -Q 'SELECT DB_NAME() AS DatabaseName, MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory;'
sqlcmd -S '.\SQLEXPRESS' -E -C -b -d ClinisysTestDb -Q 'SELECT DB_NAME() AS DatabaseName, MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory;'
```

Do not set `CLINISYS_SQL_TEST_CONNECTION` from development configuration. Migration
tools require sufficient setup rights; tests need permission to impersonate the
prepared test user. See README and database.md for the safety checks and limitations.
