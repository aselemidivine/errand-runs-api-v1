# UI-to-backend analysis

> Limitation: the supplied attachment contained the written brief only; no UI image/source package was available. This traceability map therefore covers all screens explicitly named or described in that brief. Visual-only fields may require revision when design assets are supplied.

| UI screen/capability | Journey | Module / domain | API | Tables | Authorization | Events / side effects |
|---|---|---|---|---|---|---|
| Register, email/phone login | Establish account | Identity / User, Credential | `POST /auth/register`, `/login` | Users, Roles | Anonymous + rate limit | verification OTP, audit |
| Six-digit OTP, 2FA | Verify phone/security challenge | Identity / OtpChallenge | `/auth/otp/send`, `/verify`, `/2fa` | OtpChallenges | challenge owner; throttled | SMS, revoke challenge |
| Password reset/security | Recover and secure account | Identity / ResetToken, RefreshToken | `/auth/password/*`, `/refresh`, `/logout` | RefreshTokens, SecurityEvents | token owner | rotation/revocation, audit |
| Customer home dashboard | See account and active work | Users + Errands | `GET /users/me`, `/errands` | CustomerProfiles, Addresses, Errands | Customer owner | none |
| Saved locations/profile | Manage reusable details | Users / Address, Preferences | `/users/me/addresses` | Addresses, Preferences | Customer owner | audit sensitive changes |
| Select errand category | Choose controlled workflow | Errands / ErrandCategory | `GET /errand-categories` | ErrandCategories | Authenticated | none |
| Grocery list | Build shopping request | Errands / GroceryRequest, GroceryItem | `/errands` category payload | GroceryRequests, GroceryItems | Customer owner | estimate recalculation |
| Laundry request | Schedule cleaning pickup | Errands / LaundryRequest | `/errands` category payload | LaundryRequests, LaundryItems | Customer owner | scheduling notification |
| Pharmacy request/prescription | Request fulfilment, upload privately | Errands + Files / PharmacyRequest | `/errands`, `/files/prescriptions` | PharmacyRequests, FileMetadata | Owner; assigned runner scoped | scan file, audit access |
| Custom/document errand | Describe constrained work | Errands / custom details | `/errands` | Errands, Attachments | Customer owner | policy review if flagged |
| Plan your route | Add ordered multi-stop route | Errands / Errand, ErrandStop, GeoPoint | `POST /errands` | Errands, ErrandStops | Customer owner | request estimate |
| Review estimate | Review server price | Errands / Estimate, PricingPolicy | `/errands/{id}/estimate` | Estimates, PricingRules | Customer owner | estimate expiry |
| Secure checkout | Pay estimate | Payments / Payment, Transaction | `/errands/{id}/payments` | Payments, Transactions | Customer owner; idempotency | provider intent |
| Payment callback | Provider confirms server-side | Payments / Webhook | `/payments/webhooks/{provider}` | Webhooks, Transactions | Signed provider request | verify, confirm errand, audit |
| Finding your Runner | Initiate/observe matching | Matching / MatchingRequest | `/errands/{id}/match` | MatchingRequests | Paid errand owner/system | ranked search, notification |
| Runner matched | Display assignment | Matching / RunnerAssignment | `/errands/{id}` | RunnerAssignments | Owner/assigned runner | notify both parties |
| Runner dashboard/jobs | Toggle availability, see offers | Runners / RunnerProfile, Availability | `/runners/me`, `/runners/jobs` | RunnerProfiles, Availability | Verified runner | matching eligibility |
| Runner accepts job | Claim one assignment | Matching / Assignment | `/errands/{id}/accept` | RunnerAssignments | Assigned runner; idempotent | runner busy, notify customer |
| Live tracking | Observe location/ETA/progress | Tracking / Session, LocationUpdate | `/errands/{id}/tracking` | TrackingSessions, Locations | Owner/assigned runner/support | optional push update |
| Stop 2 of 3 | Progress in sequence | Errands / ErrandStop | `/stops/{id}/start`, `/complete` | ErrandStops, StatusHistory | Assigned runner | status event, notification |
| Add receipt photo | Record actual purchase | Errands + Files / Receipt | `/errands/{id}/receipts` | Receipts, FileMetadata | Assigned runner; owner read | file scan, price approval |
| Substitution approval | Customer approves item change | Errands / Substitution | `/substitutions/{id}/decision` | Substitutions | Errand owner | notify runner |
| Stop 3 delivery/photo | Provide proof | Tracking / DeliveryConfirmation | `/errands/{id}/delivery-proof` | DeliveryProofs, FileMetadata | Assigned runner; owner read | signed URL, notify |
| Confirm completion | Customer closes work | Errands / state machine | `/errands/{id}/complete` | Errands, StatusHistory | Customer owner | earnings ledger, review prompts |
| Rate & review (both sides) | Review counterparty | Reviews / Review | `POST /reviews` | Reviews, ReviewTags | Completed participants | aggregate rating, moderation |
| Membership pricing | Compare/subscribe | Memberships / Plan, Subscription, Benefit | `/memberships`, `/subscribe` | Plans, Benefits, Subscriptions | Authenticated/customer owner | payment, renewal notification |
| Runner identity verification | Submit government ID | KYC / Verification, Document | `/runners/me/verifications` | Verifications, Documents | Runner owner; KYC admin review | scan, audit, notification |
| Vehicle verification | Establish capability | KYC / Vehicle | `/runners/me/vehicles` | Vehicles, VerificationDocuments | Runner owner; KYC admin | matching capability update |
| Earnings | Inspect auditable balance | Runner Finance / LedgerEntry | `/runners/me/earnings` | RunnerLedger | Runner owner | none |
| Payout settings | Tokenized bank destination | Runner Finance / PayoutAccount | `/runners/me/payout-account` | PayoutAccounts | Runner owner + re-auth | provider validation, audit |
| Payout/instant payout | Request disbursement | Runner Finance / Payout | `/runners/me/payouts` | Payouts, Ledger | Runner owner; idempotent | provider transfer, notification |
| Help & FAQ | Self-service help | Support / KnowledgeArticle | `/support/articles` | KnowledgeArticles | Public/published only | none |
| Contact support/live chat concept | Open and update case | Support / Ticket | `/support/tickets` | Tickets, TicketMessages | Requester/support agent | notification |
| Report item/payment problem | Raise dispute | Disputes / Dispute | `/errands/{id}/disputes` | Disputes, Evidence | Participant/support | hold funds, audit |
| Admin operations | Verify, refund, suspend, moderate | Administration / audited commands | `/admin/*` | AuditLogs + owned module tables | Explicit Admin/Support policies | immutable audit entries |

## Key validation and boundaries

Amounts and prices are server-derived; runner actuals require receipt evidence and customer approval where needed. Coordinates have numeric range checks. Scheduling accepts timezone-aware input and stores UTC. Uploaded prescription/KYC content uses private object storage, signature/MIME/size checks, malware scanning and audited short-lived access. Collection endpoints are capped and paged. Two runner accepts, duplicate provider callbacks, reviews, refunds and payouts are guarded by state, concurrency tokens and unique idempotency constraints.
