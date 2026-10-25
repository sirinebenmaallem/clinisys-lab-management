# Analysis results API — Step 06

Numeric result entry, correction and consultation only. Entry does not validate a
result and does not change the request status. No biological validation or delivery
endpoint exists. No database migration is required for this module.

## Local security prerequisite

**All four endpoints are disabled by default. Do not publish them or use real patient data.**
For an explicitly authorized synthetic local session, set
`AnalysisResults:EnableLocalAccess=true`, or environment variable
`AnalysisResults__EnableLocalAccess=true`, and unset it afterwards.

Every request must satisfy all of these conditions:

- Environment is exactly Development or Testing; Production and all other environments are refused.
- The activation flag parses as true.
- Both connection addresses (local and remote) are loopback; unknown addresses are refused.
- No `Forwarded`, `X-Forwarded-*` or `X-Real-IP` header is present.

Failure returns 404 before model binding. Bind the listener to loopback only;
do not expose it through a public listener, reverse proxy or tunnel. A local proxy
can originate loopback traffic without declaring itself: these checks are not
authentication and cannot prove the absence of such a proxy.

The server supplies `LOCAL-UNAUTHENTICATED` on each write after checking local
access again. Client headers are not credentials and cannot choose the actor.
Step 07 will add authentication and Secretary/Technician/Biologist roles; Step 08
will add workflow transitions and official validation reserved to the biologist.
The temporary policy and actor must be replaced by authenticated access.

See also the [local-access implementation notes](../src/Clinisys.API/AnalysisResults/README.md).

## Routes and responses

`requestId` and `itemId` are GUID route parameters. `itemId` identifies a RequestItem,
not a MedicalAnalysis. The item must belong to the specified request. Malformed
GUID routes do not match and return 404; empty GUIDs reaching the use case return 400.

| Method | Route | Success | Other documented statuses |
| --- | --- | --- | --- |
| POST | `/api/laboratory-requests/{requestId}/items/{itemId}/result` | 201, detail JSON and Location pointing to GET | 400, 404, 409, 503, 500 |
| PUT | `/api/laboratory-requests/{requestId}/items/{itemId}/result` | 200, detail JSON with new Version | 400, 404, 409, 503, 500 |
| GET | `/api/laboratory-requests/{requestId}/items/{itemId}/result` | 200, detail JSON | 400, 404, 409, 503, 500 |
| GET | `/api/laboratory-requests/{requestId}/results` | 200, list JSON | 400, 404, 409, 503, 500 |

There is no DELETE, upsert or validation endpoint. All responses under these routes
use `Cache-Control: no-store`. GET 409 is an exceptional SQL deadlock, not a business
read conflict. The list is not paginated and is ordered by analysis-code snapshot,
then item ID. It includes requested items without results; a missing request returns 404.

## Input contracts

POST:

```json
{"value": 0}
```

PUT:

```json
{"value": 1.5, "version": "AAAAAAAAB9E="}
```

`value` is required and non-null. Zero and negative values are allowed. The accepted
range is -999999999999.999999 through 999999999999.999999 with at most six decimal
places. Invalid JSON, missing values and out-of-range/excess-precision values return 400.

PUT requires `version`: a Base64 string decoding to exactly **eight bytes**, representing
the result's SQL Server rowversion. Missing, malformed or wrong-length tokens return
400; a stale token returns 409 without overwriting the newer result. Obtain the
current token from a result response; the example token is illustrative, not reusable.
Successful correction returns the new token, including when the numeric value is unchanged.

Unknown JSON properties are rejected. The client cannot submit actor, dates,
reference bounds, abnormality, status, prices or validation metadata. POST on an
existing result returns 409; PUT without an existing result returns 404.

## Output contracts

The following IDs, catalogue labels, numeric values and bounds are synthetic
contract examples, not medical recommendations. JSON property names use camelCase;
abnormality is an enum string: `NotEvaluated`, `Normal`, `Low` or `High`.

Detail DTO (POST, PUT and individual GET):

```json
{
  "id": "10000000-0000-0000-0000-000000000001",
  "laboratoryRequestId": "20000000-0000-0000-0000-000000000001",
  "requestItemId": "30000000-0000-0000-0000-000000000001",
  "medicalAnalysisId": "40000000-0000-0000-0000-000000000001",
  "analysisCodeSnapshot": "SYN-A",
  "analysisNameSnapshot": "Synthetic numeric analysis",
  "unitSnapshot": "synthetic-unit",
  "value": 1.5,
  "referenceLowerBoundSnapshot": 0,
  "referenceUpperBoundSnapshot": 2,
  "abnormality": "Normal",
  "enteredAt": "2026-10-25T10:00:00+00:00",
  "enteredBy": "LOCAL-UNAUTHENTICATED",
  "validatedAt": null,
  "validatedBy": null,
  "version": "AAAAAAAAB9E="
}
```

`enteredAt` and `enteredBy` describe the latest successful entry or correction.
The analysis code, name and unit come from the request item's historical snapshots.
Validation fields are read-only; Step 06 never sets them. No CIN or phone is exposed.

List DTO (a synthetic request with an item not yet entered):

```json
{
  "laboratoryRequestId": "20000000-0000-0000-0000-000000000001",
  "items": [
    {
      "requestItemId": "30000000-0000-0000-0000-000000000002",
      "medicalAnalysisId": "40000000-0000-0000-0000-000000000002",
      "analysisCodeSnapshot": "SYN-B",
      "analysisNameSnapshot": "Synthetic pending analysis",
      "unitSnapshot": "synthetic-unit",
      "result": null
    }
  ]
}
```

Each entered item's `result` contains the complete detail DTO above; each unentered
item has `result: null`. The list contains all requested items, not only entered ones.

## Reference selection and correction

At the first successful entry, the store selects references transactionally:

1. Use the request's birth-date and sex snapshots, without reading corrected patient demographics.
2. Age in days is the UTC calendar date of `RegisteredAt` minus the birth date.
3. Sex must match exactly, including `Unspecified`; there is no fallback.
4. Age intervals are `[minimumAgeDays, maximumAgeDays)`. A null maximum is unbounded.
5. No applicable range: allow entry with both snapshot bounds null and `NotEvaluated`.
6. Multiple applicable ranges: return 409; never choose arbitrarily.
7. Exactly one range: compare the item's unit snapshot and the current analysis unit
   using exact ordinal equality, without trimming, case folding or conversion.
   Incompatibility returns 409. No applicable range means no unit conflict check.

Selected numeric bounds are copied into the result. Bounds are inclusive:
below the lower bound is `Low`, above the upper bound is `High`, and either boundary
or an interior value is `Normal`.

Corrections never select references again, even if the catalogue changes or its
ranges are removed. They retain the initial bounds and recalculate abnormality.
Analysis deactivation after registration does not prevent entry for an existing item.

Both creation and correction require a request in `Registered`, `InProgress` or
`ResultsEntered`, with no validation or delivery metadata. Any `ValidatedAt` or
`ValidatedBy` on the result blocks correction. The request status never changes
automatically. Historical request snapshots, reports and audit entries are not rewritten.

## Transactions, concurrency and errors

Writes lock the parent request before inspecting its items/results. First entry also
locks the catalogue consistently with reference writers. A unique result per item
is enforced by SQL. Correction compares the client token to the stored version and
uses it as EF's original concurrency value. Caller-owned transactions use savepoints;
the store does not commit them. Results have no automatic transaction retry.

| Status | Meaning |
| --- | --- |
| 400 | Invalid body/value/version or empty route identifiers reaching Application |
| 404 | Missing request/item/result, wrong item membership, or local access disabled/refused |
| 409 | Existing result on POST, stale version, non-editable/validated state, ambiguous references, applicable unit mismatch, or confirmed SQL deadlock |
| 503 | Database unavailability or an uncertain transaction outcome |
| 500 | Unexpected failure or internal data inconsistency |

Application/technical errors use sanitized `ProblemDetails`, or
`ValidationProblemDetails` for 400, with a `traceId`. Binding errors use a fixed
message as well. Disabled-access/route-mismatch 404 responses need not have the
application ProblemDetails body. SQL text, connection strings and submitted values
are not echoed by the result error handler.

An uncertain commit is 503 and explicitly includes:

```json
{
  "title": "Result operation temporarily unavailable.",
  "status": 503,
  "detail": "The transaction outcome could not be confirmed. Verify the result before submitting again.",
  "traceId": "synthetic-trace-id",
  "outcomeUnknown": true
}
```

The error framework may add standard ProblemDetails members. Other 503 responses
do not automatically include `outcomeUnknown`. Never blindly replay an uncertain
write: read the result and reconcile its value/version first. A confirmed deadlock
returns 409 without automatic replay; the caller transaction must not be assumed usable.

## Validation and remaining limits

See [SQL test safeguards, commands and local E2 results](../tests/Clinisys.Tests/AnalysisResults/SQL-TESTS.md)
and the [project README](../README.md). Locally, 24 AnalysisResults SQL tests and the
full Release suite of 362 tests passed on `ClinisysTestDb`, with no failures or skips.
These results do not prepare or validate another installation.

Detailed correction history, nonnumeric results and derived medical calculations
are deferred. Physical network loss during commit is not reproduced by the commit
interceptor tests. Future biological validation must follow the same parent-first
locking protocol. This is a local technical assessment feature, not a production-ready API.
