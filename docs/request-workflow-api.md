# Request workflow and biological validation - Step 08

Only explicit forward transitions are allowed. All requested items require a
result before completion and validation. Existing entry permissions remain
Technician/Biologist; entry or correction never automatically changes status.

| POST under /api/laboratory-requests/{id} | Transition | Roles |
| --- | --- | --- |
| /start | Registered -> InProgress | Technician, Biologist |
| /complete-entry | InProgress -> ResultsEntered | Technician, Biologist |
| /validate | ResultsEntered -> Validated | Biologist only |
| /deliver | Validated -> Delivered | Secretary, Biologist |

Use HTTPS, an authenticated cookie and X-CSRF-TOKEN. The only input is
`{"version":"AAAAAAAAB9E="}`: the current request rowversion, Base64 encoding
exactly eight bytes. Obtain it from GET request detail. Successful actions return
`{id,status,version,validatedAt,validatedBy,deliveredAt}` with the new version.
Actors and timestamps are server controlled. Responses use no-store.

200: success; 400: invalid input/CSRF; 401: unauthenticated; 403: role denied;
404: absent request; 409: stale token, forbidden transition, incomplete results or
SQL deadlock; 503: database unavailability or uncertain commit; 500: unexpected
failure. An uncertain commit includes outcomeUnknown=true: read the request
before deciding whether to resubmit. No transaction is automatically replayed.

The request is locked before items/results, matching the result-write protocol.
Validation saves request status, validation metadata for every result and audit
atomically. Validated/delivered requests and validated results reject ordinary
corrections. Patient, analysis, price and reference snapshots remain unchanged.
Privileged direct SQL is outside these API guarantees.

AuditEntries records transitions, validation and delivery with actor, UTC instant
and old/new status. Result creation/correction records old/new numeric value in
the same transaction as the write. Existing audit entries are not modified.

No migration is required: existing columns, rowversions and audit actions suffice.

## Local SQL validation - Step 08

Validated on DESKTOP-27CJ3KR\SQLEXPRESS / ClinisysTestDb with the four approved
migrations and the existing restricted principal. Five workflow SQL tests passed:
full lifecycle/audit/result locks, incomplete results and illegal transitions,
Secretary/Technician validation refusal, independent-connection version conflict,
and caller-transaction/savepoint rollback. HTTP scenarios reuse the existing
test-only role handler with real policies, CSRF and SQL stores; real Identity
cookies are tested separately in AuthenticationSqlTests.

The full Release regression passed 511 tests, with zero failures or skipped tests.
The only test correction concerned decimal audit formatting: 1.000000 and 1 are
compared numerically. No production correction, migration or permission change
was needed during SQL validation. Fixtures clean up only their owned synthetic
rows, including audits, and do not allocate annual request numbers.

For a separately authorized rerun, explicitly configure CLINISYS_SQL_TEST_CONNECTION
to the guarded .\SQLEXPRESS / ClinisysTestDb Windows-authentication connection:

```powershell
dotnet test tests/Clinisys.Tests/Clinisys.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Clinisys.Tests.Workflow.RequestWorkflowSqlTests"
dotnet test Clinisys.slnx --configuration Release --no-build
```

These results apply to the local test environment only, not other installations.
