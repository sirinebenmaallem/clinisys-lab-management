# Standard Identity persistence - Step 07

ASP.NET Core Identity 10.0.13 uses the standard EF stores, UserManager and
RoleManager. API composition adds SignInManager, secure cookies, session
validation, CSRF and role policies; see
[HTTP configuration and local administration](../../Clinisys.API/Authentication/README.md).

The seven dbo.Auth* tables use GUID user/role keys, no medical foreign keys and
no cascading deletion. SecurityStamp and ConcurrencyStamp are separate from the
medical rowversions. IsActive defaults to false. No account or password is seeded.
Usernames are ASCII, 3-64 characters, beginning with a letter/digit; remaining
characters may include dot, underscore and hyphen. Uppercase normalized usernames
have an unfiltered unique index. The corrective FixAuthenticationUserNameCheck
migration places the literal hyphen first in the SQL LIKE character set.

IdentitySchemaVersions.Version2 and explicit mappings preserve key lengths and
exclude passkeys even for independent/design-time contexts. Medical mappings,
interceptors, transactions and retry policies are unchanged.

Application validators are reused alongside standard Identity uniqueness checks.
Passwords are preserved exactly (15-128 UTF-16 code units), with the standard
IdentityV3 hasher and package-default iteration count. Lockout is five failures
for fifteen minutes. The API rejects inactive accounts, unknown/missing business
roles and stale security stamps. Local administrative changes renew the stamp.

The three roles are Secretary, Technician and Biologist. None grants account
administration. There is no public registration, custom runtime store or
column-level SQL permission system. Administration is an elevated local interactive
command; runtime SQL permissions must support standard Identity writes (including
lockout, rehash and stamps), without db_owner or migration/DDL privileges.
Restricted test dbo DML grants are not a production permission model.

Back up and review each target before separately authorizing migration. The four
approved migrations are InitialCreate, AddLaboratoryRequestCounters,
AddAuthenticationIdentity and FixAuthenticationUserNameCheck. Guarded scripts
recognize only this ordered chain; no startup migration or account provisioning
runs. Identity migrations are validated locally on ClinisysTestDb, not automatically
on ClinisysDb or other installations. Downgrades are explicitly refused.
