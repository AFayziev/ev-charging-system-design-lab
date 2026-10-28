# EV Charging System Design Lab

A system-design lab modeling an EV charging platform with a .NET 10/C# backend
and a React/TypeScript frontend for drivers and backoffice operator users.

## Features

- Drivers discover stations, reserve connectors, start and stop charging, follow
  session progress, and download session summaries.
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
- External provider integrations, telemetry processing, and reconciliation.
- Cloudflare R2 object store, media processing, generated documents, and
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
| [PowerShell](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) | 7+ for the combined development launcher; independent Aspire commands also work |

- Windows Docker requires
  [WSL 2 or Hyper-V with virtualization](https://docs.docker.com/desktop/setup/install/windows-install/).
- Network access to NuGet, npm, and container registries.
- Local runs need no cloud accounts. Hosted
  [profiles](docs/lab.md#21-execution-profiles) require Cloudflare and/or Azure.

## Local development

1. Start Docker Engine with Linux container support.
2. Start the lab from the repository root:

   ```powershell
   pwsh -File tools/dev.ps1
   ```

3. Open the displayed `frontend` URL. Each AppHost has its own dashboard.
4. Press Enter or `Ctrl+C` to stop AppHosts started by the launcher; pre-existing
   AppHosts remain running. Named data volumes persist.

The launcher discovers the lab providers' dynamic URLs and supplies them to the
platform for that run. It reuses running AppHosts when their provider URLs match.
It starts CLI-managed sessions, including when run from a VS Code terminal.
If a running platform has stale provider URLs, stop its AppHost and rerun the launcher.

```powershell
pwsh -File tools/dev.ps1 -Detach    # Leave newly started AppHosts running.
pwsh -File tools/dev.ps1 -Isolated  # Start with Aspire's isolated ports and secrets.
```

`-Isolated` applies to newly started AppHosts; existing sessions are reused. Use
isolated startup in Git worktrees. Detached sessions are stopped with the
[individual AppHost commands](#detached-execution).

- During local development, Vite serves the UI and proxies `/api`, `/auth`, and
  `/diagnostics` requests to the gateway. These browser requests use the frontend's
  scheme, hostname, and port; the gateway and API run at separate service endpoints.
- Development replica diagnostics: `/api/diagnostics/instance` identifies the API
  instance; the `X-Gateway-Instance` response header identifies the gateway instance.
- HTTP backend hosts expose `/health` and `/alive` at their own service endpoints.
- In Development, each HTTP host exposes Scalar at `/scalar` and its OpenAPI
  document at `/openapi/v1.json`. API links in Aspire open Scalar directly.
- [Routing and execution profiles](docs/lab.md#12-application-wiring).

### Independent AppHost startup

1. Start the lab providers in a separate terminal:

   ```sh
   aspire run --apphost simulators/EvCharging.Providers.AppHost/EvCharging.Providers.AppHost.csproj
   ```

2. Copy the provider HTTP endpoints from its dashboard or `aspire describe`
   into platform configuration. Supply the provider's OAuth token endpoint and
   charging-client secret as well:

   ```sh
   dotnet user-secrets set "Parameters:wattharbor-provider-url" "<WattHarbor HTTP endpoint>" --project src/EvCharging.AppHost
   dotnet user-secrets set "Parameters:amptrail-provider-url" "<AmpTrail HTTP endpoint>" --project src/EvCharging.AppHost
   dotnet user-secrets set "Parameters:wattharbor-provider-token-endpoint" "<provider OAuth token endpoint>" --project src/EvCharging.AppHost
   dotnet user-secrets set "Parameters:wattharbor-provider-client-secret" "<charging-client secret>" --project src/EvCharging.AppHost
   ```

   Refresh these values when the provider endpoints change. Environment variables
   `Parameters__wattharbor-provider-url` and `Parameters__amptrail-provider-url`
   can supply the same settings in automated runs.

   The provider's discovery document identifies its token endpoint; the charging
   client secret is `Parameters:charging-client-secret` in the provider AppHost's
   user secrets. `tools/dev.ps1` discovers and passes these settings automatically,
   without saving a second copy of the credential.

3. Start the platform in another terminal:

   ```sh
   aspire run --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --launch-profile http
   ```

4. Tell the WattHarbor provider where to deliver observations. The provider
   receives its callback target through simulation control; without it, charging
   progress never reaches the platform. Use a `simulation-control` token from the
   provider's token endpoint (client secret `Parameters:simulation-client-secret`
   in the provider AppHost's user secrets), with the platform gateway's HTTP
   endpoint and the platform's callback key (`aspire secret get
   Parameters:wattharbor-callback-key --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj`):

   ```sh
   curl -X PUT "<WattHarbor HTTP endpoint>/simulation/callbacks/observations" \
     -H "Authorization: Bearer <simulation-control token>" -H "Content-Type: application/json" \
     -d '{"url":"<gateway HTTP endpoint>/api/callbacks/wattharbor/observations","apiKey":"<callback key>"}'
   ```

   The target persists in the provider database. `tools/dev.ps1` registers it whenever it starts
   the platform, with a key it chooses for that run.

5. Open the platform dashboard's `frontend` HTTP endpoint.
6. Stop each AppHost with `Ctrl+C`; named data volumes persist.

- Each AppHost manages its own resources and dashboard. Platform startup does not
  start the lab providers.
- The provider AppHost owns the WattHarbor lab provider's PostgreSQL database and
  Keycloak instance. Its client credentials are generated into that AppHost's
  user secrets. The imported [provider realm](infra/keycloak/providers/providers-realm.json)
  separates charging access from simulation controls. Realm imports apply on first
  creation; editing the import file does not update an existing realm.

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
| RustFS Console | Object store |

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
[the realm definition](infra/keycloak/platform/evcharging-realm.json), and SMTP connection
values come from the Keycloak resource environment. Restart does not reimport
an existing realm.

### Detached execution

Start the provider AppHost first, configure its endpoints as above, then start
the platform. The commands below target the platform; use
`simulators/EvCharging.Providers.AppHost/EvCharging.Providers.AppHost.csproj` as
`--apphost` to start, inspect, or stop the lab providers independently.

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

### Optional frontend diagnostics

[Frontend diagnostics](docs/lab.md#19-frontend-diagnostics) are disabled by default and
need no Sentry account. To report from a local run, create a Sentry React project and
supply its DSN (`https://<key>@<host>.sentry.io/<project id>`), an environment, and a release:

```sh
dotnet user-secrets set "FrontendDiagnostics:Enabled" "true" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Dsn" "<DSN>" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Environment" "local" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Release" "local" --project src/EvCharging.AppHost
```

Restart the gateway after changing a setting. Enabled settings without a hosted DSN,
environment, and release fail gateway startup.

Options (`FrontendDiagnostics:*`):

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | `false` | Disabled sends no Sentry telemetry |
| `ErrorSampleRate` | `1` | Fraction of errors sent |
| `TraceSampleRate` | `0` | Fraction of page loads, navigations, and requests traced; `0` sends no traces |
| `LogLevels` | `Warn`, `Error`, `Fatal` | Structured log levels sent |
| `ReplayEnabled` | `false` | Session replay with masked text and blocked media |

Sent: uncaught exceptions, unhandled rejections, and React error-boundary failures, with
environment, release, browser, route, and breadcrumbs (clicks, navigation, request
outcomes). Query strings and fragments are removed from URLs. User data, cookies, headers,
request bodies, stack-frame variables, and form values are never sent.

Events reach Sentry through the gateway tunnel at `/diagnostics/sentry`.

Source maps exist only in a built frontend. Start the launcher with `-Preview` to run the
`frontend` resource from a production build: the `frontend-build` resource runs
`npm run build` after the package install, and `frontend` serves `dist` with `vite preview`.
Without it, `frontend` runs the dev server.

```powershell
pwsh -File tools/dev.ps1 -Preview
```

The mode applies only to a newly started platform; the launcher warns when a reused platform
runs the other mode. Outside the launcher, set `Frontend:Mode` to `preview` in the AppHost
configuration.

Add these to upload source maps during that build (the token is a Sentry organization
auth token); `FrontendDiagnostics:Release` becomes the Sentry release, so build and runtime
match. Omit any of them to build without upload:

```sh
dotnet user-secrets set "Frontend:Sentry:Org" "<organization slug>" --project src/EvCharging.AppHost
dotnet user-secrets set "Frontend:Sentry:Project" "<project slug>" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:sentry-auth-token" "<auth token>" --project src/EvCharging.AppHost
```

Outside Aspire, `vite build` uploads when `SENTRY_UPLOAD=true` with `SENTRY_AUTH_TOKEN`,
`SENTRY_ORG`, `SENTRY_PROJECT`, and `SENTRY_RELEASE` set.

### Optional Amazon SES notification delivery

Notification email is captured in Mailpit by default. To send through
[Amazon SES](docs/lab.md#18-notification-adapters) instead, supply the sender, region and access key. 
The sender must be a verified SES identity. If the sandbox is used, every recipient address must be verified too:

```sh
dotnet user-secrets set "NotificationDelivery:Provider" "AmazonSes" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:ses-sender-email" "<verified sender>" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:ses-region" "<AWS region>" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:ses-access-key-id" "<access key id>" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:ses-secret-access-key" "<secret access key>" --project src/EvCharging.AppHost
```

Set `NotificationDelivery:SenderName` to change the sender display name (default "EV Charging"); it applies to both providers.

`notification-mail` does not start while SES is selected. Remove the `NotificationDelivery:Provider`
secret to return to Mailpit. Automated tests always use Mailpit. Set
`Parameters:notification-worker-instances` to change the notification worker replica count.

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

### Provider contract tests

The provider suite starts only the provider AppHost and checks its documented
HTTP API using isolated storage. It requires a running Docker Linux engine.

```sh
dotnet test tests/EvCharging.ProviderContracts.Tests
```

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

### Search index worker tests

Requires Docker for the Elasticsearch and PostgreSQL integration tests.

```sh
dotnet test tests/EvCharging.SearchIndexWorker.Tests
```

### Search API tests

Requires Docker for the secured Elasticsearch integration tests, which run the
real read-only role provisioning.

```sh
dotnet test tests/EvCharging.SearchApi.Tests
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
