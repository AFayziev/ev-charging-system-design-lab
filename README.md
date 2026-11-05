# EV Charging System Design Lab

A system-design lab modeling an EV charging platform with a .NET 10/C# backend
and a React/TypeScript frontend for drivers and backoffice operator users.

## Features

- Drivers discover stations, reserve connectors, start and stop charging, and
  follow session progress.
- Operator users maintain station information and media, monitor sessions, investigate
  uncertain command outcomes, and request reconciliation.
- WattHarbor and AmpTrail lab providers expose different APIs and
  controllable faults.
- Notification delivery captures local email in a dedicated Mailpit sink with
  persisted attempts; Amazon SES is an optional external provider.
- One frontend provides separate driver and operator areas.

## Architecture

- Aspire application modeling and lab orchestration with local processes and
  containers, plus targeted Cloudflare and Azure service exercises.
- Authentication, resource authorization, API gateway, load balancing, and replicas.
- PostgreSQL read/write routing and Npgsql connection pools, with managed
  replication, PgBouncer, and database failover in the Azure exercise.
- MongoDB document store, Valkey caching, Elasticsearch, and derived read models.
- RabbitMQ work queues and publish/subscribe, Kafka observation streams, and
  Wolverine inbox/outbox and durable scheduling.
- Transactional reservation checks and idempotent processing.
- External provider integrations, telemetry processing, and reconciliation of
  charging commands against observed charger state.
- Explicit read-model freshness, pending operations, and reconnect recovery.
- Cloudflare R2 object store, media processing, and Cloudflare CDN delivery.
- Observability, bounded load testing, and fault injection for timeouts,
  duplicates, lost responses, crashes, outages, and replication lag.

## Requirements

| Tool | Requirement |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | 10.0.400 or a later stable 10.0.4xx patch ([global.json](global.json)) |
| [Aspire CLI](https://aspire.dev/get-started/install-cli/) | 13.6 |
| [Node.js](https://nodejs.org/en/about/previous-releases) | 24 LTS with npm |
| [Docker](https://docs.docker.com/get-started/get-docker/) | CLI 25+ and a running Linux container engine |
| [Git](https://git-scm.com/downloads) | Repository checkout and version control |
| [PowerShell](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) | 7+ for the combined launcher; the [AppHosts can also run without it](docs/development.md#apphosts-without-the-launcher) |

- Windows Docker requires
  [WSL 2 or Hyper-V with virtualization](https://docs.docker.com/desktop/setup/install/windows-install/).
- Network access to NuGet, npm, and container registries.
- Local runs need no cloud accounts. Hosted
  [profiles](docs/lab.md#21-execution-profiles) require Cloudflare and/or Azure.

## Run locally

1. Start Docker Engine with Linux container support.
2. Start the lab from the repository root:

   ```powershell
   pwsh -File tools/dev.ps1
   ```

3. Open the displayed `frontend` URL. Each AppHost has its own dashboard.
4. Press Enter or `Ctrl+C` to stop AppHosts started by the launcher; pre-existing
   AppHosts remain running. Named data volumes persist.

The launcher starts the lab providers and the platform, and supplies the
providers' dynamic URLs to the platform for that run. It reuses running AppHosts
when their provider URLs match. If a running platform has stale provider URLs,
stop its AppHost and rerun the launcher.

```powershell
pwsh -File tools/dev.ps1 -Detach    # Leave newly started AppHosts running.
pwsh -File tools/dev.ps1 -Isolated  # Start with Aspire's isolated ports and secrets.
```

`-Isolated` applies to newly started AppHosts; existing sessions are reused. Use
isolated startup in Git worktrees. Detached sessions are stopped with the
[individual AppHost commands](docs/development.md#detached-execution).

### Sign in

The lab seeds a `driver` and an `operator` account. The operator is assigned to
part of the synthetic station catalogue. Their passwords are generated on first
start and shown as parameters in the platform dashboard, or read with:

```sh
aspire secret get Parameters:synthetic-driver-password --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj
aspire secret get Parameters:synthetic-operator-password --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj
```

To register a new driver instead, choose **Sign in**, then **Register**, and open
the verification email through the dashboard's `registration-mail` endpoint.

### Management tools

Every store and broker has a local management UI, opened from the dashboard's
**Manage** links or the resource HTTP endpoints. They listen on loopback only and
use the credentials in each resource's details.

| Tool | Resource |
|---|---|
| pgAdmin | Application and identity PostgreSQL servers |
| Keycloak Admin Console | Identity provider |
| Mailpit (`registration-mail`) | Captured account-verification emails |
| Mailpit (`notification-mail`) | Captured reservation emails |
| Mongo Express | MongoDB |
| Kafbat UI | Kafka |
| Valkey Admin | Valkey |
| Elasticvue | Elasticsearch |
| RabbitMQ Management | RabbitMQ |
| RustFS Console | Object store |

- Valkey Admin **Add Connection**: `valkey:6379`, user `default`,
  `valkey-password`, TLS disabled.
- RabbitMQ login: configured resource credentials.

## Documentation

- [Architecture](docs/architecture.md)
- [Domain reference](docs/domain.md)
- [External integration contracts](docs/integrations.md)
- [Lab setup and verification](docs/lab.md)
- [Delivery plan](docs/delivery-plan.md)
- [Development](docs/development.md): checks, tests, manual AppHost startup, data reset, and optional hosted profiles
- [Backend development guidelines](docs/backend-guidelines.md)
