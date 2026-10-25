# AnalysisResults SQL integration — phases 06-E1 and 06-E2

## History: phase 06-E1

The tests were written and compiled without execution during E1. SQL execution
required separate E2 authorization; that historical restriction was respected.

## Local validation: phase 06-E2

The following runs were completed in the local development environment using
only `ClinisysTestDb` and the existing restricted SQL test setup:

- All 24 AnalysisResults SQL integration tests passed together.
- Both targeted LaboratoryRequests deadlock scenarios passed after stabilizing
  the test synchronization: the owned retry waits before its first panel lock until
  the competing writer has committed. Production retry logic was not changed.
- The complete Release suite passed **362 tests, 0 failed, 0 skipped**.

The first AnalysisResults run had 22 passes and two failures in native-deadlock
observation. The test observer was corrected to accept EF Core 10.0.13's real
`DbContextTypeErrorEventData` for `QueryIterationFailed`, alongside
`CommandFailedAsync`. An instance-owned AsyncLocal scope isolates the store call;
native SQL 1205 is required and the same exception is deduplicated across both paths.
The corrected tests subsequently passed without weakening safety assertions.

These are local results, not automatic approval, schema preparation or validation
of any other environment. Each future SQL execution still requires authorization
and verification of the isolated target. No tests were rerun for documentation updates.

## Isolation and cleanup

All three test classes use SqlServerFact and SqlServerTestDatabase unchanged.
CLINISYS_SQL_TEST_CONNECTION is required. Only .\SQLEXPRESS / ClinisysTestDb with
Windows authentication is accepted; every physical connection uses the existing
restricted no-login execution context. No API activation, schema creation, migration,
connection probe or dependency on ClinisysDb is introduced.

Fixtures insert only synthetic GUID-owned rows. Their legacy-format SYN-prefixed
request numbers bypass the unrelated numbering use case; NO annual counter is read,
created, decremented or deleted. Cleanup uses only the inserted fixture's IDs,
inside a transaction, in FK order. An unsuccessful seed never authorizes cleanup;
reference ownership is recorded only after insertion succeeds. Existing rows are
never used as fixtures. Unknown external dependencies cause cleanup to fail rather
than delete unrelated history. An abrupt process termination can leave synthetic
rows; there is intentionally no broad recovery/reset command.

The SQL classes share a nonparallel xUnit collection to avoid interference from
other test collections. Within race tests, two independent restricted sessions are
deliberately overlapped using command interceptors and TaskCompletionSource barriers.
No Thread.Sleep/Task.Delay synchronization is used. Cancellation deadlines bound
failed synchronization; they are not assertions about elapsed execution time.
LOCK_TIMEOUT 0 probes check locks without waiting. Avoid multiple simultaneous
test-runner processes against the same test database.

Concurrent operations are cancelled and drained before disposing their source
contexts or caller transactions and before fixture cleanup. Main cooperative waits
use the scenario token (30 or 45 seconds). Final draining deliberately does not
abandon active SQL; secondary task failures do not replace the original test failure.

Cleanup has a separate 30-second cancellation deadline, a 10-second command timeout
and a 5-second SQL lock timeout. It remains one transaction restricted to owned IDs;
failure never triggers broader deletion or automatic retries. These are cooperative
limits, not an absolute wall-clock guarantee: provider rollback/disposal, network
failure or abrupt termination can exceed them or leave synthetic rows.

Coverage includes persistence, reads/left joins, demographic snapshots, age boundary,
no/unique/ambiguous ranges, unit mismatch, deactivation, duplicate/index enforcement,
current/stale/same-value versions, competing creates/updates, parent/result locks,
owned rollback, caller savepoints/rollback, invalid caller configurations, real
deadlocks and simulated uncertain commits. Tests exercise the real store, not HTTP;
the API/local gate has separate no-SQL tests.

Limits:
- Partial validation metadata cannot normally be persisted: current SQL CHECKs reject
  it. SQL tests verify rejection; Domain tests cover defensive guards on rehydrated
  inconsistent states. No constraint is disabled and no validation workflow is added.
- Real deadlocks arise during EF reads. The command failure interceptor and EF's
  QueryIterationFailed diagnostic event both feed a test-only native SQL 1205
  observer. QueryIterationFailed uses EF Core 10.0.13's actual
  DbContextTypeErrorEventData payload (context type, not context instance).
  An instance-owned AsyncLocal scope surrounds ONLY the store CreateAsync call;
  both observation paths require that scope, excluding the competing writer.
  The previous scope is restored in finally, including after failure or cancellation;
  the same SqlException instance is counted once across both paths, including EF
  wrappers. Subscriptions are disposed after the concurrent operation is drained.
  No reader wrapper or exception replacement is used. The corrected deadlock tests
  have passed locally; that does not claim every driver failure originates in the
  same callback or reproduce physical network failures.
  Store conflict, absence of replay/savepoint rollback/commit and absence of a
  persisted result remain required assertions. SqlDeadlockObserverTests exercise
  the diagnostic observer without a provider, connection or SQL execution,
  including async continuations, concurrent task isolation and exception identity.
- Commit uncertainty is simulated by interceptor exceptions immediately before or
  after a REAL commit, not a physical network failure. Both possible outcomes and
  absence of replay are checked. Actual connection loss remains unverified.
- The future validator is represented only by a competing parent UPDATE lock probe.
  Step 08 must implement the same parent-first locking; no official validation is
  performed here. Savepoint rollback failure and MARS-specific SQL execution are
  not reproduced by this suite.

## Future execution instructions

After explicit approval for the run, confirm the existing test schema/principal is prepared,
SQL Server is available and no other test runner/writer is using the database.
From the repository root, the following commands are for LATER execution only:

```powershell
$env:CLINISYS_SQL_TEST_CONNECTION = 'Server=.\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test tests/Clinisys.Tests/Clinisys.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~Clinisys.Tests.AnalysisResults.AnalysisResultSqlTests|FullyQualifiedName~Clinisys.Tests.AnalysisResults.AnalysisResultSqlTransactionTests|FullyQualifiedName~Clinisys.Tests.AnalysisResults.AnalysisResultSqlConcurrencyTests'
Remove-Item Env:CLINISYS_SQL_TEST_CONNECTION
```

Use a freshly built Release assembly. A skipped SQL test is not a successful SQL
validation. No migration/preparation script is invoked by these tests or commands.
For E1/no-SQL verification, explicitly select the five existing no-SQL classes:
AnalysisResultApplicationTests, AnalysisResultReferenceSelectorTests,
AnalysisResultStoreUnitTests, AnalysisResultApiTests, AnalysisResultLocalAccessTests.
Do NOT use the broad AnalysisResults namespace filter with a configured SQL variable:
it now includes these integration tests.
