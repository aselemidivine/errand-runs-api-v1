# ErrandRuns API

Production-oriented .NET 10 modular monolith for ErrandRuns. The implemented first increment covers customer and runner accounts, JWT authentication and role authorization, the core multi-stop errand aggregate, SQL Server persistence, health probes, OpenAPI, and architecture/domain tests.

## Start locally

1. Copy `.env.example` to `.env` and choose strong local secrets.
2. Run `docker compose up --build`.
3. Open `http://localhost:8080/swagger` to explore and test the API. The OpenAPI documents are at `/swagger/v1/swagger.json` and `/openapi/v1.json`; probes are `/health/live` and `/health/ready`.

## API reference

Register or sign in using the authentication endpoints below; both return a JWT bearer access token. In Swagger, click **Authorize** and paste only the returned `accessToken` (without writing `Bearer`); Swagger adds that prefix automatically. Tokens contain the account's `Customer` or `Runner` role, which is enforced by the errand endpoints.

| Method | Route | Required role | Purpose |
| --- | --- | --- | --- |
| POST | `/api/v1/auth/customers/register` | None | Creates a customer account and returns a JWT. |
| POST | `/api/v1/auth/runners/register` | None | Creates a runner account, an Applicant runner profile, and returns a JWT. |
| POST | `/api/v1/auth/login` | None | Signs in either account type and returns a JWT. |
| GET | `/api/v1/auth/me` | Customer or Runner | Returns the signed-in account; runner responses include runner status. |
| PUT | `/api/v1/auth/me` | Customer or Runner | Updates the display name, phone number, and profile bio. |
| POST | `/api/v1/auth/change-password` | Customer or Runner | Changes the signed-in account password. |
| POST | `/api/v1/auth/forgot-password` | None | Starts password recovery without disclosing whether the account exists. |
| POST | `/api/v1/auth/reset-password` | None | Sets a new password using a password-reset token. |
| POST | `/api/v1/errands` | Customer | Creates a multi-stop errand. The request must contain two or more stops and at least one `Delivery` stop. |
| POST | `/api/v1/errands/{id}/match` | Customer owner | Assigns the highest-ranked available runner. The errand must be in `PaymentConfirmed`. |
| POST | `/api/v1/errands/{id}/accept` | Assigned runner | Accepts an assigned errand. |
| POST | `/api/v1/errands/{id}/stops/{stopId}/start` | Assigned runner | Starts the next pending stop after the runner has begun their journey. |
| POST | `/api/v1/errands/{id}/stops/{stopId}/complete` | Assigned runner | Completes the active stop. |
| GET | `/health/live` | None | Process liveness probe. |
| GET | `/health/ready` | None | API and database readiness probe. |

Passwords require at least 8 characters, with uppercase, lowercase, and numeric characters. New runner accounts have an `Applicant` runner status and cannot be matched until the future verification workflow makes them available. Domain-rule failures return RFC 7807 Problem Details with `409`; missing resources return `404`; missing or insufficient credentials return `401` or `403`.

The UI source package was not present in the supplied attachment. The traceability analysis therefore uses every named screen/workflow in the written product brief and records this assumption.
