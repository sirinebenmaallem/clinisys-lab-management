# Step 05 — Laboratory requests

Step 07 requires authentication: reads allow all three business roles; creation
requires Secretary and CSRF protection. Use
synthetic data in a trusted local environment; never expose these APIs publicly or
use real patient data. This step implements registration and reads only, without
PUT, DELETE, cancellation, status transitions, results, biological validation or PDF.

## Routes and JSON

| Method | Route | Success |
| --- | --- | --- |
| POST | `/api/laboratory-requests` | 201, detail DTO and Location |
| GET | `/api/laboratory-requests` | 200, paginated summary DTOs |
| GET | `/api/laboratory-requests/{id}` | 200, detail DTO; 404 when missing |

Example (replace GUIDs with existing synthetic catalogue/patient IDs):

```json
{
  "patientId": "11111111-1111-1111-1111-111111111111",
  "prescribingPhysician": "Synthetic physician",
  "discountPercent": 12.5,
  "analysisIds": ["22222222-2222-2222-2222-222222222222"],
  "panelIds": []
}
```

Only these five properties are accepted. Unknown properties are rejected on this
contract only, including number, registeredAt, status, unitPrice, total, netPrice,
validatedBy, actor and patientVersion. Discount defaults to zero. Physician is
required, trimmed, maximum 200 characters. IDs must be nonempty GUIDs. Either
collection may be omitted or empty. At most 500 supplied IDs (before deduplication)
and 500 distinct final analyses are accepted; larger selections return 400.

Patient must exist. Analyses and panels must exist and be active. A selected panel
must have at least one member and every member must be active. The whole creation
is rejected if any selection is invalid. Direct selections and overlapping panels
are expanded into one distinct set of MedicalAnalysisIds; each is charged once.
Selected panels and their code/name snapshots are stored independently of that set.

The detail contains id, number, patient (id/currentFirstName/currentLastName),
patientDateOfBirthSnapshot, patientSexSnapshot, prescribingPhysician,
discountPercent, status, registeredAt, panels, items, total and version. Item
properties explicitly identify code/name/unit/price/discount snapshots. Version is
the existing eight-byte SQL rowversion encoded in Base64; there is no update API.
Summary DTOs contain id, number, minimal patient, physician, status, date and total.
Neither response includes CIN or phone. Current patient names are not historical
identity snapshots and may change after a correction; future reports must account
for this limitation.

## Pricing and history

Prices come from the locked catalogue at registration. There is no separate panel
tariff or quantity. Discount is uniform, 0–100 inclusive, with at most two decimals.
Each RequestItem computes its net price using decimal and rounds to two places with
MidpointRounding.AwayFromZero. Total is the sum of these rounded line amounts, not
a rounded aggregate discount. Total is calculated from persisted snapshots, not
current prices; NetPrice is not a mapped SQL property.

Requests start at Registered, with no validation/delivery metadata. No results or
unauthenticated audit actors are created. Later catalogue/patient changes do not
rewrite snapshots. Historical reads use RequestItems and RequestPanels, never the
current panel composition. No per-panel provenance link beyond the selected panels
is introduced. Reference bounds are selected and snapshotted at step 06. Age then
uses PatientDateOfBirthSnapshot and the UTC date of RegisteredAt; registration is
the temporary substitute for an unavailable sample-collection date.

## Numbering, transactions and failures

Numbers have exactly the format LAB-YYYY-NNNNNN, start at 000001 per UTC year and
stop at 999999. Numbers cannot be selected or changed by clients. The technical
LaboratoryRequestCounters table stores Year (primary key) and LastValue (1–999999).
It has no GUID or rowversion. An indexed UPDLOCK/HOLDLOCK lookup protects both an
existing counter and an absent year's key range. No MAX+1 is used during creation.
Counter changes and the complete request belong to the same transaction. A rolled
back unpublished allocation may be reused; committed numbers are never decremented
or reset by the API. Gaps are permitted; gap-free numbering is not promised.

Creation uses READ COMMITTED with targeted locks, in this order:

1. Selected panels, sorted by ID, using UPDLOCK/HOLDLOCK to coordinate with catalogue writers.
2. Panel composition ranges using HOLDLOCK against phantom insertions/removals.
3. Distinct analyses, sorted by ID, using HOLDLOCK to preserve prices/activity.
4. Patient using HOLDLOCK to preserve the demographics being copied.
5. One TimeProvider UTC instant, used for both RegisteredAt and numbering year.
6. Annual counter using UPDLOCK/HOLDLOCK, graph inserts, then commit.

If waiting or commit crosses midnight, the captured year remains unchanged. Each
confirmed deadlock retry captures a new instant. Owned transactions have at most
three complete attempts for SQL 1205, with new contexts using exactly the original
EF options/interceptors (including isolated-test protections). Other failures are
not automatically retried. There are no global catalogue/patient retry changes.

When an EF transaction is supplied by a caller, the store uses it without committing,
requires READ COMMITTED, savepoint support without MARS and a context without pending changes, and
creates a savepoint before the counter or graph changes. Recoverable failures roll
back the operation and detach only its own graph. A deadlock invalidating the outer
transaction is not retried internally. The caller owns its final commit/rollback;
the normal HTTP path owns its transaction and returns 201 only after commit.
Ambient TransactionScope transactions are rejected before writes.

A commit failure is treated conservatively as an unknown outcome: 503 ProblemDetails
with outcomeUnknown=true and instructions to verify before resubmitting. It is not
replayed or described as a confirmed rollback. Number uniqueness is not POST
idempotence; a deliberate resubmission can create a second request.

## Filters, errors and privacy

List filters: patientId; number (trimmed, uppercased exact match); status (string enum);
registeredFrom (inclusive); registeredTo (exclusive). Dates accept ISO-8601 offsets.
Page defaults to 1, pageSize to 20 (maximum 100); invalid or overflowing offsets
return 400. Ordering is RegisteredAt descending then Id. Response envelope:
`{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }`.
Count and page reads are separate, so concurrent registrations can affect totals.

Errors use application/problem+json with traceId: 400 for invalid inputs/binding;
404 for missing resources; 409 for inactive/empty selections, annual exhaustion or
deadlock conflicts; 503 for database unavailability/unknown commit; generic 500 for
unexpected failures or an internal generated-number collision. Read 409 is only an
exceptional SQL deadlock, not a business read conflict. GUID-constrained malformed
routes return 404. OpenAPI documents the existing success and problem types.

All request responses, including errors, send Cache-Control: no-store. Binding
errors use fixed messages without submitted values. Existing application-wide
hosting/EF logging restrictions remain in place. Logs must never include request
bodies, patient identifiers, physician values, search terms, SQL error text or
connection strings. External proxies and telemetry need their own redaction.

## Migration and controlled historical initialization

Migration 20261024175903_AddLaboratoryRequestCounters only adds the counter table,
key and checks. InitialCreate and existing columns/indexes are unchanged. Destructive
Down is disabled to avoid losing high-water marks and reusing historical numbers.
The model snapshot includes ten business entities plus the technical counter.

The guarded scripts recognize only the ordered InitialCreate/counter migration
chain, reject unknown/future histories and unexpected tables, and keep their exact
database restrictions. After migration, Initialize-LaboratoryRequestCounters.sql
checks historical LAB-prefixed numbers, rejects noncanonical/year-inconsistent
values, initializes absent counters from historical maxima and rejects existing
counters below recorded requests. It never renumbers requests or resets counters.
Non-LAB legacy numbers remain unchanged. This is deployment-time initialization
under exclusive locks with application writers stopped, not a runtime generator.
It uses the deployment principal; ordinary restricted tests need no DDL privileges.

**Local validation:** migration 20261024175903_AddLaboratoryRequestCounters and
counter initialization have completed on ClinisysTestDb and ClinisysDb. ClinisysDb
was backed up before migration. This does not migrate other environments.
The following preparation commands remain subject to separate authorization for
each target environment:

```powershell
# Stop application writers first; review migration and historical numbering.
powershell -NoProfile -File scripts/Prepare-IntegrationTestDatabase.ps1
# Development database is a separate explicit authorization:
powershell -NoProfile -File scripts/Update-DevelopmentDatabase.ps1
```

Do not start request writers if initialization reports ambiguity. Never drop/rebuild
databases or edit the migration history to bypass a refusal.

## Tests and verification

Build: `dotnet build Clinisys.slnx --configuration Release`.
Pure tests cover Domain construction/formatting/pricing, Commands/Queries, strict JSON,
handler statuses and OpenAPI attributes, and model/migration metadata without SQL.
On a new environment, do not run an unfiltered suite until the test schema is
separately approved/migrated.

SQL tests are opt-in via the existing CLINISYS_SQL_TEST_CONNECTION guard restricted
to .\SQLEXPRESS/ClinisysTestDb with Windows authentication and the no-login principal.
They never migrate/create the schema. Added SQL/HTTP scenarios cover complete
registration, expansion, pricing/history, filters, validation/privacy, savepoints,
annual counters, independent concurrent sessions, locked catalogue/patient reads,
a real deliberately induced deadlock and a lost commit acknowledgement simulation.
Synthetic concurrency tests temporarily commit only their own records and clean
them in finally; HTTP tests use the existing rollback-only session.

After separate authorization and test-database preparation, verification in a new
environment can use:

```powershell
$env:CLINISYS_SQL_TEST_CONNECTION = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test Clinisys.slnx --no-build --configuration Release
Remove-Item Env:CLINISYS_SQL_TEST_CONNECTION
```

The locally validated Release run passed all 150 automated tests, with the SQL
connection explicitly restricted to ClinisysTestDb. This includes the SQL/HTTP
integration suite; these results must not be assumed for a new environment.

Manual checks on the local ClinisysDb also passed:

- Inactive analysis: HTTP 409, without creating a request or consuming a number.
- Creation: HTTP 201, number LAB-2026-000001, initial status Registered, patient and
  analysis snapshots, price 15.00, discount 10%, net 13.50.
- SQL persistence and annual counter verified.
- Detail and list GETs: HTTP 200; patientId, number, status, registeredFrom and
  registeredTo filters verified; pagination returned an empty second page.

These local checks do not establish clinical readiness or remove the authentication,
privacy and controlled-deployment limitations described above.
