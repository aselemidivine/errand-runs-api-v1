# Local development

Requirements: .NET SDK 10.0.203+ and Docker. Copy `.env.example` to `.env`, set random local secrets, then use `docker compose up --build`. For host execution, set `ConnectionStrings__SqlServer` and `Jwt__SigningKey`, run `dotnet restore ErrandRuns.slnx`, `dotnet build ErrandRuns.slnx`, and `dotnet test ErrandRuns.slnx`.

External integrations are disabled by default. See `docs/development/external-services.md` for provider keys, environment-variable mapping, and secret-handling rules.

Create EF migrations from the repository root with `dotnet ef migrations add Initial --project src/ErrandRuns.Infrastructure --startup-project src/ErrandRuns.Api`; apply with `dotnet ef database update` after installing/pinning the EF CLI. Never point automatic local migrations at production.
