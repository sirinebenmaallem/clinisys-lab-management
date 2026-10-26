# Official laboratory PDF reports - Step 10

GET `/api/laboratory-requests/{id}/report` downloads an A4 PDF using the existing
Requests.Read policy: Secretary, Technician and Biologist. Authentication is
required. There is no preview for unvalidated results and no status transition.

200 returns application/pdf with attachment filename
`compte-rendu-LAB-2026-000001.pdf`. Only ASCII letters, digits, hyphens and
underscores from the request number enter the filename; no patient name is used.
Responses are no-store, with no ETag, range processing or disk report cache.

400: empty request ID; 401: unauthenticated; 403: role denied; 404: missing request;
409: request is not Validated/Delivered, required results are missing or validation
metadata is incomplete; 503: database unavailable; 500: unexpected/PDF generation
failure. Errors use the existing sanitized ProblemDetails handlers. GET needs no
CSRF token and does not write audit, status, results or validation metadata.

## Contents and configuration

Configure the laboratory display name with `Laboratory:Name` or the environment
variable `Laboratory__Name`. Default: the neutral label `Laboratoire`. No real
address, telephone, signature or professional accreditation is invented.

The report includes the request number/date, patient name and internal ID,
snapshot birth date/sex, analysis code/name/unit snapshots, numeric values,
frozen reference bounds, stored abnormality, calculated-result marker and official
validation date/actor. Dates are UTC; decimal values retain up to six places.
High/low results carry explicit text and bold emphasis, not color alone. Missing
references are labeled unavailable/non-evaluated. Tables paginate with repeated
headers and page numbering.

The current model stores names on Patient, not on LaboratoryRequest: printed
names are current names, while birth date/sex are historical snapshots. The
validator is the immutable actor identifier saved during validation, not a current
Identity username lookup or a digital signature. This feature does not change
these historical modeling limits or claim a signed/frozen archival report.
CIN, phone, prices and internal credentials are not included.

## Library and deployment

Infrastructure alone references PDFsharp-MigraDoc 6.2.4, MIT licensed (as declared
by that exact NuGet package), and PDFsharp 6.2.4 transitively. Application has only
read/PDF ports and plain DTOs; there is no template engine or new persistence model.
See [the pinned package](https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4).

This deployment uses Windows Arial via PDFsharp's Windows font resolver. The
service account must be able to read Windows fonts. No proprietary font file is
copied into the repository. Linux or containers require a separately configured,
licensed font resolver; this implementation fails safely rather than silently
substituting missing fonts. HTTPS, authorization and operational privacy controls
remain required; no production readiness is claimed.

No migration or SQL preparation is needed. Non-SQL tests use synthetic report
data, a fake read port, real rendering/PDF reopening, and an intercepted SQL
connection that cannot physically open.

Local verification: Release build passed with no warnings/errors; 117 targeted
non-SQL tests passed, including 27 new report tests. Tests check text/layout data,
real PDF structure/A4 dimensions, multipage output, permissions, completeness and
unchanged input data. A visual preview was not completed because temporary build
output exhausted available disk space; its temporary directory was removed.

Final local validation: one SQL integration test passed on the verified
DESKTOP-27CJ3KR\SQLEXPRESS / ClinisysTestDb with the existing restricted helper
and owned AnalysisResults fixture. It checks all three pre-validation refusals,
Validated/Delivered PDFs, frozen values/bounds and unchanged request/result/audit
snapshots. Report commands reject non-query execution in this test; its read
context remains untracked. Fixture cleanup removes only its own synthetic data.
No migration or permission change was performed.

The full Release regression passed 577 tests, zero failures, zero skipped. No
production correction was required during this final validation. These results
apply only to the local test environment, not other deployments.
