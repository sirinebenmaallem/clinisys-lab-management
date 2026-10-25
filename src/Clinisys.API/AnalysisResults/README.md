# Temporary local result API — Step 06-D

The four result endpoints are disabled by default. No launch profile enables them.
For an explicitly authorized local session only, set the configuration key
`AnalysisResults:EnableLocalAccess` to `true` (environment variable
`AnalysisResults__EnableLocalAccess=true`). Unset it after testing.

Every result request requires Development or the dedicated Testing environment,
both local and remote connection addresses established as loopback, and absence
of Forwarded, X-Forwarded-* and X-Real-IP headers. Missing addresses, another
environment, a missing/invalid/false flag or a proxy header cause HTTP 404 before
model binding and controller activation. Production is always refused, even with
the flag enabled. Use loopback-only listener bindings; do not expose these APIs
through a public listener, tunnel or reverse proxy.

The server actor provider checks the same conditions on every write and returns
LOCAL-UNAUTHENTICATED. This is not an authenticated technician or biologist.
Client identity headers are not credentials; actor/date/status/reference fields
in the input JSON are rejected. There is no biological validation or delivery API.

These restrictions support synthetic local tests only. They are not authentication
and cannot prove that an external proxy is absent: a proxy can originate a loopback
request without declaring itself. Never use real patient data or publish these
endpoints before Step 07 authentication/authorization. The temporary policy and
actor provider must then be replaced by authenticated access and identity.

Routes: POST/PUT/GET /api/laboratory-requests/{requestId}/items/{itemId}/result,
GET /api/laboratory-requests/{requestId}/results. POST accepts {"value":0}; PUT
accepts {"value":0,"version":"AAAAAAAAB9E="} (eight-byte Base64 rowversion).
POST returns 201 plus Location; PUT returns 200 with a new version; GETs return
200 or 404. Lists include unentered items with result=null. Responses are no-store.
Errors use sanitized ProblemDetails/ValidationProblemDetails with traceId:
400 invalid input, 404 missing/disabled, 409 conflict, 503 technical unavailability
or uncertain commit, 500 unexpected failure. Only an uncertain commit includes
outcomeUnknown=true; never automatically resubmit it. SQL integration remains a
separate, explicitly authorized validation step.
