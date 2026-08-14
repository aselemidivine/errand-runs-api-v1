# ADR 0001: Modular monolith

Status: accepted. Deploy one ASP.NET Core process and one SQL Server while enforcing project, schema and contract boundaries. This minimizes operational cost and preserves extraction seams. Distributed messaging, event sourcing and microservices are rejected for the MVP; asynchronous reliability will use a local transactional outbox and background worker.
