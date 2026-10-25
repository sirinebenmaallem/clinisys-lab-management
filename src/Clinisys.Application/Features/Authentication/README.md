# Authentication contracts - Step 07

This module owns pure contracts, validation, three role names and twelve named
permissions. It references no Identity, EF Core or ASP.NET Core types.
[API adapters](../../../Clinisys.API/Authentication/README.md) implement session
and antiforgery ports; Infrastructure registers standard Identity EF stores.

Usernames are trimmed ASCII (3-64 characters), begin with a letter/digit and
otherwise allow letters/digits/dot/underscore/hyphen. Lookup is uppercase invariant;
display casing is preserved. Passwords remain unchanged (15-128 UTF-16 code units).
Inputs reject unmapped JSON members. There is no public account-creation contract.

Role assignments require at least one exact known role. Unknown assignments are
rejected; duplicate roles are deduplicated. Permissions are the union of known
roles, without a hierarchy, administrator role. Step 08 adds Requests.Process, Requests.Validate and
Requests.Deliver; only Biologist receives Requests.Validate.
Patients.Search allows all roles and can reveal identifier matches; patient detail
containing CIN/phone is Secretary-only. The API enforces these policies server-side.

Session DTOs contain userId, userName, roles and expiresAt. Pure session validation
checks a stable GUID, username, known roles and future UTC expiry; actual credential,
cookie and security-stamp validation belongs to the API adapter. Antiforgery output
contains requestToken and headerName. No credential hashing occurs in Application.

Step 08 records result corrections and explicit workflow/validation audit entries.
Entering or correcting a result never constitutes biological validation.
