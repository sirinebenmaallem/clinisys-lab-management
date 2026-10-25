# Standard Identity HTTP integration — Step 07, simplified lot 2

Production code uses the standard EF Identity stores, UserManager, SignInManager
and PasswordHasher. There is no custom runtime store, JWT or public registration.
Application contracts and medical rules/migrations remain unchanged.

## HTTP contract

- GET /api/auth/antiforgery (anonymous allowed): `{requestToken,headerName}`.
- POST /api/auth/login (anonymous allowed): `{userName,password}` only;
  returns `{userId,userName,roles,expiresAt}` and the authentication cookie.
- GET /api/auth/session (authenticated): the same session DTO.
- POST /api/auth/logout (authenticated): `{}`; 204 after global stamp revocation.

Fetch antiforgery first, send its token in X-CSRF-TOKEN on unsafe requests, then
fetch a NEW token after login before the next write. Cookies stay in the browser;
never copy them to JavaScript storage. All API responses are no-store. Invalid
input/CSRF: 400; failed login/absent session: 401; denied permission: 403; login
rate limit: 429. Technical failures are sanitized. No HTML login redirects.

The __Host-Clinisys.Auth cookie is Secure, HttpOnly, SameSite=Lax, Path=/,
nonpersistent, with an absolute thirty-minute ticket and no sliding renewal.
HTTPS is required. Login uses five failures/fifteen minutes via Identity; IP
limiting adds ten login requests/minute without queuing. It is per process and
uses the actual remote IP, not untrusted proxy headers. Proxy/multiple-instance
deployment needs separate review; this is not a production-readiness claim.

Every authenticated cookie request checks the current account, security stamp and
role set against SQL. Inactive or role-less accounts, unknown roles and changed
roles are rejected. Logout changes the stamp globally before local sign-out.
Revocation takes effect on subsequent requests, not work already authorized.
If revocation fails, the local cookie is removed but no global-success is reported.
Two-factor accounts cannot log in through this password-only API.

Business endpoints require authentication by default and the existing named role
policies. Health and antiforgery/login are deliberately anonymous. Patient searches
by CIN/phone remain available to the three roles, with identifier-free list DTOs;
matching still reveals existence and must be used for a legitimate laboratory task.
Only Secretary can retrieve patient detail containing CIN/phone. Results require
Technician or Biologist, and EnteredBy is the authenticated GUID. No client actor,
automatic workflow transition or official biological validation is introduced.

## Data Protection

Development uses ASP.NET Core's platform defaults under the service/user profile;
ensure a usable persistent profile with protected keys. In-memory tests explicitly
use ephemeral keys. Production startup requires DataProtection:KeyDirectory and
this Windows deployment protects those persisted keys with user-scoped DPAPI.
Restrict directory ACLs to the service identity and protect backups. Do not put
keys, certificates, passwords or connection strings in Git. A different platform,
service identity or shared-key deployment requires an explicit protection review.

## Local administration — implemented but NOT executed during this lot

From an elevated interactive Windows console, after independently authorizing the
target and configuring the connection securely:

```powershell
dotnet run --project src/Clinisys.API --configuration Release -- --manage-users
```

This mode starts no HTTP listener and runs no migration. It only accepts local
.\SQLEXPRESS with Windows authentication and ClinisysDb or ClinisysTestDb, requires
typing the exact database name, then asks for the operation and username. Do NOT
run it against development or tests without the corresponding approval.

Operations: create, activate, deactivate, reset-password, roles, unlock. Password
input is hidden and never accepted in command-line arguments. Only the three
approved business roles are created if missing. Create assigns explicit roles
before activation. Each operation runs in a transaction and renews the security
stamp; role edits/activation/password reset revoke previous sessions. No default
account or password is seeded. Standard Identity concurrency failures abort the
operation; errors never print credentials or SQL messages. If commit is uncertain,
inspect state before deciding whether to retry.

The operator must have Windows elevation plus the SQL rights required for these
Identity DML operations; a business role never grants local administration. Use
SQL least privilege compatible with standard stores, without granting API DDL or
db_owner. Column-specific SQL separation is not implemented.

## Local validation

Latest full Release regression reported: **463 passed, 0 failed, 0 skipped** on
ClinisysTestDb, including six AuthenticationSqlTests using standard EF stores,
real cookies and rollback-only synthetic accounts. Identity and the username CHECK
correction are applied on that test database only. No production privilege model
or committed local provisioning is established by these tests.

Business HTTP tests also use a test-only authenticated handler and real CSRF tokens;
non-SQL cookie tests use real managers with a synthetic storage double.
For a separately authorized SQL rerun, set CLINISYS_SQL_TEST_CONNECTION to the
existing guarded test connection and run:

```powershell
dotnet test tests/Clinisys.Tests/Clinisys.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Clinisys.Tests.Authentication.AuthenticationSqlTests"
```

Never use ClinisysDb for this filter. See the root
[database preparation instructions](../../../README.md#database-setup-and-verification)
before preparing a new test environment; migration scripts also initialize counters
and test-principal permissions. Back up and authorize the exact target first.
