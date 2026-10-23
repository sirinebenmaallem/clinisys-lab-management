# Clinisys

Medical laboratory management application developed as a five-day technical assessment.
Step 02 adds the domain database model, EF Core SQL Server mapping and InitialCreate migration.
The only HTTP endpoint remains the health endpoint; no CRUD or frontend is implemented.

## Technology stack

- .NET 10 and C#, with nullable reference types enabled in every project.
- ASP.NET Core Web API with controllers and built-in OpenAPI document generation.
- xUnit and Microsoft.AspNetCore.Mvc.Testing for an in-memory HTTP integration test.
- EF Core 10.0.13 with SQL Server 2025 Express (`.\SQLEXPRESS`).
- Planned for later steps: HTML, CSS, Bootstrap, JavaScript and jQuery/AJAX.

## Architecture

The solution uses Clean Architecture. Lightweight CQRS use cases can be organized
in Application when needed; no MediatR or CQRS framework is installed.

| Project | Responsibility | Direct project references |
| --- | --- | --- |
| `src/Clinisys.Domain` | Entities, enums, reference interval and construction rules | None |
| `src/Clinisys.Application` | Future use cases, DTOs and interfaces | Domain |
| `src/Clinisys.Infrastructure` | EF context, entity configurations, migration and SQL Server DI | Application, Domain |
| `src/Clinisys.API` | Controllers, composition root and dependency injection | Application, Infrastructure |
| `tests/Clinisys.Tests` | HTTP, domain, EF model and opt-in SQL integration tests | API, Domain, Infrastructure |

Dependencies point toward the inner layers. Infrastructure will implement
interfaces owned by Application. Application remains empty until use cases are
implemented. Domain has no EF dependency. Authentication, workflow services,
repositories and business endpoints are not implemented at this stage.

## Local setup

Install the .NET SDK **10.0.402**, selected by the existing `global.json`.
Run the following commands from the repository root:

```powershell
dotnet --version
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
dotnet test Clinisys.slnx --no-build
dotnet run --project src/Clinisys.API --launch-profile http
```

The HTTP launch profile uses `http://localhost:5138` in Development.
In a second terminal:

```powershell
Invoke-RestMethod http://localhost:5138/api/health
Invoke-RestMethod http://localhost:5138/openapi/v1.json
```

`GET /api/health` returns HTTP 200 with `{"status":"Healthy"}`. It reports that
the API is responding; it does not check a database. The integration test verifies
the HTTP status, JSON content type and response body through the application host.
OpenAPI JSON is exposed at `/openapi/v1.json` only in Development; no Swagger UI
package is installed.

For HTTPS, trust the development certificate using `dotnet dev-certs https --trust`
and run `dotnet run --project src/Clinisys.API --launch-profile https`.
The HTTPS profile uses `https://localhost:7158` and redirects HTTP requests to HTTPS.

## Database setup and verification

Install/start SQL Server 2025 Express at `.\SQLEXPRESS`. The Windows account running
the migration needs permission to create `ClinisysDb` (if absent) and its schema.
The development connection uses Windows authentication, encryption and a trusted
local development certificate exception. It is in `src/Clinisys.API/appsettings.Development.json`.
There is no automatic migration or database creation on API startup.

From the repository root, in PowerShell:

```powershell
dotnet tool restore
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
dotnet test Clinisys.slnx --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Update-DevelopmentDatabase.ps1
```

The script requires `sqlcmd` (ODBC client). Its execution policy override applies
only to that PowerShell process. It checks the exact local server/database,
connectivity and existing schema/history before applying **only InitialCreate**.
It stops on unrecognized existing tables or migration history, never drops tables,
and prints the migration history and table names for verification. Its only query
against `master` is a read-only existence check. The migration's destructive
`Down` operation is intentionally disabled.

Prepare a separate integration-test database, then run the real SQL tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Prepare-IntegrationTestDatabase.ps1
$env:CLINISYS_SQL_TEST_CONNECTION = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test Clinisys.slnx --no-build
Remove-Item Env:CLINISYS_SQL_TEST_CONNECTION
```

These tests only accept `.\SQLEXPRESS` / **`ClinisysTestDb`**, with Windows authentication.
`ClinisysDb`, system databases and unrelated targets are rejected before opening a
connection. The preparation script checks existing schema/history and applies only
InitialCreate without deleting or overwriting application data. It never migrates
the development database. A repeated preparation is safe for an already migrated
test database. It requires migration rights and permission to create/impersonate
the no-login database user `ClinisysIntegrationTests`.

Every test connection enters that user's database-scoped `EXECUTE AS ... WITH NO REVERT`
context before executing test commands. It has SELECT/INSERT/UPDATE/DELETE and schema
metadata visibility in the test database, with no DDL or cross-database permissions.
Pooling, reconnect retries and ambient transaction enlistment are disabled. Tests
verify the development database is inaccessible and reopening a connection reapplies
the restriction. No SQL password, server login or secret file is created.

Tests use synthetic records and roll back each data transaction. Ten SQL tests cover
schema/migration consistency, restricted access, uniqueness, foreign keys, deletion
restrictions, snapshots, decimal round trips, initial status and audit fields. Without
the variable these ten tests are explicitly skipped; the other 35 tests still run.
The additional guard tests reject unsafe targets without connecting to SQL Server.
Do not copy the development connection into `CLINISYS_SQL_TEST_CONNECTION`.

EF tooling uses explicit configuration, independent of the API launch profile:

```powershell
$env:ConnectionStrings__ClinisysDb = (Get-Content src/Clinisys.API/appsettings.Development.json -Raw | ConvertFrom-Json).ConnectionStrings.ClinisysDb
dotnet ef migrations list --project src/Clinisys.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/Clinisys.Infrastructure
# For a later, reviewed model change only (do not recreate InitialCreate):
# dotnet ef migrations add DescriptiveChangeName --project src/Clinisys.Infrastructure --output-dir Persistence/Migrations
Remove-Item Env:ConnectionStrings__ClinisysDb
```

Use the guarded script above to apply this initial migration rather than an
unguarded `dotnet ef database update`. Do not use database drop, EnsureDeleted,
EnsureCreated or downgrade commands for this project.

See [database design, assumptions and security strategy](docs/database.md) for
entity relationships, financial snapshots, audit limitations and the planned
SQL enforcement of validated-request immutability. **Immutability, automatic audit
capture and workflow transitions are not enforced in this step.**

Do not store patient information or secrets in source control. Use .NET user
secrets or environment variables for credentials. Production must supply its own
`ConnectionStrings__ClinisysDb`, a valid server certificate and a restricted account;
the local certificate exception must not be copied to production. Connection strings
are not logged, and EF sensitive-data logging is not enabled. A patched explicit
`System.Security.Cryptography.Pkcs` dependency overrides the vulnerable transitive
version selected by the design-time tooling; do not suppress NuGet audit warnings.
Build output, local secret files and IDE artifacts are excluded by `.gitignore`.
