# Development

Commands for working on the lab beyond starting it. The
[README](../README.md#run-locally) covers requirements and the combined launcher.

## Development endpoints

- Vite serves the UI and proxies `/api`, `/auth`, and `/diagnostics` requests to
  the gateway. These browser requests use the frontend's scheme, hostname, and
  port; the gateway and API run at separate service endpoints.
- `/api/diagnostics/instance` identifies the API instance; the
  `X-Gateway-Instance` response header identifies the gateway instance.
- HTTP backend hosts expose `/health` and `/alive` at their own service endpoints.
- In Development, each HTTP host exposes Scalar at `/scalar` and its OpenAPI
  document at `/openapi/v1.json`. API links in Aspire open Scalar directly.
- [Routing and execution profiles](lab.md#12-application-wiring).

## AppHosts without the launcher

Each AppHost manages its own resources and dashboard. Platform startup does not
start the lab providers. The provider AppHost owns the WattHarbor lab provider's
PostgreSQL database and Keycloak instance. Its client credentials are generated
into that AppHost's user secrets. The imported
[provider realm](../infra/keycloak/providers/providers-realm.json) separates
charging access from simulation controls. Realm imports apply on first creation;
editing the import file does not update an existing realm.

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

## Identity realm changes

An existing Keycloak realm is retained across restarts, and restart does not
reimport it. Apply registration and SMTP changes through its Admin Console; the
source configuration is [the realm definition](../infra/keycloak/platform/evcharging-realm.json),
and SMTP connection values come from the Keycloak resource environment.

## Replica overrides

```sh
dotnet user-secrets set "Parameters:gateway-instances" "3" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:api-instances" "3" --project src/EvCharging.AppHost
dotnet user-secrets set "Parameters:notification-worker-instances" "2" --project src/EvCharging.AppHost
```

- Store secrets in user-secrets or environment configuration, never tracked settings.
- Aspire persists generated dependency credentials in AppHost user-secrets.

## Charging-data reset

1. Stop API replicas and workers in Aspire Dashboard before resetting data.
2. Start the local reset resource:

   ```sh
   aspire resource lab-data-reset start --apphost src/EvCharging.AppHost/EvCharging.AppHost.csproj --non-interactive
   ```

3. Wait for `lab-data-reset` to finish, then restart the stopped resources.

- [Reset scope and synthetic fixtures](lab.md#211-synthetic-fixtures).

## Optional hosted profiles

### Frontend diagnostics

[Frontend diagnostics](lab.md#19-frontend-diagnostics) are disabled by default and
need no Sentry account. To report from a local run, create a Sentry React project and
supply its DSN (`https://<key>@<host>.sentry.io/<project id>`), an environment, and a release:

```sh
dotnet user-secrets set "FrontendDiagnostics:Enabled" "true" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Dsn" "<DSN>" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Environment" "local" --project src/EvCharging.AppHost
dotnet user-secrets set "FrontendDiagnostics:Release" "local" --project src/EvCharging.AppHost
```

Restart the gateway after changing a setting. Enabled settings without a hosted DSN,
environment, and release fail gateway startup. `ErrorSampleRate`, `TraceSampleRate`,
`LogLevels`, and `ReplayEnabled` under `FrontendDiagnostics:` adjust collection.
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

### Amazon SES notification delivery

Notification email is captured in Mailpit by default. To send through
[Amazon SES](lab.md#18-notification-adapters) instead, supply the sender, region and access key.
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
secret to return to Mailpit. Automated tests always use Mailpit.

## Backend checks

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

## Frontend checks

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

Run each project separately with `dotnet test tests/<Project>`. Integration suites
need a running Docker Linux engine; Testcontainers starts and removes isolated
containers.

| Project | Real dependencies |
|---|---|
| `EvCharging.Domain.Tests` | None |
| `EvCharging.Api.Tests` | PostgreSQL, object storage |
| `EvCharging.ChargingWorker.Tests` | PostgreSQL |
| `EvCharging.ObservationWorker.Tests` | MongoDB |
| `EvCharging.Persistence.Tests` | PostgreSQL; schema isolation and runtime-role permissions |
| `EvCharging.Gateway.Tests` | PostgreSQL |
| `EvCharging.NotificationWorker.Tests` | PostgreSQL |
| `EvCharging.MediaWorker.Tests` | PostgreSQL and object storage, with real storage provisioning |
| `EvCharging.S3ObjectStore.Tests` | Object storage, with real storage provisioning |
| `EvCharging.SearchIndexWorker.Tests` | Elasticsearch and PostgreSQL |
| `EvCharging.SearchApi.Tests` | Secured Elasticsearch, with real read-only role provisioning |
| `EvCharging.ProviderContracts.Tests` | Provider AppHost only, with isolated storage |
| `EvCharging.E2E.Tests` | Full platform and provider AppHosts; see below |

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
- One test: `--filter "FullyQualifiedName~<TestName>"`.
