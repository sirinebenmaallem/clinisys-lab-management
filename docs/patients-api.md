# Step 04 — Patient API

This feature uses the existing Patient model and InitialCreate schema. No migration,
patient deletion endpoint, request workflow, or patient audit mechanism is added.

**Authentication and authorization are not implemented. Use synthetic data only in
a trusted local assessment environment. Do not use real patient data or deploy these
APIs publicly.** Full detail responses contain sensitive identifiers. Names, dates of
birth and sex in list responses are also personal information.

## Routes and responses

| Method | Route | Success |
| --- | --- | --- |
| POST | `/api/patients` | 201, detail DTO and Location |
| GET | `/api/patients` | 200, paginated list DTOs |
| GET | `/api/patients/{id}` | 200, detail DTO |
| PUT | `/api/patients/{id}` | 200, updated detail DTO with new Version |

All patient responses, including errors, send `Cache-Control: no-store`.
GUID route constraints match the catalogue: malformed route IDs return 404.

Create example (synthetic identifiers, not real identity or telephone formats):

```json
{
  "firstName": "Synthetic",
  "lastName": "Example",
  "dateOfBirth": "2000-01-01",
  "sex": "Unspecified",
  "nationalIdentityNumber": "SYNTHETIC-ID-ONLY",
  "phoneNumber": "SYNTHETIC-PHONE-ONLY"
}
```

First and last names are required, trimmed, and limited to 100 characters each.
Birth date is required, later than 0001-01-01 and no later than the current UTC date.
Sex is required and accepts string values `Unspecified`, `Female`, `Male`;
integer and undefined enum inputs are rejected. There is no arbitrary maximum age.

Identity number is optional, trimmed, uppercased invariantly and limited to 50
characters. Phone is optional, trimmed and limited to 30. At least one must be
provided. Null means absent; supplied blank strings are invalid. No country-specific
format or phone punctuation normalization is imposed. Identity numbers are unique;
phone numbers can be shared. Phone-only duplicates can exist: matching names,
birth dates or phones do not cause automatic merging or uniqueness rejection.

Detail DTO: `id`, the six fields above, and `version` (Base64 SQL rowversion).
List DTO: `id`, `firstName`, `lastName`, `dateOfBirth`, `sex`. List DTOs omit identity
number, phone and version entirely; use GET by ID for an editable detail record.

## Search and pagination

Example: `GET /api/patients?page=1&pageSize=20&search=SYNTHETIC-ID-ONLY`.

- Page defaults to 1 and must be positive; pageSize defaults to 20, maximum 100.
- Search is at most 200 characters and is trimmed. Blank search means no filter.
- Matches first or last name by substring, or identity number/phone by exact match.
  Identity search is uppercased like stored identity input; phone is only trimmed.
- Comparison case/accent behavior follows the existing database collation.
- Queries use parameterized EF expressions; substring wildcard characters are literal.
- Ordering is last name, first name, then ID. Out-of-range pages contain no items.
- Response: `{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }`.
- Excessive offsets and invalid query types return 400. There is no isActive filter.

Count and page queries are separate reads; concurrent writes can change the data
between them. Name substring queries may scan the table. No speculative search
index or collation migration is introduced.

## PUT and concurrency

PUT uses an envelope containing the replacement patient fields and the version
returned by POST, GET by ID or the latest successful PUT:

```json
{
  "patient": {
    "firstName": "Synthetic corrected",
    "lastName": "Example",
    "dateOfBirth": "2000-01-01",
    "sex": "Unspecified",
    "nationalIdentityNumber": null,
    "phoneNumber": "SYNTHETIC-PHONE-ONLY"
  },
  "version": "COPY_THE_CURRENT_BASE64_TOKEN_FROM_THE_DETAIL_RESPONSE"
}
```

The placeholder above must be replaced with the actual eight-byte rowversion encoded
as Base64. Missing, invalid Base64 or wrong-length tokens return 400. Stale tokens
return 409 without changing stored data. The store compares the submitted token
with the loaded rowversion and uses that client token as EF's original concurrency
value in the SQL UPDATE predicate. This also protects a race after loading the row.
Even an unchanged PUT performs a concurrency-checked write and returns a fresh token.
After a conflict, reload and review changes before retrying.

PUT replaces all six editable fields. Null/omitted optional identifiers are cleared,
but at least one must remain. ID is immutable. No client-created fields or additional
patient columns are introduced.

## Errors, history and privacy

Errors use `application/problem+json`: 400 for validation, 404 for missing patients,
409 for duplicate identities or concurrency/constraint conflicts, 503 for database
failures, and generic 500 for unexpected failures. Validation errors contain an errors
dictionary. Messages do not include submitted values or SQL exception text. Automatic
patient binding errors are sanitized as well as domain errors.

Patient updates leave existing request birth-date/sex snapshots, results and audit
entries untouched. New requests will use corrected demographics. Names and identifiers
are not historically snapshotted; future reporting needs an explicit policy for identity
corrections. No automatic historical recalculation or request-linked patient audit is
performed. Existing audit entities require a request and cannot represent general
patient editing history.

The logging filters in Program.cs apply application-wide, including the analysis
catalogue. They protect confidentiality by suppressing SQL logs that can contain
sensitive identifiers, reducing SQL diagnostic detail for all modules. Sanitized
application error logs remain available and include status and trace ID, not patient
inputs. Hosting request-start/finish logging is filtered to Warning because those logs include query
strings. EF sensitive-data logging stays disabled.
EF update and command logs are suppressed because duplicate-key SQL errors can include
identifiers even when sensitive-data logging is disabled. Do not enable request-body logging,
query-string telemetry or sensitive SQL parameter logging. External proxies, hosting
logs and monitoring must independently redact or omit patient URLs and bodies before
any future deployment; no-store does not provide authentication or access control.

## Verification

From the repository root:

```powershell
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
dotnet test Clinisys.slnx --no-build
```

SQL-dependent tests explicitly skip unless `CLINISYS_SQL_TEST_CONNECTION` is set.
Use only the already prepared isolated test database:

```powershell
$env:CLINISYS_SQL_TEST_CONNECTION = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test Clinisys.slnx --no-build
Remove-Item Env:CLINISYS_SQL_TEST_CONNECTION
```

The existing helper validates the exact server/database, enters the restricted test
principal, and provides rollback-only HTTP sessions. Patient tests use synthetic
records and never apply migrations or write to ClinisysDb. Domain tests cover atomic
updates and validation; SQL/HTTP tests cover lifecycle, identifiers, search/pagination,
privacy, stale updates and historical snapshots. Run the entire solution to include
catalogue regressions. See README for the separately guarded database preparation
process when a test database has not yet been provisioned.
