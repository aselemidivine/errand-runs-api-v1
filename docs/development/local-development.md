# Local development

Requirements: .NET SDK 10.0.203+ and Docker.

## Run with Docker

From the repository root in PowerShell:

```powershell
Copy-Item .env.example .env
# Edit .env: set ERRANDRUNS_DB_PASSWORD and ERRANDRUNS_JWT_KEY to strong values.
docker compose up --build
```

The API listens on `http://localhost:8080`. In Development, browse to `http://localhost:8080/swagger` to explore its interactive Swagger UI. Use `Invoke-WebRequest http://localhost:8080/health/live` to verify that the process is live, and `Invoke-WebRequest http://localhost:8080/health/ready` to verify that SQL Server is also ready.

## Run without Docker or SQL Server (temporary API sandbox)

For immediate Swagger/API testing, use the Development-only in-memory provider. It supports registration, login, roles, and the other API workflows, but all data is erased when the API stops.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet user-secrets set "Database:Provider" "InMemory" --project src/ErrandRuns.Api
dotnet user-secrets set "Jwt:SigningKey" "replace-this-with-a-unique-local-secret-of-at-least-32-characters" --project src/ErrandRuns.Api
dotnet restore ErrandRuns.slnx
dotnet run --project src/ErrandRuns.Api --urls http://localhost:5055
```

Open `http://localhost:5055/swagger`, register a customer or runner, copy the returned `accessToken`, choose **Authorize**, and paste only the token. Do not type `Bearer`; Swagger adds that prefix automatically. Then call `GET /api/v1/auth/me`.

## Run without Docker (SQL Server LocalDB)

Windows installations with a working SQL Server LocalDB instance can run the API without Docker. First confirm that `sqllocaldb info MSSQLLocalDB` returns instance details. From the repository root in PowerShell, run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=(localdb)\MSSQLLocalDB;Database=ErrandRuns;Trusted_Connection=True;TrustServerCertificate=True" --project src/ErrandRuns.Api
dotnet user-secrets set "Jwt:SigningKey" "replace-this-with-a-unique-local-secret-of-at-least-32-characters" --project src/ErrandRuns.Api
dotnet restore ErrandRuns.slnx
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/ErrandRuns.Infrastructure --startup-project src/ErrandRuns.Api
dotnet run --project src/ErrandRuns.Api --urls http://localhost:5055
```

Open `http://localhost:5055/swagger`. To stop the API, press `Ctrl+C`. The connection string is stored in the .NET user-secrets store, not in source control.

If `sqllocaldb info MSSQLLocalDB` reports a registry/configuration error, repair or reinstall SQL Server LocalDB, or use Docker Desktop instead. Do not continue to the migration command until the LocalDB instance command succeeds.

For a manually configured SQL Server instead, set `ConnectionStrings__SqlServer`, `Jwt__SigningKey`, and `ASPNETCORE_ENVIRONMENT=Development`, then run:

```powershell
dotnet restore ErrandRuns.slnx
dotnet build ErrandRuns.slnx
dotnet test ErrandRuns.slnx
dotnet run --project src/ErrandRuns.Api
```

## Test the API

Swagger is the recommended manual test client. Start by calling `POST /api/v1/auth/customers/register` or `POST /api/v1/auth/runners/register`; each returns an `accessToken`. Click **Authorize** in Swagger, paste that token, and then test `GET /api/v1/auth/me` or the role-appropriate errand APIs. `POST /api/v1/auth/login` obtains a new token for an existing account, and `POST /api/v1/auth/change-password` requires the current password.

The only full request that can be exercised from the publicly exposed workflow today is `POST /api/v1/errands` as a customer. The matching and execution routes also depend on workflow transitions that do not currently have public endpoints: payment must first be confirmed, a runner profile must exist and be available, and the runner must begin their journey. Swagger documents these preconditions on each operation.

## Database migrations

The initial migration—including Identity's users, roles, and user-role tables—is committed in the repository. Docker applies it automatically in Development after SQL Server passes its health check. For host execution, apply it explicitly before trying data-changing endpoints:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/ErrandRuns.Infrastructure --startup-project src/ErrandRuns.Api
```

External integrations are disabled by default. See `docs/development/external-services.md` for provider keys, environment-variable mapping, and secret-handling rules.

Create future EF migrations from the repository root with `dotnet tool run dotnet-ef migrations add <Name> --project src/ErrandRuns.Infrastructure --startup-project src/ErrandRuns.Api`. Never enable automatic migrations in production.
