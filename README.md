# EV Charging System Design Lab

A system-design lab modeling an EV charging platform with a .NET 10/C# backend
and a React/TypeScript frontend for drivers and backoffice operator users.

The system connects station discovery, connector reservations, charging control,
and live session tracking with operator workflows for station management and
failure recovery.

## Features

- Drivers discover stations, reserve connectors, start and stop charging, follow
  session progress, and download session summaries.
- Operator users maintain station information and media, monitor sessions, investigate
  uncertain command outcomes, and request reconciliation.
- Synchronous and queued charging-provider simulators expose different APIs and
  controllable faults.
- Notification delivery uses a third-party service simulator locally and a
  Cloudflare Email Service adapter for the hosted exercise.
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
- External provider adapters, telemetry processing, and reconciliation.
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

Provider simulators and fault scenarios cover timeouts, duplicate events, lost
responses, worker crashes, dependency outages, replication lag, and recovery.

## Requirements

| Tool | Requirement |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | 10.0.400 or a later stable patch in the 10.0.4xx feature band, as configured in [global.json](global.json) |
| [Aspire CLI](https://aspire.dev/get-started/install-cli/) | 13.6, matching the AppHost SDK and hosting packages |
| [Node.js](https://nodejs.org/en/about/previous-releases) | 24 LTS with npm for frontend tooling |
| [Docker](https://docs.docker.com/get-started/get-docker/) | Docker CLI 25.0.0 or newer and running Docker Engine with Linux container support; Docker Desktop on Windows or macOS |
| [Git](https://git-scm.com/downloads) | Repository checkout and version control |

On Windows, Docker Desktop requires a supported WSL 2 or Hyper-V backend with
hardware virtualization enabled. See the [Windows installation requirements](https://docs.docker.com/desktop/setup/install/windows-install/).
Dependency restore and image pulls require access to NuGet, npm, and container
registries. Aspire SDK and hosting dependencies are restored through NuGet.

The local lab runs without cloud accounts. Hosted exercises additionally require
a Cloudflare account/domain for R2, CDN, or email delivery, or an Azure subscription
for managed PostgreSQL, depending on the
[execution profile](docs/lab.md#21-execution-profiles).

## Local development

```sh
aspire run --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --launch-profile http
```

The AppHost starts the frontend, gateway/API replicas, workers, and third-party
service simulators as local processes. Open the `frontend` HTTP endpoint from
the Aspire dashboard. The frontend and API share this browser origin.
`/api/diagnostics/instance` and the `X-Gateway-Instance` response header identify
the selected local replicas.
`/health` and `/alive` on backend hosts expose development health checks.

Container-backed milestones require Docker Engine to be running.

Stop with `Ctrl+C`.

Local routing and execution profiles are described in the
[lab reference](docs/lab.md#12-application-wiring).

Detached execution:

```sh
aspire start --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --launch-profile http --non-interactive
aspire describe --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj
aspire stop --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --non-interactive
```

Optional local replica overrides and development secret storage:

```sh
dotnet user-secrets set "Parameters:gateway-instances" "3" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:api-instances" "3" --project src/EvCharging.AppHost
```

Secret values belong in this local store or environment configuration, never
in tracked settings.

Frontend dependency restore and checks:

```sh
npm --prefix src/EvCharging.Web ci
npm --prefix src/EvCharging.Web run format:check
npm --prefix src/EvCharging.Web run format
npm --prefix src/EvCharging.Web run typecheck
npm --prefix src/EvCharging.Web run build
```

## Documentation

- [Architecture](docs/architecture.md)
- [Domain reference](docs/domain.md)
- [External integration contracts](docs/integrations.md)
- [Lab setup and verification](docs/lab.md)
- [Implementation plan](docs/implementation-plan.md)
