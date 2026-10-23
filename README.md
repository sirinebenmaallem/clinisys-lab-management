# Clinisys

Medical laboratory management application developed as a five-day technical assessment.
This first step provides a working solution skeleton and a health endpoint only.

## Technology stack

- .NET 10 and C#, with nullable reference types enabled in every project.
- ASP.NET Core Web API with controllers and built-in OpenAPI document generation.
- xUnit and Microsoft.AspNetCore.Mvc.Testing for an in-memory HTTP integration test.
- Planned for later steps: EF Core, SQL Server 2025 Express, HTML, CSS, Bootstrap,
  JavaScript and jQuery/AJAX. These are not implemented yet.

## Architecture

The solution uses Clean Architecture. Lightweight CQRS use cases can be organized
in Application when needed; no MediatR or CQRS framework is installed.

| Project | Responsibility | Direct project references |
| --- | --- | --- |
| `src/Clinisys.Domain` | Future entities, value objects and domain rules | None |
| `src/Clinisys.Application` | Future use cases, DTOs and interfaces | Domain |
| `src/Clinisys.Infrastructure` | Future persistence and external implementations | Application, Domain |
| `src/Clinisys.API` | Controllers, composition root and dependency injection | Application, Infrastructure |
| `tests/Clinisys.Tests` | HTTP integration tests | API |

Dependencies point toward the inner layers. Infrastructure will implement
interfaces owned by Application. The three library projects are intentionally
empty until business requirements require code. There are no database tables,
migrations, authentication, repositories or business features at this stage.

## Local setup

Install the .NET SDK **10.0.402**, selected by the existing `global.json`.
Run the following commands from the repository root:

```powershell
dotnet --version
dotnet restore Clinisys.slnx
dotnet build Clinisys.slnx --no-restore
dotnet test Clinisys.slnx --no-build
dotnet run --project src/Clinisys.API --launch-profile http
```

The HTTP launch profile uses `http://localhost:5138` in Development.
In a second terminal:

```powershell
Invoke-RestMethod http://localhost:5138/api/health
Invoke-RestMethod http://localhost:5138/openapi/v1.json
```

`GET /api/health` returns HTTP 200 with `{"status":"Healthy"}`. It reports that
the API is responding; it does not check a database. The integration test verifies
the HTTP status, JSON content type and response body through the application host.
OpenAPI JSON is exposed at `/openapi/v1.json` only in Development; no Swagger UI
package is installed.

For HTTPS, trust the development certificate using `dotnet dev-certs https --trust`
and run `dotnet run --project src/Clinisys.API --launch-profile https`.
The HTTPS profile uses `https://localhost:7158` and redirects HTTP requests to HTTPS.

SQL Server is not required for this step. The intended local instance for later
steps is `.\SQLEXPRESS`; no connection string or database access is configured.
Do not store patient information or secrets in source control. Use .NET user
secrets or environment variables when configuration requires secrets in later steps.
Build output, local secret files and IDE artifacts are excluded by `.gitignore`.
