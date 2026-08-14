# Database design

SQL Server is the system of record. Each module owns a schema: `identity`, `users`, `app` (errands), `runners`, `kyc`, `matching`, `tracking`, `payments`, `memberships`, `notifications`, `reviews`, `support`, and `admin`. The current migration model includes Errands/ErrandStops, RunnerProfiles and Payments; remaining tables are the next slices and are not claimed as implemented.

Money uses `decimal(18,2)` plus ISO currency. Coordinates use `decimal(9,6)`. All timestamps are `DateTimeOffset` UTC. Financial references/idempotency keys are unique. Errand stop sequence is unique per errand. Mutable critical aggregates carry `rowversion`. Foreign keys default to restrict for financial/audit data; dependent route details cascade only with their draft errand. Read paths project with `AsNoTracking`; pagination is capped at 100.

Future indexes: `(CustomerId, CreatedAt)`, `(RunnerId, Status)`, matching `(ZoneId, Status)`, tracking `(SessionId, RecordedAt DESC)`, review unique `(ErrandId, ReviewerId, Direction)`, webhook unique `(Provider, EventId)`, payout unique idempotency key. Spatial indexes can replace bounded-coordinate filtering when matching volume justifies SQL geography.
