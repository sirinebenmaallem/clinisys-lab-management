# Step 03 — catalogue API

This step extends the existing architecture and database without changing its schema.
No patient/request/result/PDF endpoints or frontend are introduced. No medically
authoritative reference data is seeded. JSON request/response properties use camelCase;
sex values are strings: `Unspecified`, `Female`, `Male` (case-insensitive input).
Numeric enum input is rejected.

## Routes

| HTTP | Route | Success |
| --- | --- | --- |
| POST | `/api/medical-analyses` | 201, analysis DTO and Location |
| GET | `/api/medical-analyses/{id}` | 200, analysis DTO |
| GET | `/api/medical-analyses` | 200, paginated analyses |
| PUT | `/api/medical-analyses/{id}` | 200, updated analysis DTO |
| POST | `/api/medical-analyses/{id}/deactivate` | 204, idempotent deactivation |
| POST | `/api/medical-analyses/{analysisId}/reference-ranges` | 201, range DTO and Location |
| GET | `/api/medical-analyses/{analysisId}/reference-ranges` | 200, array of ranges |
| GET | `/api/medical-analyses/{analysisId}/reference-ranges/{id}` | 200, range DTO |
| PUT | `/api/medical-analyses/{analysisId}/reference-ranges/{id}` | 200, updated range DTO |
| DELETE | `/api/medical-analyses/{analysisId}/reference-ranges/{id}` | 204, catalogue range removed |
| POST | `/api/analysis-panels` | 201, panel DTO and Location |
| GET | `/api/analysis-panels/{id}` | 200, panel and its analysis DTOs |
| GET | `/api/analysis-panels` | 200, paginated panels |
| PUT | `/api/analysis-panels/{id}` | 200, updated panel DTO |
| POST | `/api/analysis-panels/{id}/deactivate` | 204, idempotent deactivation |
| PUT | `/api/analysis-panels/{id}/analyses/{analysisId}` | 204, association added |
| DELETE | `/api/analysis-panels/{id}/analyses/{analysisId}` | 204, catalogue association removed |
| GET | `/api/health` | 200, unchanged health endpoint |

There is no DELETE route for analyses or panels. Removing a reference range or panel
association affects only the live catalogue. RequestItems, RequestPanels, results,
audit entries and their snapshots are never edited or deleted by catalogue commands.
A removed association/range returns 404 on a subsequent removal. A duplicate addition
returns 409 rather than silently hiding a duplicate request.

## Input and query contracts

Analysis create/update (PUT replaces these editable fields; it does not reactivate):

```json
{"code":"SYNTHETIC-ONLY","name":"Synthetic catalogue example","unit":"test-unit","price":12.34}
```

Code, name, unit and price are required. Codes have at most 30 characters, names 200,
units 30. Domain validation trims strings, rejects blanks and uppercases codes.
Prices are nonnegative `decimal(18,2)`; more than two fractional digits and overflow
are rejected, not silently rounded. Zero is allowed. Codes remain reserved after
deactivation. The existing SQL collation governs uniqueness and search comparison.

Reference range input (the numbers are synthetic examples, not clinical guidance):

```json
{"sex":"Female","minimumAgeDays":0,"maximumAgeDays":100,"lowerBound":0,"upperBound":1}
```

All fields except maximumAgeDays are required. Age intervals are half-open
`[minimumAgeDays, maximumAgeDays)`; null maximum is unbounded. Minimum age is nonnegative,
maximum must be strictly greater than minimum. Numeric bounds are signed
`decimal(18,6)` and inclusive, with lower <= upper. Adjacent age intervals are valid.
Overlaps for the **same analysis and same sex** return 409, on create and update;
updating a range excludes itself from the overlap check. Unspecified matches only
Unspecified, preserving the existing reference-selection semantics. No age-band or
reference-value policy is invented. Reference IDs are scoped to their analysis.

New/updated ranges require an active analysis. Existing ranges remain readable and
can be removed after deactivation. Changing an analysis unit while catalogue ranges
exist returns 409: values must be reviewed/removed first, rather than silently
reinterpreting numeric bounds in a different unit. This is an application safeguard,
not an automated medical unit conversion. Existing result unit/bound snapshots are
preserved. Updating a price or label never recalculates historical request amounts.

Panel create/update:

```json
{"code":"SYNTHETIC-PANEL","name":"Synthetic panel example"}
```

Code/name have the same limits and normalization as analyses. New associations require
both analysis and panel to exist and be active. An inactive analysis already in a panel
remains visible; deactivation never silently removes associations or historical items.
Removal is allowed for catalogue maintenance, even after deactivation.

Both catalogue list endpoints accept:

- `page` (default 1, positive), `pageSize` (default 20, 1–100).
- `search` (at most 200 characters, trimmed, matches code **or** name by substring).
- `isActive=true|false` (optional; omitted includes active and inactive records).

Response: `{ "items": [...], "totalCount": 42, "page": 1, "pageSize": 20 }`.
Ordering is code then ID; an out-of-range page is an empty array with the total count.
Excessive offsets, invalid types and invalid bounds return 400. Read queries use
AsNoTracking and parameterized EF queries. Count/items are separate read-committed
queries, so totals can change during concurrent catalogue writes.

## Errors, transactions and logging

Errors use `application/problem+json`. Validation errors (400) contain an `errors`
dictionary and traceId. Missing resources return 404; duplicate codes/associations,
overlap, inactive additions and concurrency conflicts return 409. Database outages
return 503; unexpected failures return a generic 500. SQL details and connection
strings are never returned. Cancellation tokens propagate to EF operations.

Range writers take an UPDLOCK/HOLDLOCK on the parent analysis inside a transaction
before checking for overlaps. Analysis updates/deactivation take the same lock;
panel association writes lock panel then analysis. Locks are held until commit,
including when there are no existing ranges. A two-connection integration test checks
concurrent overlapping creates produce exactly one success and one overlap conflict.
Unique database indexes remain the final protection against duplicate codes/pairs.
Existing EF rowversion concurrency failures map to 409. There is no client ETag/
If-Match protocol yet; stale client payloads are not independently version-checked.

Commands normally own their transaction. When a caller supplies an existing transaction,
commands enlist without committing it; the caller must commit/rollback. This supports
real HTTP tests with an enclosing rollback-only transaction. Failed requests must not
be followed by a caller committing unrelated partially tracked work in a reused scope.

Application defines specific persistence ports and separate command/query classes
under Features/MedicalAnalyses, Features/ReferenceRanges and Features/AnalysisPanels.
Infrastructure implements them using ClinisysDbContext. Controllers only bind input,
delegate and select HTTP responses. No MediatR, generic repository, EF dependency in
Domain/Application, or new NuGet package is added.

Structured application logs contain status, exception type and trace ID, not input
bodies, query terms, SQL error messages or medical data. The handled-exception
diagnostics are suppressed explicitly using the
[ASP.NET Core 10 exception-handler option](https://learn.microsoft.com/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed?view=aspnetcore-10.0).
Keep EF sensitive-data logging disabled and review operational logging configuration
before handling patient data in later steps.

## Verification and limits

See README for restore/build/test and isolated SQL setup commands. All SQL/HTTP
integration tests require CLINISYS_SQL_TEST_CONNECTION pointing to ClinisysTestDb.
They keep the existing restricted-session safeguards and explicitly skip when the
variable is absent. HTTP tests run the Development host with real controllers,
Application and SQL Server, rolling back their synthetic data. The concurrency test
commits only its own synthetic catalogue rows for separate sessions, then removes
only those rows in finally; no medical history is created/deleted by that test.

For a running local Development API (default HTTP profile port 5138), execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-CatalogDevelopment.ps1
```

This read-only smoke check verifies health, catalogue paging/search and OpenAPI against
the development configuration without inserting any development records. Full write
lifecycles are verified against the isolated test DB. OpenAPI JSON remains available
at `/openapi/v1.json` in Development; there is no Swagger UI.

**Security limits:** authentication and authorization are not implemented. Catalogue
write endpoints are accessible to anyone who can reach the API. Use only a trusted
local development environment. Database-level immutability after validation/delivery,
automatic audit capture and append-only audit protection remain unimplemented from
Step 02. Range overlap protection covers these API writers, not privileged direct SQL
or other writers that ignore the locking protocol. A deployment still needs restricted
application permissions and database enforcement before clinical use.
