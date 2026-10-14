# EV Charging System Design Lab

A system-design lab modeling an EV charging platform with a .NET 10/C# backend
and a React/TypeScript frontend for drivers and backoffice operator users.

## Features

- Drivers discover stations, reserve connectors, start and stop charging, follow
  session progress, and download session summaries.
- Operator users maintain station information and media, monitor sessions, investigate
  uncertain command outcomes, and request reconciliation.
- Synchronous and queued charging-provider simulators expose different APIs and
  controllable faults.
- Notification delivery captures local email in a dedicated Mailpit sink with
  persisted attempts; external-provider integration is a separate exercise.
- One frontend provides separate driver and operator areas.

## Architecture

- Aspire application modeling and lab orchestration with local processes and
  containers, plus targeted Cloudflare and Azure service exercises.
- Authentication, resource authorization, API gateway, load balancing, and replicas.
- PostgreSQL read/write routing and Npgsql connection pools, with managed
  replication, PgBouncer, and database failover in the Azure exercise.
- MongoDB document storage, Valkey caching, Elasticsearch, and rebuildable read models.
- RabbitMQ work queues and publish/subscribe, Kafka observation streams, and
  Wolverine inbox/outbox and durable scheduling.
- External provider adapters, telemetry processing, and reconciliation.
- Cloudflare R2 object storage, media processing, generated documents, and
  Cloudflare CDN delivery.
- Observability, fault injection, bounded load testing, and failure recovery.

## Resilience

- Transactional reservation checks, idempotent processing, and outbox publishing.
- Reconciliation of charging commands against observed charger state.
- Explicit read-model freshness, pending operations, and reconnect recovery.
- Fault scenarios: timeouts, duplicates, lost responses, crashes, outages, and
  replication lag.

## Requirements

| Tool | Requirement |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | 10.0.400 or a later stable 10.0.4xx patch ([global.json](global.json)) |
| [Aspire CLI](https://aspire.dev/get-started/install-cli/) | 13.6 |
| [Node.js](https://nodejs.org/en/about/previous-releases) | 24 LTS with npm |
| [Docker](https://docs.docker.com/get-started/get-docker/) | CLI 25+ and a running Linux container engine |
| [Git](https://git-scm.com/downloads) | Repository checkout and version control |

- Windows Docker requires
  [WSL 2 or Hyper-V with virtualization](https://docs.docker.com/desktop/setup/install/windows-install/).
- Network access to NuGet, npm, and container registries.
- Local runs need no cloud accounts. Hosted
  [profiles](docs/lab.md#21-execution-profiles) require Cloudflare and/or Azure.

## Local development

1. Start Docker Engine with Linux container support.
2. Start the lab:

   ```sh
   aspire run --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --launch-profile http
   ```

3. Open the dashboard's `frontend` HTTP endpoint.
4. Stop with `Ctrl+C`; named data volumes persist.

- Starts application hosts, simulators, and dependency containers.
- Frontend and API share the browser origin.
- Replica identity: `/api/diagnostics/instance` and `X-Gateway-Instance`.
- Backend health: `/health` and `/alive`.
- [Routing and execution profiles](docs/lab.md#12-application-wiring).

### Management tools

- Access: dashboard **Manage** links or resource HTTP endpoints.
- Local only: loopback endpoints, excluded from deployment.
- Credentials: local Aspire resource details.

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
| RustFS Console | Object storage |

- Valkey Admin **Add Connection**: `valkey:6379`, user `default`,
  `valkey-password`, TLS disabled.
- RabbitMQ login: configured resource credentials.

### Driver registration

Choose **Sign in**, then **Register** on the identity-provider page. Register with
an email address, then open the verification email through the
dashboard's `registration-mail` HTTP endpoint and set a password. Completing
verification and sign-in creates the required driver profile. Later sign-ins reuse it.

An existing Keycloak realm is retained across restarts. Apply registration and SMTP
changes through its Admin Console; the source configuration is
[the realm definition](infra/keycloak/evcharging-realm.json), and SMTP connection
values come from the Keycloak resource environment. Restart does not reimport
an existing realm. Account-email changes are a separate delivery task.

### Detached execution

1. Start:

   ```sh
   aspire start --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --launch-profile http --non-interactive
   ```

2. Inspect resources and endpoints:

   ```sh
   aspire describe --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj
   ```

3. Stop:

   ```sh
   aspire stop --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --non-interactive
   ```

### Optional replica overrides

```sh
dotnet user-secrets set "Parameters:gateway-instances" "3" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:api-instances" "3" --project src/EvCharging.AppHost
```

- Store secrets in user-secrets or environment configuration, never tracked settings.
- Aspire persists generated dependency credentials in AppHost user-secrets.

### Charging-data reset

1. Stop API replicas and workers in Aspire Dashboard before resetting data.
2. Start the local reset resource:

   ```sh
   aspire resource lab-data-reset start --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --non-interactive
   ```

3. Wait for `lab-data-reset` to finish, then restart the stopped resources.

- [Reset scope and synthetic fixtures](docs/lab.md#211-synthetic-fixtures).

### Backend checks

1. Remove unused `using`s and apply formatting:

   ```sh
   dotnet format style EvChargingSystemDesign.slnx --no-restore --diagnostics IDE0005 --severity info
   dotnet format EvChargingSystemDesign.slnx --no-restore
   ```

2. Verify cleanup, formatting, and build:

   ```sh
   dotnet format style EvChargingSystemDesign.slnx --no-restore --diagnostics IDE0005 --severity info --verify-no-changes
   dotnet format EvChargingSystemDesign.slnx --no-restore --verify-no-changes
   dotnet build EvChargingSystemDesign.slnx --no-restore
   ```

### Frontend checks

1. Restore dependencies:

   ```sh
   npm --prefix src/EvCharging.Web ci
   ```

2. Check formatting and build:

   ```sh
   npm --prefix src/EvCharging.Web run format:check
   npm --prefix src/EvCharging.Web run build
   ```

- Apply formatting fixes: `npm --prefix src/EvCharging.Web run format`.

## Tests

### Gateway session tests

Requires a running Docker Linux container engine. Testcontainers starts and removes
an isolated PostgreSQL container.

```sh
dotnet test tests/EvCharging.Gateway.Tests
```

### Database access tests

- Isolated PostgreSQL container; requires Docker.
- Verify charging/authentication schema isolation and runtime-role permissions.

```sh
dotnet test tests/EvCharging.Persistence.Tests
```

### Notification worker tests

Requires Docker for delivery-state integration tests.

```sh
dotnet test tests/EvCharging.NotificationWorker.Tests
```

### Frontend component tests

Supply the browser timezone fixture through the process environment before
starting either mode:

```powershell
$env:TZ = "Asia/Tashkent"
```

| Mode | Command |
|---|---|
| Single run | `npm --prefix src/EvCharging.Web run test` |
| Watch | `npm --prefix src/EvCharging.Web run test:watch` |

### End-to-end tests

- Isolated Aspire application and disposable container data.
- Require Docker, frontend dependencies, and Chromium.
- Supply `TZ` through the process environment for the browser-time booking journey,
  as shown under [frontend component tests](#frontend-component-tests).

1. Build the test project:

   ```sh
   dotnet build tests/EvCharging.E2E.Tests
   ```

2. Install Chromium (first run):

   ```sh
   pwsh tests/EvCharging.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

3. Run the tests:

   ```sh
   dotnet test tests/EvCharging.E2E.Tests
   ```

- Backend only: `--filter "Category=Api"`.
- Playwright workflow: `--filter "Category=Browser"`.

## Documentation

- [Backend development guidelines](docs/backend-guidelines.md)
- [Architecture](docs/architecture.md)
- [Domain reference](docs/domain.md)
- [External integration contracts](docs/integrations.md)
- [Lab setup and verification](docs/lab.md)
- [Implementation plan](docs/implementation-plan.md)
