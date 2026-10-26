# Automatic LDL - Step 09

The existing POST/PUT result routes trigger Friedewald calculation in the same
transaction as the source write. Existing Technician/Biologist permissions,
CSRF, parent locking, rowversions and validated/delivered request guards apply.
No additional endpoint, automatic workflow transition or validation is introduced.

## Explicit catalogue mapping

Configure these server settings with the actual catalogue codes. The following
codes are synthetic examples, not seeded or assumed laboratory identifiers:

```json
{
  "LdlCalculation": {
    "TotalCholesterolCode": "TEST-CHOL-TOTAL",
    "HdlCode": "TEST-HDL",
    "TriglyceridesCode": "TEST-TRIG",
    "CalculatedLdlCode": "TEST-LDL-CALC"
  }
}
```

All four codes must be distinct uppercase catalogue codes. Identification uses
AnalysisCodeSnapshot, never fuzzy names or current catalogue labels. All four
analyses must already belong to the request; no item or charge is added. Configure
before entering lipid results and keep the mapping stable for these requests.
Without valid configuration, no new automatic value is produced. If automatic
results already exist, writes fail closed when configuration is missing or its
target no longer matches, rather than silently retaining a stale calculation.

## Calculation and validity

- Exact unit `mg/dL`: LDL = total cholesterol - HDL - triglycerides / 5; TG < 400.
- Exact unit `mmol/L`: LDL = total cholesterol - HDL - triglycerides / 2.2; TG < 4.5.
- All four unit snapshots must match exactly. No unit conversion or normalization.
- All three numeric results must exist. Negative sources and negative calculated
  LDL are rejected for calculation, not clamped to zero. Numeric zero is valid.
- Decimal arithmetic, rounded once to six decimal places, midpoint away from zero,
  matching the existing decimal(18,6) result precision.

Formula and conservative mmol/L threshold follow the
[EAS/EFLM consensus](https://pmc.ncbi.nlm.nih.gov/articles/PMC4929379/).
No new clinical reference bounds are supplied by this feature.

## Persistence, corrections and manual results

Migration AddCalculatedAnalysisResult adds only IsCalculated bit, default false.
Every existing result therefore remains manual. Historical migrations are unchanged;
the new migration must be separately reviewed/applied before SQL use. No startup
migration runs. Guarded preparation scripts recognize the fifth migration.

A generated LDL uses the usual first-entry reference selection and abnormality
evaluation. Corrections preserve its stored bounds and recalculate abnormality.
Source and LDL writes/audits commit or roll back together, using the same server
actor. Manual LDL results are never overwritten. Direct PUT of an automatic LDL
returns 409: correct its source results instead. Clients cannot set IsCalculated.

If source correction makes an automatic LDL invalid, only that unvalidated
automatic result is removed. Audit retains its old value with newValue=null;
reads then show result=null, and workflow completeness checks block validation.
A later valid calculation creates a new result and freezes references at that
new first entry. No manual or validated result is removed.

## Responses

Result details add isCalculated. Source POST/PUT responses add ldlCalculation:
`{status,requestItemId,value}`. GET results lists expose the same calculation state
alongside ordinary result entries; individual GET exposes the persisted numeric
result and provenance. A missing result still returns the existing 404 contract.

States include Calculated, NotConfigured, LdlNotRequested, MissingSource,
IncompatibleUnits, InvalidSource, TriglyceridesTooHigh, InvalidCalculatedValue,
ManualResultPreserved and ReferenceConflict. Noncalculated states have value=null.
If eligible inputs exist but no LDL is persisted, list status is NotCalculated.
Ambiguous analysis snapshots are refused with 409, without saving the source
change or selecting an arbitrary item. Reference conflicts prevent first LDL
creation and are reported in the source write response; they do not invent bounds.

Existing validation/status/concurrency errors keep their HTTP contracts. A failed
calculation is distinct from a failed source entry: valid source data can be saved
while ldlCalculation explains why no LDL was produced.

Without configuration, source write/individual read DTOs keep ldlCalculation=null
for compatibility. The request-results list reports NotConfigured explicitly.

## Isolated SQL validation

The four TEST codes above are injected only into LdlCalculationSqlTests. They are
not configured in application settings, seeded permanently or identified as actual
laboratory codes. Each test uses SqlServerFact and SqlServerTestDatabase, rejects
existing matching codes, creates synthetic rows and rolls its whole transaction
back. No permanent account, catalogue row or annual counter is created.

Migration 20261025235303_AddCalculatedAnalysisResult was applied only on the local
DESKTOP-27CJ3KR\SQLEXPRESS / ClinisysTestDb after a fresh COPY_ONLY/CHECKSUM backup
and successful RESTORE VERIFYONLY. No other environment is thereby prepared.
The five LDL SQL tests passed, covering both units, thresholds, recalculation,
IsCalculated persistence, audit, manual-value preservation and validated locks.
The 29 SQL tests for AnalysisResults/LDL and the full Release regression also
passed: 549 passed, zero failed, zero skipped. Compilation had no warnings/errors.
An initial regression exposed write/read DTO diagnostic differences when mapping
was absent; writes now retain null in that case, preserving existing contracts.
Post-run checks confirmed none of the four TEST codes remained in the catalogue.

For a separately authorized rerun, explicitly set CLINISYS_SQL_TEST_CONNECTION
to the guarded .\SQLEXPRESS / ClinisysTestDb Windows-authentication connection:

```powershell
dotnet test tests/Clinisys.Tests/Clinisys.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Clinisys.Tests.AnalysisResults.LdlCalculationSqlTests"
```
