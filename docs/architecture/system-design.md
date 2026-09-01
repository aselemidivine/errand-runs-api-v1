# System design

For the current comprehensive designs, see the [UML diagram set](uml-diagrams.md), [High-level system design](high-level-system-design.md), and [Low-level system design](low-level-system-design.md). This file remains a short architectural summary.

ErrandRuns is a deployable modular monolith with four dependency layers: API → Infrastructure/Application → Domain. Business invariants live in aggregates and application services; HTTP endpoints contain transport concerns only. SQL schemas provide module ownership (`app`, `runners`, `payments`) while contracts mediate module interaction.

The first vertical slice implements the high-risk backbone: ordered multi-stop errands, guarded state transitions, matching, assigned-runner execution, pricing, payment model, persistence, authentication middleware and ownership checks. Later modules should follow the same slice pattern rather than introduce a shared generic repository.

Synchronous domain changes commit transactionally. Notifications and provider callbacks should use a transactional outbox in the next increment. Periodic location writes are preferred initially; SignalR is deferred until product load warrants it.

## Assumptions

- No visual design assets were attached, so screen coverage derives from the supplied screen names and narrative.
- Currency defaults to NGN at the boundary but remains a value on every `Money` instance.
- An errand requires at least one actionable stop. Single-location errands do not require a delivery stop; multi-location errands retain unique ordered stop sequences.
- Matching currently ranks eligible runners by rating and completed work; distance/service-zone filtering is an extension point.
