# Result API — authenticated access, Step 07

The four result endpoints now require an authenticated Technician or Biologist.
The former local-access flag no longer grants access. Unsafe requests additionally
require a valid antiforgery token obtained under the authenticated identity.

Authorization is enforced by server-side role policies before controller activation.
Anonymous callers receive 401; authenticated callers without permission receive
403. Deployment, HTTPS and Data Protection still need environment-specific review.

The server actor provider returns the authenticated user's stable GUID.
Client identity headers are not credentials; actor/date/status/reference fields
in the input JSON are rejected. There is no biological validation or delivery API.

See [standard Identity HTTP integration](../Authentication/README.md) for cookies,
CSRF, roles and local account administration. Do not treat this implementation as
approval to expose real patient data or deploy publicly without operational review.

Routes: POST/PUT/GET /api/laboratory-requests/{requestId}/items/{itemId}/result,
GET /api/laboratory-requests/{requestId}/results. POST accepts {"value":0}; PUT
accepts {"value":0,"version":"AAAAAAAAB9E="} (eight-byte Base64 rowversion).
POST returns 201 plus Location; PUT returns 200 with a new version; GETs return
200 or 404. Lists include unentered items with result=null. Responses are no-store.
Errors use sanitized ProblemDetails/ValidationProblemDetails with traceId:
400 invalid input/CSRF, 401 unauthenticated, 403 forbidden, 404 missing, 409 conflict, 503 technical unavailability
or uncertain commit, 500 unexpected failure. Only an uncertain commit includes
outcomeUnknown=true; never automatically resubmit it. SQL integration remains a
separate, explicitly authorized validation step.
