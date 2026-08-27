# ErrandRuns API

Production-oriented .NET 10 modular monolith for ErrandRuns. The implemented first increment covers customer and runner accounts, JWT authentication and role authorization, the core multi-stop errand aggregate, SQL Server persistence, health probes, OpenAPI, and architecture/domain tests.

Architecture documentation: [high-level design](docs/architecture/high-level-system-design.md) and [low-level design](docs/architecture/low-level-system-design.md).

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
| POST | `/api/v1/auth/phone-verification/request` | Customer or Runner | Sends/resends a six-digit OTP to the account phone; 60-second cooldown. |
| POST | `/api/v1/auth/phone-verification/verify` | Customer or Runner | Verifies the OTP challenge and marks the account phone confirmed. |
| GET/POST | `/api/v1/users/me/locations` | Customer or Runner | Lists or creates saved home/work/favorite delivery locations. |
| GET/PUT/DELETE | `/api/v1/users/me/locations/{id}` | Location owner | Reads, updates, or removes map and delivery preferences. |
| POST | `/api/v1/users/me/locations/{id}/default` | Location owner | Makes a location the default home base. |
| POST | `/api/v1/errands` | Customer | Creates a multi-stop errand. The request must contain two or more stops and at least one `Delivery` stop. |
| GET | `/api/v1/errands/categories` | Customer | Returns the grocery, laundry, pharmacy, document, and custom UI categories. |
| GET | `/api/v1/errands` | Customer | Returns paged active errands, history, or both for the signed-in customer. |
| GET | `/api/v1/errands/{id}` | Customer owner | Returns route stops, requested items, instructions, and estimate details. |
| GET | `/api/v1/errands/{id}/estimate` | Customer owner | Returns the server-calculated service fee and total estimate. |
| GET | `/api/v1/errands/{id}/tracking` | Customer owner | Returns stop progress and the assigned runner ID. |
| POST | `/api/v1/errands/{id}/cancel` | Customer owner | Cancels an errand that has not already completed or been cancelled. |
| POST | `/api/v1/errands/{id}/confirm-completion` | Customer owner | Confirms receipt after every stop has been completed. |
| POST | `/api/v1/errands/{id}/match` | Customer owner | Assigns the highest-ranked available runner. The errand must be in `PaymentConfirmed`. |
| POST | `/api/v1/errands/{id}/accept` | Assigned runner | Accepts an assigned errand. |
| POST | `/api/v1/errands/{id}/stops/{stopId}/start` | Assigned runner | Starts the next pending stop after the runner has begun their journey. |
| POST | `/api/v1/errands/{id}/stops/{stopId}/complete` | Assigned runner | Completes the active stop. |
| GET | `/api/v1/runners/me/dashboard` | Runner | Returns status, availability, rating, jobs, and withdrawable balance. |
| POST | `/api/v1/runners/me/verification/submit` | Runner | Submits an Applicant profile for operational verification. |
| PUT | `/api/v1/runners/me/availability` | Verified runner | Goes online or offline for matching. |
| GET | `/api/v1/runners/me/jobs` | Runner | Returns assigned active jobs or completed history. |
| GET | `/api/v1/runners/me/jobs/{id}` | Assigned runner | Returns route details and expected earnings. |
| POST | `/api/v1/runners/me/jobs/{id}/accept` | Assigned runner | Accepts the job. |
| POST | `/api/v1/runners/me/jobs/{id}/decline` | Assigned runner | Declines the job and returns the runner to Available. |
| POST | `/api/v1/runners/me/jobs/{id}/start-journey` | Assigned runner | Starts travel to the first stop. |
| GET | `/api/v1/runners/me/earnings` | Runner | Returns available balance and ledger transactions. |
| GET/PUT | `/api/v1/runners/me/payout-account` | Verified runner | Reads or verifies tokenized Nigerian bank details. |
| POST | `/api/v1/runners/me/payouts` | Verified runner | Submits an idempotent Paystack withdrawal. |
| POST | `/api/v1/payments/webhooks/paystack` | Signed Paystack request | Reconciles successful, failed, and reversed runner transfers. |
| GET | `/api/v1/notifications` | Customer or Runner | Returns paged notifications and unread count. |
| POST | `/api/v1/notifications/{id}/read` | Notification owner | Marks a notification read. |
| POST | `/api/v1/conversations/errands/{errandId}` | Errand participant | Opens the assigned errand conversation. |
| GET | `/api/v1/conversations/{id}` | Conversation participant | Reads persisted messages. |
| POST | `/api/v1/conversations/{id}/messages` | Conversation participant | Sends a persisted message. |
| POST | `/api/v1/calls` | Conversation participant | Starts a voice-call session. |
| POST | `/api/v1/calls/{id}/answer` | Call recipient | Answers a ringing call. |
| POST | `/api/v1/calls/{id}/decline` | Call recipient | Declines a ringing call. |
| POST | `/api/v1/calls/{id}/end` | Call participant | Ends or cancels a call. |
| GET | `/health/live` | None | Process liveness probe. |
| GET | `/health/ready` | None | API and database readiness probe. |

Passwords require at least 8 characters, with uppercase, lowercase, and numeric characters. New runner accounts have an `Applicant` runner status and cannot be matched until the future verification workflow makes them available. Domain-rule failures return RFC 7807 Problem Details with `409`; missing resources return `404`; missing or insufficient credentials return `401` or `403`.

Phone verification uses a six-digit, account-bound challenge. Codes expire after 10 minutes, resend is available after 60 seconds, and five incorrect attempts invalidate the challenge. Development responses contain `developmentCode` for Swagger testing; production sends the code through the configured Termii DND SMS route and never returns it. Saved locations store the map pin, address, landmark, gate/delivery instructions, favorite/default flags, and preferred errand categories. A user can store up to 20 locations and has at most one default home base.

Runner earnings are credited only when the customer confirms a completed errand. The merchandise budget is excluded: the runner receives the configured `RunnerPayments:RunnerPercent` of the service fee (80% by default). Withdrawals include the configured fee (NGN 50 by default), require an `Idempotency-Key`, and store only Paystack's recipient token plus the account-number suffix. Signed Paystack webhooks mark transfers paid; failed or reversed transfers restore the ledger balance.

The UI source package was not present in the supplied attachment. The traceability analysis therefore uses every named screen/workflow in the written product brief and records this assumption.
