# ErrandRuns high-level system design

![ErrandRuns UML architecture overview](diagrams/errandruns-uml-architecture.png)

The image above is the quick visual overview. The diagrams below break each subsystem down in more detail.

## 1. Purpose and scope

ErrandRuns is a Lagos-focused marketplace in which customers submit multi-stop errands, verified runners execute them, and the platform coordinates identity, matching, progress, communications, payment settlement, and runner payouts.

The current backend is a .NET 10 modular monolith. It is one deployable process, but code boundaries and SQL schemas separate the major business capabilities. This keeps the first production version operationally simple while preserving clear extraction points if individual modules later need independent scaling.

This document describes the implemented backend and labels future components explicitly.

## 2. System context

```mermaid
flowchart LR
    Customer[Customer mobile or web app]
    Runner[Runner mobile app]
    Ops[Operations and support - future]
    API[ErrandRuns API]
    Paystack[Paystack]
    Push[Firebase push - configured, adapter future]
    SMS[Termii SMS - configured, adapter future]
    Maps[Google Maps - configured, adapter future]
    KYC[Smile Identity - configured, adapter future]
    Storage[Private blob storage - configured, adapter future]

    Customer -->|HTTPS and SignalR| API
    Runner -->|HTTPS and SignalR| API
    Ops -.->|HTTPS| API
    API -->|account verification and transfers| Paystack
    Paystack -->|signed webhooks| API
    API -.-> Push
    API -.-> SMS
    API -.-> Maps
    API -.-> KYC
    API -.-> Storage
```

Solid lines are implemented interactions. Dashed lines show configured extension points whose complete business adapters are not yet implemented.

## 3. Container view

```mermaid
flowchart TB
    subgraph Clients
        C[Customer client]
        R[Runner client]
    end

    subgraph Backend[ErrandRuns deployment]
        HTTP[Minimal HTTP APIs]
        HUB[SignalR communications hub]
        APP[Application services]
        DOMAIN[Domain aggregates and policies]
        INFRA[EF Core, Identity and provider adapters]
    end

    DB[(SQL Server)]
    PS[Paystack API]
    OBS[Console logs and health probes]

    C -->|REST and JWT| HTTP
    R -->|REST and JWT| HTTP
    C -->|WebSocket or long polling| HUB
    R -->|WebSocket or long polling| HUB
    HTTP --> APP
    HUB --> APP
    APP --> DOMAIN
    APP --> INFRA
    INFRA --> DB
    INFRA --> PS
    PS -->|HMAC signed webhook| HTTP
    HTTP --> OBS
```

### Container responsibilities

| Container | Responsibility |
|---|---|
| HTTP API | Versioned endpoints, request binding, role policies, rate limiting, Problem Details and OpenAPI |
| SignalR hub | Authenticated real-time notifications, messages, read receipts and WebRTC signaling |
| Application layer | Use-case orchestration, ownership checks, repository/provider contracts and response mapping |
| Domain layer | Errand, runner, money, payment, payout, messaging, notification and call invariants |
| Infrastructure layer | SQL Server persistence, ASP.NET Core Identity and Paystack payout integration |
| SQL Server | Durable system of record, optimistic concurrency, indexes and uniqueness constraints |

## 4. Logical module view

```mermaid
flowchart LR
    Identity[Identity]
    Errands[Customer errands]
    Runners[Runner operations]
    Finance[Payments and runner finance]
    Comms[Notifications and communications]

    Identity -->|user ID and role| Errands
    Identity -->|user ID and role| Runners
    Errands -->|assignment and completion| Runners
    Errands -->|confirmed completion| Finance
    Errands -->|status events| Comms
    Runners -->|job actions| Errands
    Finance -->|earning and payout events| Comms
    Comms -->|participant validation| Errands
```

| Module | Implemented capabilities |
|---|---|
| Identity | Customer/runner registration, email-or-phone login, JWTs, profiles, password change and reset |
| Errands | Categories, items, ordered stops, estimates, ownership, active/history, matching, tracking and completion |
| Runners | Verification submission, availability, assigned jobs, sequential execution and dashboard |
| Finance | Payment model, runner earning ledger, payout accounts, Paystack transfers and reconciliation |
| Communications | Persistent notifications, conversations, messages, read receipts, call lifecycle and real-time events |

## 5. Principal end-to-end flow

```mermaid
sequenceDiagram
    actor Customer
    participant API
    participant Errand as Errand module
    participant Runner as Runner module
    participant Finance
    participant Comms
    actor RunnerApp as Runner

    Customer->>API: Create multi-stop errand
    API->>Errand: Validate route and calculate estimate
    Errand-->>Customer: PendingPayment and estimate
    Note over API,Finance: Customer checkout/provider confirmation is not fully exposed yet
    Customer->>API: Match paid errand
    API->>Runner: Reserve highest-ranked available runner
    API->>Comms: New assignment notification
    Comms-->>RunnerApp: SignalR notification
    RunnerApp->>API: Accept and start journey
    loop Ordered stops
        RunnerApp->>API: Start stop
        RunnerApp->>API: Complete stop
        API->>Comms: Customer progress notification
    end
    API-->>Customer: AwaitingConfirmation
    Customer->>API: Confirm completion
    API->>Finance: Credit runner earning once
    API->>Runner: Increment completed jobs and return Available
    API->>Comms: Earnings notification
```

## 6. Runner payout flow

```mermaid
sequenceDiagram
    actor Runner
    participant API
    participant Ledger
    participant Paystack

    Runner->>API: Save Nigerian bank account
    API->>Paystack: Resolve account and create recipient
    Paystack-->>API: Account name and recipient token
    API->>API: Store token and last four digits only
    Runner->>API: Request payout plus Idempotency-Key
    API->>Ledger: Check available balance
    API->>Paystack: Submit NGN transfer in kobo
    Paystack-->>API: Transfer reference
    API->>Ledger: Record payout debit
    Paystack->>API: Signed transfer webhook
    alt success
        API->>Ledger: Mark payout Paid
    else failed or reversed
        API->>Ledger: Add compensating PayoutReversal credit
    end
```

## 7. Communications architecture

REST is the durable control plane; SignalR is the low-latency delivery plane.

```mermaid
flowchart LR
    Sender[Customer or runner]
    REST[REST communication API]
    SQL[(Communications tables)]
    Hub[SignalR hub]
    Recipient[Other participant]
    WebRTC[Peer-to-peer WebRTC media]

    Sender -->|send message or start call| REST
    REST -->|persist first| SQL
    REST -->|publish event| Hub
    Hub --> Recipient
    Sender <-->|offer, answer and ICE via hub| Recipient
    Sender <-.->|audio via STUN/TURN - external| WebRTC
    Recipient <-.-> WebRTC
```

Messages and call metadata survive disconnects because they are stored before broadcast. Audio is not proxied through the API. Production voice calling requires client WebRTC support and STUN/TURN infrastructure or a telecom provider.

## 8. Deployment topology

### Current local/container deployment

```mermaid
flowchart TB
    Host[Docker host]
    subgraph Host
        APIContainer[ASP.NET Core container - port 8080]
        SQLContainer[SQL Server 2022 container - port 1433]
    end
    APIContainer --> SQLContainer
    APIContainer --> Internet[External providers]
```

The API image is built in a .NET SDK stage and runs as a non-root user in the ASP.NET runtime image. Docker Compose waits for SQL Server health before starting the API and applies migrations only in Development.

### Recommended production deployment

```mermaid
flowchart TB
    DNS[DNS and TLS edge]
    LB[Managed load balancer]
    API1[API instance]
    API2[API instance]
    Backplane[SignalR backplane or managed SignalR]
    SQL[(Managed SQL Server)]
    Secrets[Secret manager]
    Monitor[Logs, metrics and traces]
    Providers[Paystack and future providers]

    DNS --> LB
    LB --> API1
    LB --> API2
    API1 <--> Backplane
    API2 <--> Backplane
    API1 --> SQL
    API2 --> SQL
    Secrets --> API1
    Secrets --> API2
    API1 --> Monitor
    API2 --> Monitor
    API1 --> Providers
    API2 --> Providers
```

Multiple API instances require a SignalR backplane or managed SignalR service; otherwise users connected to different instances will miss live events. SQL Server remains the authoritative state.

## 9. Security boundaries

- JWT bearer authentication identifies a stable GUID and `Customer` or `Runner` role.
- Endpoint role policies provide coarse authorization; application services enforce resource ownership.
- Conversation and call access requires membership in the assigned errand.
- Password hashing and credential verification are delegated to ASP.NET Core Identity.
- Paystack webhooks use an HMAC-SHA512 signature and constant-time comparison.
- Raw bank account numbers are forwarded to Paystack but not stored.
- Sensitive endpoints use fixed-window rate limiting.
- Errors use RFC 7807 responses and suppress internal details for HTTP 500.
- Development-only Swagger, in-memory persistence and simulated payouts must not be enabled in production.

## 10. Availability and failure behavior

| Failure | Current behavior | Recommended evolution |
|---|---|---|
| SQL unavailable | Readiness probe fails; durable requests fail | Managed SQL, backups, retry policy for transient faults |
| Client disconnects | Durable messages remain in SQL | Push notifications for offline recipients |
| SignalR instance loss | Client reconnects; REST can recover state | Managed SignalR/backplane and connection retry |
| Paystack request fails | Payout becomes Failed and no debit remains | Background retry for safe provider failures |
| Transfer later fails | Signed webhook adds compensating credit | Reconciliation job to detect missed webhooks |
| Duplicate payout request | Unique runner/idempotency-key returns original payout | Retain idempotency records per financial policy |
| Concurrent aggregate update | SQL rowversion detects conflict | Map concurrency exceptions to HTTP 409 explicitly |

## 11. Scaling strategy

1. Scale the modular monolith horizontally behind a load balancer.
2. Add a SignalR backplane and transactional outbox before multiple API instances.
3. Add caching only to read-heavy static data such as categories and bank lists.
4. Move location updates to a dedicated tracking store when write volume warrants it.
5. Extract communications or tracking only when independent scaling provides measurable value.
6. Keep identity and financial ledgers strongly consistent even if other modules become asynchronous.

## 12. Known gaps

- Customer checkout initialization and payment-collection webhook flow are not fully exposed.
- Runner approval/admin operations are not implemented; runners can submit verification only.
- Distance, time, zone, complexity and urgency pricing are not calculated.
- Firebase, Termii, maps, KYC and blob-storage options exist, but full adapters remain future work.
- There is no transactional outbox, so a database commit can succeed immediately before a real-time publish fails.
- Real-time geographic tracking and production WebRTC STUN/TURN infrastructure are not included.
- Reviews, disputes, refunds, saved locations and support workflows remain future modules.
