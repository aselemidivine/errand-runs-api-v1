# Module boundaries

| Module | Owns | May consume |
|---|---|---|
| Identity | users, credentials, OTPs, refresh tokens, roles | Notifications |
| Users | customer profiles, addresses, preferences | Identity user id |
| Errands | errands, stops, category details, estimates, transitions | Membership pricing policy |
| Runners/KYC | runner profile, availability, zones, vehicles, verification metadata | Identity user id, private storage |
| Matching | matching requests and assignments | read contracts from Errands/Runners |
| Tracking | sessions, locations, delivery proof | assignment contract |
| Payments | payments, transactions, refunds, webhooks | errand payment contract |
| Memberships | plans, benefits, subscriptions | Payments |
| Reviews | bilateral reviews and moderation | completed-errand contract |
| Support/Disputes | tickets, disputes | user and errand references |
| Administration | audited commands, no table ownership bypass | module command contracts |

Modules never expose `IQueryable` or another module's EF configuration. Cross-module foreign identifiers are stable GUIDs; transactional workflows use application orchestration and, when asynchronous side effects are added, an outbox.
