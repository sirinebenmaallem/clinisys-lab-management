# Clinisys

Medical laboratory management application developed as a five-day technical assessment.
Step 02 adds the domain database model, EF Core SQL Server mapping and InitialCreate migration.
Step 03 adds the analysis catalogue, reference ranges and panels API. Step 04 adds
patient create/read/search/update with optimistic concurrency. There is no frontend
yet. Step 05 adds registration and consultation of laboratory
requests; its additive counter migration and SQL tests have been validated locally.
Step 06 adds creation, correction and consultation of numeric analysis results,
frozen reference bounds and abnormality evaluation. Official validation is implemented separately in Step 08. Step 07 adds standard ASP.NET Core Identity, secure cookies, CSRF
protection and server-side role authorization. This application is not ready
for production.

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
| `src/Clinisys.Application` | Catalogue, patient, request and result commands/queries, DTOs, persistence/actor ports, authentication contracts and pure reference selection | Domain |
| `src/Clinisys.Infrastructure` | EF mappings, stores, SQL transactions, locks, rowversion concurrency, standard Identity EF stores and dependency injection | Application, Domain |
| `src/Clinisys.API` | HTTP controllers, sanitized errors, composition root, cookies, CSRF, role policies and local administration | Application, Infrastructure |
| `tests/Clinisys.Tests` | HTTP, domain, EF model and opt-in SQL integration tests | API, Domain, Infrastructure |

Dependencies point toward the inner layers. Infrastructure implements
interfaces owned by Application. Domain and Application have no EF dependency.
Catalogue commands and queries are separated without MediatR or generic repositories.
Step 07 implements authentication and the Secretary, Technician and Biologist roles.
Step 08 implements explicit workflow transitions and official validation reserved to Biologist.
Official validation uses a separate endpoint; entering a result never validates it.

## Catalogue API (Step 03)

See [routes, JSON contracts, business rules and security limits](docs/catalog-api.md).
The API supports create/read/search/page/update/deactivate for analyses and panels,
reference-range management with overlap checks, and panel-analysis associations.
Catalogue changes preserve historical snapshots. No schema change or new migration
is required for Step 03; Step 05 adds the separate counter migration.

Reads require any business role; writes require Biologist and CSRF protection.

## Patient API (Step 04)

See [patient routes, examples, contracts, concurrency and privacy limits](docs/patients-api.md).
POST/GET `/api/patients` and GET/PUT `/api/patients/{id}` use the existing schema.
Lists omit identity/phone; detail responses provide a Base64 version required by PUT.
Patient responses use no-store. Shared phones are allowed; national identities are unique.
No DELETE or migration is added. Existing laboratory request snapshots are preserved.

**Use synthetic data only; public deployment is not approved.** Search allows all
three roles; patient detail and writes require Secretary. Run the solution
tests below with the isolated SQL connection to include patient and catalogue tests.

## Analysis results API (Step 06)

See [result routes, JSON contracts, references, concurrency and local security limits](docs/analysis-results-api.md)
and [SQL test isolation, execution instructions and local validation results](tests/Clinisys.Tests/AnalysisResults/SQL-TESTS.md).
The four endpoints create, correct and read numeric results, including lists of
requested items without a result. Reference bounds are frozen at first entry;
corrections preserve those bounds and recalculate abnormality. No new migration is required.

Result endpoints require Technician or Biologist and CSRF protection on writes.
The actor is the authenticated user GUID. The local-access flag and temporary
anonymous actor have been removed.

Local Step 06-E2 validation on `ClinisysTestDb`: all **24 AnalysisResults SQL tests**
passed, both targeted LaboratoryRequests deadlock scenarios passed after test
synchronization was stabilized, and the full Release suite passed **362 tests,
0 failed, 0 skipped**. These results do not validate or prepare other environments.

## Authentication and authorization (Step 07)

See [HTTP authentication, CSRF, HTTPS, Data Protection and local administration](src/Clinisys.API/Authentication/README.md)
and [Identity persistence and migrations](src/Clinisys.Infrastructure/Authentication/README.md).
UserManager, RoleManager, SignInManager and standard EF stores are used; there is
no custom runtime store, public registration or HTTP account administration.
Users may hold multiple business roles; permissions are their union.

| Operation | Secretary | Technician | Biologist |
| --- | --- | --- | --- |
| Catalogue reads, patient search, request reads | Yes | Yes | Yes |
| Patient detail (CIN/phone), patient writes, request creation | Yes | No | No |
| Catalogue writes | No | No | Yes |
| Result reads and writes | No | Yes | Yes |

Business APIs require authentication by default. GET /api/health,
GET /api/auth/antiforgery and POST /api/auth/login allow anonymous access.
GET /api/auth/session requires authentication. POST /api/auth/logout revokes
sessions globally. Send X-CSRF-TOKEN on unsafe requests, including login and logout;
fetch a fresh token after login. The cookie is Secure, HttpOnly, SameSite=Lax,
with absolute 30-minute expiry and no remember-me. API failures return 401/403
without HTML redirects; responses are no-store.

Initial accounts use the elevated interactive local command:
`dotnet run --project src/Clinisys.API --configuration Release -- --manage-users`.
Authorize and securely configure the target first; confirm the database when
prompted. Password input is hidden, never passed in arguments or stored in Git.
No business role grants account administration. Biological validation is Biologist-only.

HTTPS is required. Production startup requires DataProtection:KeyDirectory;
persist keys with Windows DPAPI and service-account-only directory ACLs.
Development requires a usable protected persistent user profile. Hosting, proxy
configuration and SQL least privilege require review before deployment.

The approved migration chain is InitialCreate, AddLaboratoryRequestCounters,
AddAuthenticationIdentity, then FixAuthenticationUserNameCheck. Back up the target,
review history/schema, and separately authorize the appropriate preparation command
below. Test preparation also configures the restricted test principal; both scripts
initialize missing request counters. Neither provisions business accounts.
There is no automatic startup migration. Identity and the CHECK correction were
applied and verified locally on ClinisysTestDb; this does not validate or migrate
ClinisysDb or any other environment.

Latest complete local Release regression reported: **463 passed, 0 failed, 0 skipped**
with ClinisysTestDb, including six real Identity SQL/cookie tests. This run is not
repeated during commit preparation. For a separately authorized rerun, configure
CLINISYS_SQL_TEST_CONNECTION using the isolated connection below and execute
`dotnet test Clinisys.slnx --configuration Release --no-build`.
The targeted filter is
`FullyQualifiedName~Clinisys.Tests.Authentication.AuthenticationSqlTests`.
Synthetic accounts use rollback-only transactions and existing restricted helpers;
this does not validate production privileges or committed account provisioning.

## Request workflow (Step 08)

See [transitions, roles, concurrency and audit](docs/request-workflow-api.md).
Start processing, complete entry, validate and deliver are explicit actions. All
requested results are required before validation. Validated results are locked.
No new schema migration is required. Local validation on ClinisysTestDb passed
all five workflow SQL tests and the full Release regression: **511 passed,
0 failed, 0 skipped**. These results do not validate other environments.

## Local setup

Step 10 adds [official A4 PDF reports](docs/laboratory-reports-api.md) for validated
or delivered requests. The existing Requests.Read policy protects downloads for
all three roles. Generation is read-only and no-store, using frozen references,
stored validation metadata and configurable Laboratory:Name. PDFsharp-MigraDoc
6.2.4 (MIT) is isolated in Infrastructure; the current deployment uses Windows
Arial fonts. No migration is added.
Final local Step 10 validation passed the SQL PDF scenario and the complete Release
suite: **577 passed, 0 failed, 0 skipped**, with ClinisysTestDb only. Visual preview
remains unverified because of limited disk space; PDF structure/content and
pagination are covered by automated tests.

Step 09 adds [automatic Friedewald LDL calculation](docs/ldl-calculation.md), using
explicit server-side catalogue codes, recognized matching units and audited
recalculation. Manual LDL results are preserved. Its additive IsCalculated migration
has been applied only on the local ClinisysTestDb after a verified backup. Other
databases require separate review/application. Test-only catalogue codes are not
production configuration.
Local Step 09 validation passed five LDL SQL tests and the full Release suite:
**549 passed, 0 failed, 0 skipped**, using ClinisysTestDb only.

See [Step 05 request contracts, numbering, transactions and deployment prerequisites](docs/laboratory-requests-api.md).
The new API requires the separately reviewed counter migration and controlled historical
initialization. Do not run the database preparation/update commands below without that
authorization. InitialCreate is unchanged; scripts now recognize the four approved migrations in order.
Previously recorded Step 04 SQL test results below describe that earlier schema.
For Step 05, migration `20261024175903_AddLaboratoryRequestCounters` was applied locally
to `ClinisysTestDb` and `ClinisysDb`, after backing up `ClinisysDb`. The Release run
passed all 150 automated tests with the SQL connection explicitly restricted to
`ClinisysTestDb`. Manual development-database checks also passed; see the request API
document for details. These local results do not prepare any other environment:
new installations still require the reviewed migration and historical initialization.

Install the .NET SDK **10.0.402**, selected by the existing `global.json`.
Run the following commands from the repository root:

```powershell
dotnet --version
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
dotnet test Clinisys.slnx --no-build
dotnet run --project src/Clinisys.API --launch-profile https
```

Cookie authentication requires HTTPS. Trust the local certificate first with
`dotnet dev-certs https --trust`; the HTTPS profile uses `https://localhost:7158`.
In a second terminal:

```powershell
Invoke-RestMethod https://localhost:7158/api/health
Invoke-RestMethod https://localhost:7158/openapi/v1.json
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
connectivity and existing schema/history before applying **only the approved ordered
migrations**, followed by controlled request-counter initialization.
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
the approved migration chain without deleting or overwriting application data. It never migrates
the development database. A repeated preparation is safe for an already migrated
test database. It requires migration rights and permission to create/impersonate
the no-login database user `ClinisysIntegrationTests`.

Every test connection enters that user's database-scoped `EXECUTE AS ... WITH NO REVERT`
context before executing test commands. It has SELECT/INSERT/UPDATE/DELETE and schema
metadata visibility in the test database, with no DDL or cross-database permissions.
Pooling, reconnect retries and ambient transaction enlistment are disabled. Tests
verify the development database is inaccessible and reopening a connection reapplies
the restriction. No SQL password, server login or secret file is created.

Tests use synthetic records with transaction rollback or targeted cleanup of their
own committed fixtures. Historical Step 04 results follow; current Step 07
Release validation is recorded in the authentication section. The original ten SQL tests cover
schema/migration consistency, restricted access, uniqueness, foreign keys, deletion
restrictions, snapshots, decimal round trips, initial status and audit fields. Step 03
adds ten real HTTP/SQL scenarios and a two-connection overlap concurrency test. The
concurrency test removes only its own synthetic catalogue rows after completion;
it never creates or deletes medical history. Step 04 adds 19 patient tests covering
domain/application rules and five SQL/HTTP scenarios. With the variable configured,
**84 tests pass (0 failed, 0 skipped)**. Without it, 26 SQL-dependent tests are explicitly
skipped and 58 tests run without SQL Server.
The additional guard tests reject unsafe targets without connecting to SQL Server.
Do not copy the development connection into `CLINISYS_SQL_TEST_CONNECTION`.

Step 04 verification used `dotnet build Clinisys.slnx --no-restore --configuration Release`
and `dotnet test Clinisys.slnx --no-build --configuration Release` because an already
running Development API locked Debug output DLLs. Restore and the Release build passed
with no warnings; the isolated SQL run passed all 84 tests. The sandbox could not
initialize SqlClient encryption, so the SQL run used the same restricted test setup
outside the sandbox without weakening encryption or changing database configuration.

The original Step 03 smoke script predates authentication. Its catalogue calls now
require a cookie; this command is historical, not an anonymous Step 07 smoke check:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-CatalogDevelopment.ps1
```

It verifies health, catalogue search/pagination and OpenAPI without changing the
development database. Write lifecycles run only in the isolated integration tests.

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
SQL enforcement of validated-request immutability. Step 08 enforces workflow and validated-result immutability through the API,
with transactional transition/result audits. Direct privileged SQL immutability
and audit tamper resistance still require separate database controls.

Do not store patient information or secrets in source control. Use .NET user
secrets or environment variables for credentials. Production must supply its own
`ConnectionStrings__ClinisysDb`, a valid server certificate and a restricted account;
the local certificate exception must not be copied to production. Connection strings
are not logged, and EF sensitive-data logging is not enabled. A patched explicit
`System.Security.Cryptography.Pkcs` dependency overrides the vulnerable transitive
version selected by the design-time tooling; do not suppress NuGet audit warnings.
Build output, local secret files and IDE artifacts are excluded by `.gitignore`.
