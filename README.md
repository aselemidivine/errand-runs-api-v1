# ErrandRuns API

Production-oriented .NET 10 modular monolith for ErrandRuns. The implemented first increment covers the core multi-stop errand aggregate, pricing, runner matching contract, runner-owned execution, SQL Server persistence, JWT authorization, rate limits, Problem Details, health probes, OpenAPI, and architecture/domain tests.

## Start locally

1. Copy `.env.example` to `.env` and choose strong local secrets.
2. Run `docker compose up --build`.
3. In development, OpenAPI is available at `/openapi/v1.json`; probes are `/health/live` and `/health/ready`.

The UI source package was not present in the supplied attachment. The traceability analysis therefore uses every named screen/workflow in the written product brief and records this assumption.
