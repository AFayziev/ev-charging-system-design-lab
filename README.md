# EV Charging System Design

A system-design lab modeling an EV charging platform with a .NET 10/C# backend
and a React/TypeScript frontend for drivers and charging-station operators.

The system connects station discovery, connector reservations, charging control,
and live session tracking with operator workflows for station management and
failure recovery.

## Features

- Drivers discover stations, reserve connectors, start and stop charging, follow
  session progress, and download session summaries.
- Operators maintain station information and media, monitor sessions, investigate
  uncertain command outcomes, and request reconciliation.
- Two simulated charging operators expose different APIs and controllable faults.
- One frontend provides separate driver and operator areas.

## Architecture

- Aspire application modeling and lab orchestration with local processes and
  containers, plus targeted Cloudflare and Azure service exercises.
- Authentication, resource authorization, API gateway, load balancing, and replicas.
- PostgreSQL primary/read-replica routing, Npgsql connection pools, PgBouncer,
  and a provider-managed database failover exercise.
- MongoDB document storage, Valkey caching, Elasticsearch, and rebuildable read models.
- RabbitMQ work queues and publish/subscribe, Kafka observation streams, and
  Wolverine inbox/outbox and durable scheduling.
- External operator adapters, telemetry processing, and reconciliation.
- Cloudflare R2 object storage, media processing, generated documents, and
  Cloudflare CDN delivery.
- Observability, fault injection, bounded load testing, and failure recovery.

## Resilience

Charging commands and observed charger state are tracked separately so uncertain
external outcomes can be reconciled. Transactional checks protect reservations,
while idempotent processing and outbox publishing support asynchronous workflows.

Read models have explicit freshness requirements. The frontend shows pending
operations, stale observations, and connection interruptions, then refreshes
authoritative state after reconnecting.

Operator simulators and fault scenarios cover timeouts, duplicate events, lost
responses, worker crashes, dependency outages, replication lag, and recovery.

## Documentation

- [Architecture](docs/architecture.md)
- [Domain reference](docs/domain.md)
- [External integration contracts](docs/integrations.md)
- [Lab profiles and verification](docs/lab.md)
- [Implementation plan](docs/implementation-plan.md)
