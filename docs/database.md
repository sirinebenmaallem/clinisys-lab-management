# Step 02 database design

## Requirements source and scope

The six assessment screenshots supplied in the conversation were read as business
requirements, not as instructions to implement all five days of work. No assessment
document was found in the repository. Relevant requirements: catalogue code/name/unit/price,
age/sex reference ranges, panels, patient lookup by CIN or phone, unique request number,
prescribing physician, percentage discount, numeric results and abnormality, validation,
delivery, and history of changes including actor/time/old/new values.

The assessment also mentions Friedewald LDL calculation (only below the specified
triglyceride threshold), PDF reports, dashboards/stored statistics procedure,
authentication/roles, Excel import, simulated SMS and Docker. These are later steps;
none is implemented or claimed here. Example clinical values from the screenshots
are not seeded into the database.

## Terminology and relationships

| C# entity / SQL table | French terminology | Relationships and purpose |
| --- | --- | --- |
| Patient / Patients | Patient, CIN | One patient has many requests; optional unique CIN and indexed nonunique phone |
| MedicalAnalysis / MedicalAnalyses | Analyse | Unique code, label, unit, catalogue price, active flag |
| ReferenceRange / ReferenceRanges | Valeurs de référence | Many ranges per analysis, classified by sex and age interval |
| AnalysisPanel / AnalysisPanels | Bilan | Unique code, name, active flag |
| PanelAnalysis / PanelAnalyses | Composition du bilan | Explicit panel-analysis many-to-many join; unique pair |
| LaboratoryRequest / LaboratoryRequests | Demande | Patient FK, unique request number, physician, discount, status and validation/delivery metadata |
| RequestPanel / RequestPanels | Bilans demandés | Unique request-panel pair and panel code/name snapshots |
| RequestItem / RequestItems | Analyse demandée | Unique request-analysis pair; historical catalogue and financial snapshots |
| AnalysisResult / AnalysisResults | Résultat | Zero or one numeric result per request item; bounds, abnormality and validation metadata |
| AuditEntry / AuditEntries | Historique des modifications | Request FK and optional item FK, actor, timestamp, action, field, old/new values |

Requests contain the patient date-of-birth and sex snapshots used to determine age
and reference ranges. Audit entries with an item use a composite FK so they cannot
refer to an item from a different request. Navigations are intentionally omitted;
explicit FKs define all relationships and avoid exposing mutable entity collections.
Report joins and use cases will be implemented later.

## Constraints and SQL types

- Application-assigned nonempty GUID primary keys; required bounded text with SQL
  checks against blank fields. Constructors trim text and normalize codes/CIN/request
  numbers to invariant uppercase. SQL uniqueness follows the database collation;
  the application does not silently force a server-wide collation.
- All foreign keys explicitly use `NO ACTION`. SQL prevents deleting a referenced
  patient, request, analysis or item. There is **no cascade deletion**. Unreferenced
  records and leaf results/audit rows can still be deleted by an authorized SQL user;
  this is not a complete retention/immutability policy.
- Prices: `decimal(18,2)`, nonnegative; discounts: `decimal(5,2)`, 0 through 100 inclusive.
  Domain constructors reject extra fractional digits and precision overflow before
  persistence. SQL decimal conversion itself can round excess fractional digits;
  SQL checks enforce ranges, not rejection of every over-precise input literal.
- Measurements and numeric reference bounds: signed `decimal(18,6)`. Negative values
  are allowed because positivity is analysis-specific. Lower bound must not exceed
  upper bound. Bounds are inclusive; equal bounds are valid.
- `DateOnly` maps to `date` for birth dates. Event timestamps use UTC `DateTimeOffset`
  mapped to `datetimeoffset(7)`. SQL preserves offsets; UTC normalization of future
  direct writes is an operational responsibility. Birth dates must be non-default
  and not in the future. No arbitrary maximum patient age is imposed.
- Every entity has SQL `rowversion` for optimistic EF concurrency checks. It is not
  a date, an audit log, or a database immutability mechanism.
- SQL CHECKs constrain enum values, age intervals, bounds, prices, discounts,
  paired validation actor/time, chronological validation/delivery and consistency
  of abnormality flags with the stored numeric bounds.
- Indexes cover analysis/panel codes, CIN, phone, request number/date, range lookup,
  relation pairs and request audit timeline. One result per request item is enforced
  by a unique FK index. No phone uniqueness is imposed (families may share a phone).

## Historical financial and reference snapshots

RequestItem copies analysis code/name/unit, unit price and the request discount at
construction. No price is read from the catalogue when calculating historical
amounts. `NetPrice` is calculated from these immutable-through-public-API snapshot
fields, rounded to two decimals away from zero; a request total will be the sum of
these rounded item amounts. It is not a stored aggregate that can drift.

RequestPanel records the selected panel, while RequestItems store the flattened,
deduplicated analyses actually requested. Later catalogue panel changes must never
re-expand historical requests. Automatic panel expansion is not implemented here.
There is no separate panel price: its amount is assumed to be the sum of its analyses.

AnalysisResult copies the bounds actually used; its unit is the request item's unit
snapshot. No bounds means `NotEvaluated`, not `Normal`. Updating catalogue prices,
units or ranges cannot retroactively change stored results or amounts. The SQL tests
verify catalogue price/unit changes leave request item snapshots intact. Snapshot
fields are not yet protected against intentional direct SQL editing.

## Assumptions requiring confirmation before use-case implementation

- At least CIN or phone is required; name, birth date and sex are required. CIN is
  optional for patients without one (for example minors); when present it is unique.
  No country-specific identity or telephone format is invented.
- `Sex` values are `Unspecified`, `Female`, `Male`. They describe the reference-range
  classification, not an exhaustive representation of personal identity. `Unspecified`
  matches only `Unspecified`; it is not a wildcard. Clinical policy for missing sex
  must be approved before selecting ranges automatically.
- Age is elapsed calendar days at sample/request registration (birth date and the
  UTC registration date). An age interval is `[MinimumAgeDays, MaximumAgeDays)`;
  null maximum is unbounded. This supports children without imposing age bands or
  treating a year as a fixed number of days. Actual sample collection time is not
  supplied by the assessment and is not modeled yet.
- An exact duplicate range (including an unbounded maximum) is rejected by SQL.
  Step 03 catalogue commands now reject other overlapping ranges for the same
  analysis/sex under a shared parent-row locking protocol. The initial schema still
  does **not** prevent overlaps introduced by privileged direct SQL/other writers.
  No automatic patient/result range selection is currently implemented.
- One numeric result per requested analysis, no quantities or repeat runs within a
  request. Repeats, text results, detection limits and unit conversion need explicit
  requirements before extending the schema.
- One percentage discount is copied onto each item. Prices share one laboratory
  currency; the currency, taxes and convention catalogue were not specified. There
  is no invoice/payment ledger at this step.
- Request numbers are caller-supplied and unique, bounded to 30 characters; the
  `LAB-2026-000123` pattern is an example, not an imposed numbering algorithm.
- Physician is required free text, not a new physician directory. Actor strings are
  placeholders for future authenticated subject identifiers; no users/roles or
  authorization guarantees exist yet.
- Patient name/CIN/phone remain linked to the patient record rather than fully
  snapshotted. A later reporting policy must decide how identity corrections are
  shown on historical reports.

## Workflow and future enforcement strategy

The stored enum maps exactly to the assessment:
`Registered` (Enregistrée) → `InProgress` (En cours) → `ResultsEntered` (Saisie)
→ `Validated` (Validée) → `Delivered` (Délivrée).
Construction starts at Registered without validation/delivery metadata. Columns
exist for request/result validation actor/time and delivery time. CHECK constraints
ensure row consistency, but do not enforce transitions or cross-row completeness.
Workflow services, validation operations and edits are deliberately not exposed yet.

**Not implemented:** SQL immutability triggers, SQL role provisioning, automatic
audit capture, append-only audit enforcement, workflow transitions, authorization
and enforcement of complete results before validation. Private setters, rowversion
and CHECK constraints do not stop a direct SQL update to a validated record.

Before implementing writes, the planned protection is:

1. Domain/application operations enforce allowed transitions, result completeness
   and the authenticated biologist-only validation rule. Update the request, its
   results and audit entries in one transaction with optimistic concurrency.
2. Add set-based SQL triggers on requests, items, selected panels and results.
   Inspect **both inserted and deleted** rows/parent IDs so changing a child FK or
   resetting the parent's status cannot bypass locking. Reject updates/deletes to
   validated/delivered clinical and financial data, and inserts into locked requests.
   Permit only the explicit Validated → Delivered transition, with delivery timestamp,
   leaving clinical and financial fields unchanged. Delivered requests allow no edits.
3. Acquire locks on the relevant parent rows in a consistent order for both child
   writes and validation (for example UPDLOCK/HOLDLOCK), preventing a race between
   result editing and request validation. Handle multirow statements and moves
   between requests. Reject backward/skipped transitions, validate result state and
   prevent editing validation metadata after validation.
4. Capture authorized changes at the database boundary with old/new values, UTC
   timestamp and authenticated actor passed through a controlled session context.
   An application account with freely writable session context alone cannot provide
   tamper-proof actor identity: use restricted procedures/permissions and an
   independently protected operational audit as needed. Protect AuditEntries from
   UPDATE/DELETE, deny DDL/trigger-disable/ownership permissions to runtime users,
   and keep a separate deployment principal. A sysadmin can bypass in-database
   protections; external auditing/backups remain necessary.
5. Add SQL integration tests for direct INSERT/UPDATE/DELETE, multirow statements,
   parent reassignment, status reset, audit tampering and concurrent validation/edit
   transactions. Only after those pass may database-level immutability be claimed.

## Migration safety and security

### Isolated integration tests

Integration tests now require `.\SQLEXPRESS` / `ClinisysTestDb`. The earlier opt-in
guard accepted `ClinisysDb`; that unsafe default for integration testing has been
removed. Model-only tests also name the test database, even though they never connect.
Development configuration remains unchanged and uses Windows authentication with
no password. The test setup script is separate from the development migration script.

`scripts/Prepare-IntegrationTestDatabase.ps1` only migrates the fixed test target,
preserves existing data, rejects unexpected schema/history or unsafe database trust
settings, and prepares a restricted no-login user inside that database. It accepts
SQL Server's automatic CONNECT grant, four DML permissions and VIEW DEFINITION on
the dbo schema; unexpected permissions/ownership/roles cause it to stop. There is
no new server login or SQL credential. Each test connection is nonpooled and switches
to that user WITH NO REVERT before test queries. A connection interceptor repeats
the restriction after every reopen, and tests prove cross-database access fails.
This protects against accidental test SQL, including three-part database names.
An operator can still intentionally edit the test harness or use a separate privileged
connection; it is not a security boundary against the machine administrator.

The original five opt-in SQL tests perform these operations, all in rolled-back
transactions with synthetic identifiers:

| Original test | Inserts | Updates | Deletes |
| --- | --- | --- | --- |
| UniqueAnalysisCodes | Analyses and rejected duplicate | None | None |
| UniqueRequestNumbers | Patient, requests and rejected duplicate | None | None |
| DirectSql_RejectsInvalidRangeAndFinancialValues | Patient, analysis, request, range, item | Attempts invalid bounds, age, price, discount, status (rejected) | None |
| HistoricalRelationships | Patient, analysis, request, item, result, audit | Catalogue price/unit change | Attempts parent deletion (rejected) |
| AuditCannotReferenceAnItemFromAnotherRequest | Two requests, patient, analysis, item and rejected audit | None | None |

Additional SQL tests compare the actual schema to InitialCreate (columns/types,
primary and unique keys, unique filters, all FKs with NO ACTION and trusted/enabled
CHECKs), exercise all unique relation pairs and CIN, verify exact decimal round trips,
persist audit actor/time/old/new values and reject orphaned audit references. The
audit tests verify referential/content integrity, **not append-only/tamper resistance**.
Immutability and automatic audit capture remain unimplemented as described above.

The SQL execution-context behavior is documented by
[Microsoft: EXECUTE AS](https://learn.microsoft.com/en-us/sql/t-sql/statements/execute-as-transact-sql).

### Application migration and operational limits

InitialCreate only creates the ten application tables, their constraints/indexes
and EF migration history; no demo or real patient data is seeded. Its Up operations
are tested to contain no destructive operations or arbitrary SQL. Its Down method
throws rather than dropping medical history. Corrective work must use reviewed
forward migrations and backups, never automatic destructive rollback.

The development update script accepts only `.\SQLEXPRESS`, `ClinisysDb` and Windows
authentication. It probes connectivity and refuses unknown existing tables/history.
EF startup never invokes Migrate/EnsureCreated. See README for commands. EF's own CLI
can target arbitrary connection strings: use the guarded script for application and
review any manually issued command independently.

Do not enable EF sensitive-data logging, print connection strings, put credentials
in source control or use real patient data in tests. Local `TrustServerCertificate`
is a development-only concession. Production requires certificate validation, a
dedicated least-privilege account, protected backups, access controls, a retention
policy and separation between migration and application permissions. Free-text
audit values may contain sensitive data in future and must receive the same access
and retention controls as results.
