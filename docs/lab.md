# EV Charging System Design Lab

The lab implements the [architecture](architecture.md) with synthetic fixtures,
bounded load, and controlled faults. This reference maps architectural roles to
selected technologies and defines implementation constraints, execution profiles,
experiment settings, and verification.
[Domain invariants](domain.md) remain constant across profiles; configurable
reservation limits define each experiment's admission policy.
[Integration contracts](integrations.md) define simulator and real-provider behavior.
The [implementation plan](implementation-plan.md) tracks delivery and pending selections.

## 1. Technical setup

### 1.1 Technology selections

| Area | Selection |
|---|---|
| Backend | .NET 10, C# 14, ASP.NET Core 10 Minimal APIs, OpenAPI, Problem Details |
| Orchestration | Aspire 13.6 C# AppHost, local processes and Docker Linux containers |
| Frontend | React 19, TypeScript, Vite, React Router, Mantine, TanStack Query, Axios |
| API gateway | YARP |
| Identity | Keycloak; gateway-owned OIDC Authorization Code with PKCE, using ASP.NET Core OpenID Connect and cookie authentication |
| Live delivery | SignalR .NET/JavaScript clients; StackExchange.Redis backplane on Valkey |
| Relational access / pooling | PostgreSQL 18; EF Core 10/Npgsql provider and long-lived Npgsql data sources; managed PgBouncer in Azure |
| Auditing | EF Core save interception; PostgreSQL JSONB; [audit design](architecture.md#41-audit-history) |
| Application outcomes | FluentResults typed results |
| Managed database exercise | Azure Database for PostgreSQL Flexible Server; readable replica and provider-managed HA |
| Documents | MongoDB with the official C# driver |
| Cache / live backplane | Valkey with StackExchange.Redis |
| Search | Elasticsearch with the official Elastic .NET client |
| Messaging | Wolverine RabbitMQ transport and EF Core/PostgreSQL durability |
| Observation stream | Confluent.Kafka client; Kafka in KRaft mode |
| Object storage / CDN | RustFS locally, Cloudflare R2 hosted; AWSSDK.S3 / Cloudflare CDN |
| Notification delivery | [Local Mailpit adapter and external-provider options](#18-notification-adapters) |
| File processing | SkiaSharp images, PDFsharp summaries, .NET ZIP/JSON diagnostics |
| HTTP resilience | Microsoft.Extensions.Http.Resilience and Polly |
| Default diagnostics | OpenTelemetry ASP.NET Core, HTTP, and runtime instrumentation with OTLP export to Aspire Dashboard |
| Optional observability profile | OpenTelemetry Collector, Prometheus, Loki, Tempo, Grafana |
| Frontend error tracking | Hosted Sentry service, React SDK, and ASP.NET Core SDK tunnel; [configuration and constraints](#19-frontend-diagnostics) |

### 1.2 Application wiring

The platform and provider simulators run in independent Aspire AppHosts, each
with its own resource lifecycle and dashboard. The platform references provider
HTTP endpoints as parameterized external services. Simulator processes share
generic service defaults but do not reference platform business or messaging
assemblies. [Local startup](../README.md#local-development) configures the boundary.

Local host endpoints remain available for diagnostics. Service references define
application routing; they do not enforce network isolation.

Vite provides the local HTTP browser origin and development proxy to YARP for
API/authentication requests. Frontend files bypass the API gateway. Gateway and
API services default to two replicas each, balanced by Aspire's local proxies.
[Aspire networking](https://aspire.dev/fundamentals/networking-overview/)

Hosted execution uses static frontend hosting/CDN and managed TLS ingress/service
routing. The compute target remains unselected.

The baseline uses Aspire Dashboard diagnostics. The optional `observability`
profile sends OTLP through the Collector, which exports traces to Tempo, metrics
to Prometheus, and logs to Loki, with Grafana querying these backends. It also
forwards telemetry to Aspire Dashboard so resource diagnostics remain available.

HTTP failures use Problem Details without exception details. Request cancellation
is cooperative and does not establish whether a side effect occurred. Correlation
uses standard W3C trace context.

HTTP hosts expose `/health` and `/alive` in all environments; hosted ingress
controls public access. Instance diagnostics and OpenAPI remain development-only.
API endpoints are available through the gateway under `/api`.

### 1.3 PostgreSQL access and pooling

PostgreSQL range/exclusion constraints with `btree_gist` enforce booking conflicts.
Session claims use conditional updates/constraints; operation identity has a unique
constraint. These mechanisms enforce the [domain rules](domain.md#5-reservations).
[Range constraints](https://www.postgresql.org/docs/18/rangetypes.html#RANGETYPES-CONSTRAINT),
[btree_gist](https://www.postgresql.org/docs/18/btree-gist.html)

The local baseline uses one application database without a replica or server
pooler. Logical routes remain separate across environments:

| Connection | Local baseline | Azure database profile |
|---|---|---|
| `platform-write` | Application database | Primary through managed PgBouncer |
| `platform-read` | Same database with a read-only application role | Readable replica, with managed pooling when enabled |
| `platform-migration` | Direct connection with charging-schema owner credentials | Direct primary for charging migrations |

The application database uses `gateway_auth` for gateway persistence and `charging`
for shared business records, each with its own migration history and schema-owning
credentials. Gateway runtime access is limited to authentication records; platform
writer/reader roles access charging records. Provisioning owns database-wide
permissions and extensions. Schema migration jobs gate their consumers.
Host-specific Wolverine schemas are introduced with message durability.

Application roles enforce route permissions; read/write paths retain separate
Npgsql data sources and budgets. Authoritative reads use the write route; replica
queries tolerate asynchronous lag. Changing topology changes connection settings,
not application routing or consistency rules.

Managed PgBouncer uses transaction mode. Session state, advisory locks, temporary
objects, prepared statements, and persistence-library coordination require
compatibility checks; session-affine operations use a suitable direct connection.

Client pool limits multiply across processes and routes; server pools also depend
on pooler instances and database/user combinations. Gateway session persistence
has its own primary-route pool within the aggregate connection budget.

Azure exercises use managed PgBouncer endpoints on port 6432 and direct connections
on 5432, with provider authentication and TLS. Managed pooling and read replicas
require General Purpose or Memory Optimized compute. Replica lag/outage and
server-pool contention are verified in Azure; local runs verify route permissions
and client pooling. Npgsql multi-host routing is an optional direct-connection comparison.
[Npgsql lifecycle](https://www.npgsql.org/doc/basic-usage.html),
[Npgsql routing](https://www.npgsql.org/doc/failover-and-load-balancing.html),
[PgBouncer compatibility](https://www.pgbouncer.org/features.html),
[Azure PgBouncer](https://learn.microsoft.com/en-us/azure/postgresql/connectivity/concepts-pgbouncer),
[Azure read replicas](https://learn.microsoft.com/en-us/azure/postgresql/read-replica/concepts-read-replicas),
[Azure HA](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-high-availability)

#### Read consistency routing options

Current routing:

- Reservation [list](../src/EvCharging.Api/Features/Reservations/ListReservations.cs)
  and [detail](../src/EvCharging.Api/Features/Reservations/GetReservation.cs) use
  `PlatformDbContext` through `platform-write`, with untracked entity reads.
  Entire pages, including history, read primary.
- Catalog queries retain `PlatformReadDbContext`; no global override is applied.
- Both routes reach the same server locally; replica lag requires the Azure profile.

The following approaches are drafts, not configured routes.
[Architecture](architecture.md#5-read-and-write-paths) owns the consistency boundaries.

**Approach 1: fixed routing by use case**

One explicitly configured read context per query handler, with reservation
list/detail reads targeting the primary through reader credentials.

1. **Add a primary read context using the existing EF model.**

   In `EvCharging.Persistence`:

   ```csharp
   public sealed class PlatformPrimaryReadDbContext(
       DbContextOptions<PlatformPrimaryReadDbContext> options)
       : PlatformDbContext(options);
   ```

   The three contexts then have clear responsibilities:

   | Context | Destination | Permissions |
   |---|---|---|
   | `PlatformReadDbContext` | Ordinary read destination, potentially a replica | Reader |
   | `PlatformPrimaryReadDbContext` | Primary | Reader |
   | `PlatformDbContext` | Primary | Writer |

   This extra type makes ordinary constructor injection sufficient. It avoids
   a custom selector or middleware.

2. **Provide the primary reader connection through AppHost.**

   Reuse `AddConnection` and the existing reader credentials:

   ```csharp
   var primaryRead = AddConnection(
       "platform-primary-read",
       "platform_reader",
       readerPassword);
   ```

   Add it to `DatabaseConnectionResources`:

   ```csharp
   /// <summary>Read-only connection to the primary for current state.</summary>
   public required IResourceBuilder<ConnectionStringResource> PrimaryRead
   {
       get;
       init;
   }
   ```

   Reference it from the API alongside the existing connections:

   ```csharp
   .WithReference(databaseAccess.PrimaryRead)
   ```

   Today both reader connections reach the same PostgreSQL instance. When a
   replica is introduced, the ordinary read connection can move there while
   this connection remains on primary.

3. **Register it through the existing persistence extension.**

   Extend the existing data-source registration loop with:

   ```csharp
   public const string PrimaryReadRoute = "platform-primary-read";
   ```

   Then register the context:

   ```csharp
   builder.Services.AddDbContext<PlatformPrimaryReadDbContext>(
       (services, options) =>
           options.UseNpgsql(
               services.GetRequiredKeyedService<NpgsqlDataSource>(
                   PrimaryReadRoute)));

   builder.EnrichNpgsqlDbContext<PlatformPrimaryReadDbContext>(
       settings => settings.DisableRetry = true);
   ```

   It receives no audit interceptor. Database grants enforce reader permissions.
   Retry configuration remains subject to the separately recorded review.

4. **Change only the reservation query dependencies.**

   ```csharp
   public sealed class ListReservationsQueryHandler(
       PlatformPrimaryReadDbContext database)
   {
       // Existing query implementation.
   }

   public sealed class GetReservationQueryHandler(
       PlatformPrimaryReadDbContext database)
   {
       // Existing query implementation.
   }
   ```

   Their queries, ownership checks, pagination and response mappings stay
   together. The existing shared mapping accepts `PlatformDbContext`, so the
   derived read context already fits.

   Catalog queries continue using `PlatformReadDbContext`. Booking, cancellation
   and charging-start commands use the writer context for their transactions.

5. **Keep frontend behavior independent of database topology.**

   Existing endpoints and query keys remain unchanged. Successful commands
   update the TanStack cache with their returned state, then refresh the affected
   queries. Reservation refreshes and status polling consistently reach the
   primary through the backend configuration.

   This removes the need for page-level primary mode, separate consistency cache
   keys, or rules for switching back to a replica.

**Approach 2: temporary primary routing window**

Server-side primary affinity for a configurable period after a successful write,
following [Rails' automatic database switching](https://guides.rubyonrails.org/active_record_multiple_databases.html#activating-automatic-role-switching).

1. **The gateway records the window.**

   After a successful mutating browser API response—`POST`, `PUT`, `PATCH` or
   `DELETE`—a YARP response transform sets a short-lived, protected, `HttpOnly`
   cookie. Each subsequent successful write renews it. Failed validation does
   not start the window.

   The duration belongs to validated application configuration, for example
   `ReadRouting:PrimaryWindow`. A specific value should reflect the expected
   replica lag.

2. **The gateway selects the routing hint on subsequent requests.**

   A request transform validates the cookie. While it remains valid, the gateway
   adds an internal header indicating primary reads. It removes any
   browser-supplied version of that header before setting its own.

   The cookie naturally follows refreshes, navigation and other tabs in the same
   browser. Gateway replicas would share Data Protection keys. YARP supports
   both request and response transforms; ASP.NET Core provides protected payloads
   with expiration.

3. **The API selects the read connection once per request.**

   Keep `PlatformReadDbContext`. Its existing scoped registration chooses the
   data source using the routing hint:

   ```csharp
   builder.Services.AddDbContext<PlatformReadDbContext>(
       (services, options) =>
       {
           var routing = services.GetRequiredService<ReadRoutingContext>();

           var route = routing.UsePrimary
               ? PrimaryReadRoute
               : ReadRoute;

           options.UseNpgsql(
               services.GetRequiredKeyedService<NpgsqlDataSource>(route));
       });
   ```

   `ReadRoutingContext` is a small request-scoped decision established before
   Wolverine invokes the handler. Both data sources use **reader credentials**.
   Commands retain their writer context.

   Handlers receive one read context and contain no connection-selection branches.

4. **Expiry returns ordinary reads to the replica.**

   Reads do not renew the window. React keeps using the same endpoints and query
   keys, updating its cache from command responses as usual.

An illustrative five-second window behaves like this:

```text
12:00:00  Booking commits; gateway issues the routing cookie
12:00:01  Reservation list reads primary
12:00:03  Page refresh reads primary
12:00:05  Window expires; ordinary reads use replica
```

**Trade-offs**

| Concern | Fixed routing | Temporary window |
|---|---|---|
| Primary load | All reservation list/detail traffic, including history | Browser reads during the window, plus explicitly current-status queries |
| Replica offloading | Other queries that tolerate lag | Ordinary reads outside the window |
| Coordination | Context registration and connection wiring | Gateway cookie/transforms and API request-scoped selection |
| Read-your-write | Reads after commit reach primary | Assumes replica catches up before the window expires |
| Worker updates | Subsequent primary reads see committed updates | Do not renew the browser window; current-status checks need independent primary routing |
| Lost write response | Subsequent reads still use primary | May prevent the cookie reaching the browser |
| In-flight older reads | Can still overwrite frontend state | Can still overwrite frontend state |

Both approaches retain reader permissions for queries and primary-side checks
inside command transactions. Neither makes multiple response queries one atomic
snapshot. Native TanStack cache updates and cancellation of superseded reads
preserve immediate command results. Npgsql server-role routing does not track
recent writes or prove replica freshness.

Pending selection: routing approach, window duration, and designation of
current-status queries. The Azure profile can compare primary load, lag beyond
the window, and user-visible state regression.

Sources:

- [YARP transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/extensibility-transforms)
- [Time-limited Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads)
- [Npgsql routing](https://www.npgsql.org/doc/failover-and-load-balancing.html)
- [TanStack mutation responses](https://tanstack.com/query/latest/docs/framework/react/guides/updates-from-mutation-responses)

### 1.4 Message persistence and envelopes

Each message-processing API/worker host family uses a distinct Wolverine storage
schema in the application database. Replicas share that schema and durability
coordination. SQL mutations and outgoing envelopes commit together;
non-SQL store updates use idempotent writes
before acknowledgement. The hosted durability runtime recovers stranded inbox/outbox
work.

Message envelopes include event ID, schema version, entity ID/version, occurrence
and receipt times, correlation/causation IDs, and trace context. They exclude secrets
and private file URLs. Transport deduplication uses message identity; conditional
business transitions also protect against independently submitted duplicate work.
[Wolverine durability](https://wolverinefx.net/guide/durability/),
[PostgreSQL storage](https://wolverinefx.net/guide/durability/postgresql),
[EF Core integration](https://wolverinefx.net/guide/durability/efcore/),
[RabbitMQ reliability](https://www.rabbitmq.com/docs/reliability)

#### Observation stream

| Area | Choice |
|---|---|
| Topic | One observations topic with six partitions and seven-day retention, defined in `infra/kafka/topics.txt` and created by the one-shot `kafka-provisioning` resource before the API and observation consumers start; existing topics keep their partition count |
| Partition key | Integration key and provider station, charger and connector address |
| Producer | Idempotent, `acks=all`, bounded delivery timeout, configured through the Aspire Confluent producer settings; its health check is disabled so a stream outage fails callbacks without removing the API from gateway rotation |
| Batch limit | A callback carries at most `ObservationIngestion:MaximumBatchSize` observations (default 100); the request size limit follows from it |
| Callback credential | One API key per provider integration in the `ObservationSources` settings, supplied by a persistent AppHost secret; the key identifies the integration and several keys may be active for rotation. ASP.NET Core provides no API-key scheme, so a small authentication handler validates the key |

#### Observation views

| Area | Choice |
|---|---|
| Consumer | The observation worker reads the topic as its own consumer group with the Confluent client, in batches (`ObservationProcessing:BatchSize`, `BatchWait`). Offsets are stored only after the batch is written |
| Documents | Two MongoDB collections in the `observations` database: history, one document per source event, and latest, one document per provider session. The API reads the latest view for the progress query. The indexes are created by the one-shot `document-store-migrations` resource before the worker starts |
| Writes | Idempotent bulk writes with majority, journaled acknowledgement. The latest view is replaced only by a strictly newer occurrence time |
| Clock skew | One setting, `Charging:ClockSkew`, owned by the AppHost and passed to the worker and the API. A reading dated more than the skew ahead of its receipt stays history, and the API leaves readings dated more than the skew ahead of its clock out of the power history |
| Power history | The API reads history in one-minute buckets of source time with a MongoDB aggregation on the by-session index, so a request from the last bucket held returns one bucket |
| Unprocessable messages | An invalid or unsupported-version envelope is logged and skipped. Stream and document-store failures are retried with exponential backoff and no attempt limit using a `Microsoft.Extensions.Resilience` pipeline |

### 1.5 Storage and delivery

The local storage service runs as a single-node container.
[RustFS](https://github.com/rustfs/rustfs) uses the Apache-2.0 license.
Storage compatibility checks cover upload/download, HEAD
verification, publication copy, deletion, and presigned transfers. Contract tests
cover both the selected local service and R2. Browser transfers require storage
CORS rules for the application origin.
[RustFS S3 compatibility](https://docs.rustfs.com/en/reference/s3-compatibility)

Public assets use an R2 custom domain in the same Cloudflare account/zone as the
bucket, on a dedicated subdomain of the lab-owned domain. Exact hostnames are
environment configuration. `r2.dev` does not provide
CDN caching. Private buckets have no public custom-domain access; presigned URLs
use the R2 S3 API domain rather than the public asset domain.
[R2 .NET client](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/),
[Public buckets](https://developers.cloudflare.com/r2/buckets/public-buckets/),
[Presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/)

### 1.6 Frontend and live clients

Frontend behavior follows the [live-delivery design](architecture.md#11-frontend-and-live-delivery).

Valkey cache and SignalR backplane traffic use separate key/channel prefixes,
connections, and budgets. Live-route cookie affinity
keeps negotiation and transport on the same API. Authentication-session and
live-affinity cookies serve separate purposes. Client/server contract checks cover
fan-out, disconnect, and reconnection.
[SignalR scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0),
[Valkey compatibility](https://valkey.io/topics/migration/),
[YARP affinity](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/session-affinity?view=aspnetcore-10.0)

### 1.7 Gateway authentication conventions

Browser routes follow the [gateway authentication design](architecture.md#131-gateway-authentication).

Gateway session persistence uses PostgreSQL; ticket protection uses ASP.NET Core
Data Protection with EF Core key storage. The free Apache-2.0 Duende
AccessTokenManagement OpenIdConnect package provides token refresh and revocation
with process-local coordination.
The local key ring is unencrypted at
rest. Hosted key protection and retention remain deployment dependencies.
[Duende AccessTokenManagement](https://docs.duendesoftware.com/accesstokenmanagement/web-apps/),
[Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)

| Browser route | Responsibility |
|---|---|
| `/auth/login` | Start login with a validated local return URL |
| `/auth/callback` | Complete OIDC and establish the browser session |
| `/auth/session` | Sign-in state, display claims, and antiforgery token; no OIDC tokens |
| `/auth/logout` | Antiforgery-protected logout of the application and provider session |
| `/auth/logout-callback` | Complete the provider's post-logout redirect |

Hosted public origins require HTTPS; discovery transport can use internal HTTP
independently of public HTTPS. Browser session cookies use
HttpOnly and SameSite=Lax; hosted execution requires Secure. Explicitly enabled
local HTTP uses loopback browser origins without TLS provisioning.
Keycloak administration is directly accessible through its local loopback endpoint.
Local registration uses Keycloak-hosted forms and email verification. Mailpit
captures verification emails for inspection and link activation; no real email is
delivered. Its registration mailbox is independent of reservation-notification
delivery adapters.
Hosted ingress must restrict administration and management endpoints.
Keycloak uses a dedicated PostgreSQL database. Its pool starts with the configured
number of connections and remains within configured limits; identity connections
count toward database connection budgets.

Identity provisioning supports local and hosted execution. Public origins remain
stable across restarts so registered redirects and the issuer stay valid.
Provisioning preserves existing realm state. Changes to realm configuration,
credentials, or the public origin require an explicit administrative update;
restart alone does not apply them.

Before attaching an access token upstream, the gateway strips browser-supplied
authorization/identity headers and its session cookie. API token validation checks
signature, issuer, audience, and expiry. API/live authentication failures return
unauthorized responses rather than HTML login redirects.
Worker and provider callback routes validate their service/source credentials and
forward the authenticated machine identity. Browser token substitution and cookie
handling apply only to browser-session routes; machine callers do not enter the
interactive OIDC flow.
[Keycloak hostname and backchannel configuration](https://www.keycloak.org/server/hostname),
[Antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0),
[YARP authentication](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0)

### 1.8 Notification adapters

Reservation emails use Handlebars.Net for embedded plain-text and HTML
[templates](../src/EvCharging.NotificationWorker/Templates/). Email styling follows
the browser application's
[theme](../src/EvCharging.Web/src/app/bootstrap.tsx) and
[semantic colours](../src/EvCharging.Web/src/app/global.css), using inline styles
for email-client compatibility.
Email timestamps use explicit UTC labels.

Local email uses Mailpit's HTTP API and a dedicated `notification-mail` mailbox,
independent of registration email. Mailpit provides content inspection; persisted
attempts provide failure evidence. Configuration belongs to
[worker options](../src/EvCharging.NotificationWorker/Configuration/NotificationDeliveryOptions.cs);
delivery semantics belong to [integrations](integrations.md#4-notification-providers).

External-provider selection remains unresolved among Cloudflare Email Service,
Azure Communication Services Email, and Amazon SES. Hosted setup depends on that selection.

### 1.9 Frontend diagnostics

Frontend diagnostics cover error capture, structured logs, breadcrumbs, release
context, and browser tracing. Captured failures include
JavaScript exceptions, unhandled rejections, and React error-boundary reports.
Reports include the route, browser, and recent actions/request outcomes.
Tokens, form values, and signed URLs are excluded.

| Configuration | Behavior |
|---|---|
| Enablement | Disabled by default; independent of the `observability` profile. Disabled mode sends no Sentry telemetry |
| Public settings | DSN, environment, and build/release identifier supplied through frontend startup configuration |
| Collection controls | Configurable error/trace sampling and log levels; session replay has a separate opt-in setting, disabled by default |
| Build credentials | Source-map upload token, organization, and project configured in the build environment; the secret token is never exposed to the browser |
| Source-map access | Source maps remain private and match the deployed release; excluded from public CDN assets |

Local builds with Sentry disabled require no Sentry account or upload credentials.
Runtime settings are defined by [gateway diagnostics options](../src/EvCharging.Gateway/Diagnostics/FrontendDiagnosticsOptions.cs);
[frontend initialization](../src/EvCharging.Web/src/lib/diagnostics/sentry.ts) applies collection controls and
[Vite build configuration](../src/EvCharging.Web/vite.config.ts) controls private source-map upload.
The build and startup release identifiers must refer to the same deployed build.

### 1.10 WattHarbor charging provider

WattHarbor implements the [provider contract](integrations.md#1-provider-contracts)
with these choices:

| Area | Choice |
|---|---|
| Persistence | Provider-owned PostgreSQL server, database and credentials; EF model and migrations independent of platform persistence |
| Authentication | Provider-owned Keycloak realm and storage; client-credentials accounts for the charging worker and simulation controls, with a separate provider audience and control permissions |
| Transport | HTTP/JSON start and command/session lookup endpoints; typed `HttpClient` through `IHttpClientFactory`, with bounded timeouts |
| Idempotency | Platform operation public ID as the stable provider key across attempts; retain and compare the original effect payload |
| Atomicity | Command result, simulated session and connector claim commit in one provider transaction |
| Fault controls | Authenticated, deterministic faults scoped to a command or a session's telemetry; apply-then-lose-response faults interrupt the response after commit. Faults are temporary and not persisted |
| Observations | Recorded in the provider database at the [normal simulator cadence](#22-default-timings), so sessions and their readings survive restart. The first and final readings accompany start and stop, and no reading follows the final one. Energy is in Wh on the wire |
| Callback delivery | Recorded observations are delivered in batches to a target set through simulation control and kept across restart. Failed batches are retried with the same event identities under bounded settings, and stale observations are not delivered. Simulators exclude Messaging, so this is a small provider-side loop rather than an outbox |

Endpoint tables separate the [public provider API](integrations.md#public-provider-api)
from the [internal simulation API](integrations.md#internal-simulation-api).

Testing treats the provider as a black box:

- Assert only the documented command/query contract consumed by the platform.
  Simulator controls arrange equipment and faults; their implementation,
  validation, permissions and persistence are outside the contract-test scope.
  No direct database access, internal handler calls or storage assertions.
- Verify deduplication, payload conflicts, competing starts, rejection, lost-response
  resolution and outcome lookups through that public contract.
- Client checks use the same contract; a small set of platform E2E journeys
  verifies the complete boundary without repeating every provider scenario.

## 2. Experiments and verification

### 2.1 Execution profiles

| Profile | Resources and purpose |
|---|---|
| Local baseline | Aspire processes/containers: multiple gateway/API instances, independent workers, one application PostgreSQL server, a separate identity database server, MongoDB, Valkey, Elasticsearch, one RabbitMQ broker, Kafka, an S3-compatible storage container, Keycloak, simulators, and Aspire Dashboard diagnostics |
| Optional `observability` | Adds the [selected observability backends](#11-technology-selections) for telemetry routing, backend queries, custom dashboards, and export/backend failure exercises; startup/health gates apply only when enabled |
| Optional Sentry | Enables [frontend diagnostics](#19-frontend-diagnostics); requires a hosted project and build-upload credentials |
| Broker leader loss | Three RabbitMQ nodes with quorum queues; leader-loss and recovery behavior |
| Cloudflare files/CDN | R2 and Cloudflare CDN deliver versioned frontend assets and published media through a custom domain; real presigned transfers, warm/cold cache behavior, and purge |
| External notification delivery | Provider selection unresolved; test recipients and recorded provider/delivery outcomes |
| Azure database failover | Managed PostgreSQL HA plus readable replica/pooler routes; provider-supported failover exposes application interruption and recovery |

The local baseline covers redelivery, outbox recovery, projection replay, cache
loss, and dependency interruptions. Clustered profiles cover quorum/leader behavior;
the Azure database profile covers replication and managed pooling. Local application
replicas share a host failure boundary and do not demonstrate multi-zone availability.

MongoDB, Valkey, Elasticsearch, RabbitMQ, and Kafka need no cloud subscriptions.
WattHarbor and AmpTrail are controlled lab providers.
Notification email is captured locally in Mailpit without real delivery.
Hosted notification delivery depends on [provider selection](#18-notification-adapters).
Local S3 storage does not demonstrate hosted CDN or R2 signing behavior;
the files/CDN profile uses the [R2 delivery constraints](#15-storage-and-delivery).

The local profile requires no paid product subscriptions. Cloudflare CDN uses the
Free plan; R2 Standard storage has monthly free allowances with billed overages.
Managed Azure PostgreSQL, its HA standby, and its readable replica incur cloud
resource charges. Cloudflare and Azure profiles are active only for their exercises.
The local notification sink requires no cloud subscription. Hosted email costs and
recipient restrictions depend on the provider selected for the external exercise.
[R2 pricing](https://developers.cloudflare.com/r2/pricing/),
[Cloudflare Free plan](https://www.cloudflare.com/plans/free/),
[Azure PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)

### 2.1.1 Synthetic fixtures

| Fixture | Exercise |
|---|---|
| Assigned and unassigned stations | Operator access and authorization boundaries |
| Fictional catalogue with varied equipment counts | Public browsing and concurrent sessions without sharing one charging outlet |
| Synthetic driver and operator identities | Identity-linked ownership and station assignments |

Seeding creates the synthetic catalogue and identity-linked fixtures. Reset restores
charging fixtures and removes charging workflow/file metadata while preserving browser sessions,
encryption keys, and identity-provider data. Reset is limited to relational
charging data; other stores are unaffected. Entity identifiers change when reset
recreates records. The reset operation is excluded from deployment.
[Reset procedure](../README.md#charging-data-reset).

### 2.2 Default timings

Short intervals make failure and recovery visible during a small demonstration.
These are overridable experiment settings, not production availability targets.

| Setting | Default | Meaning |
|---|---|---|
| Telemetry interval | 2 seconds | Normal simulator cadence |
| Freshness limit | 10 seconds | Greater source-observation age is stale. A started session with no observation is stale once it is older than this |
| Driver progress polling | 5 seconds; power history 30 seconds | The charging page stops polling a session that sent its final reading, or that the API reports silent for longer than the silence limit (`Charging:SilenceLimit`), and offers a manual refresh |
| Silence limit | 10 minutes | A running session whose provider has been quiet longer than this is no longer polled; it must exceed the freshness limit. The API also reports the session's elapsed time, so the page does not depend on the device clock |
| Replica lag tolerance | 2 seconds | Greater lag triggers bounded fallback or a degraded view |
| Provider HTTP attempt timeout | 3 seconds | Maximum wait for one effect or outcome-query response |
| Command request limit | 3 total requests per start or stop | Initial request plus same-key resends made by automatic reconciliation checks that find no provider record; subject to provider safety and command eligibility |
| Automatic reconciliation | Every 5 seconds, 6 scheduled checks per run | Captured checking window closes one interval after the final scheduled check; manual or operator checks remain available |

Wolverine scheduled work and retry policies bound automatic reconciliation under
the [resilience boundaries](architecture.md#12-resilience-boundaries).
Operator reconciliation requests are audited. Scenario records define request
retry and admission limits.

Pool limits, worker concurrency, instance counts, stream/document/file retention,
and load are set per scenario. Deliberately small budgets expose saturation without a large
user population. Overrides do not weaken eligibility, idempotency, or evidence rules.

### 2.2.1 Reservation policy experiments

The initial booking-limit experiment configures admission limits as follows.
[Reservation rules](domain.md#5-reservations) define their semantics.

| Setting | Initial experiment value |
|---|---|
| Maximum outstanding reservations per driver | 1 |
| Maximum booking duration | 4 hours |
| Maximum advance-booking horizon | 24 hours |
| Arrival allowance | 15 minutes |

Quota experiments also use a limit greater than one to verify configurable
admission under concurrent API instances. Policy-change experiments verify that
accepted deadlines remain stable and a lower quota prevents further bookings
without cancelling accepted allocations.

### 2.3 Verification tools and signals

| Area | Tool |
|---|---|
| Domain and backend tests | xUnit |
| Real dependency integration | Testcontainers for .NET |
| Topology tests | Aspire.Hosting.Testing |
| Frontend behavior | Vitest and React Testing Library |
| Browser workflows | Playwright |
| Load and saturation | k6 |
| Network latency/isolation | Toxiproxy |
| External faults | Charging-provider simulator APIs; local notification adapter and mail-sink interruption scenarios |

Verification uses correlated telemetry from the
[configured diagnostics path](#12-application-wiring).

Charging-provider scenarios use operation/session identifiers, execution records,
and correlated logs and traces for troubleshooting. A history of every provider
request and response is outside scope. Provider behavior is verified through its
public API and the resulting platform state.

| Boundary | Evidence |
|---|---|
| Requests | Latency, errors, admission rejection, active requests, selected replicas |
| SQL and pooling | Client counts by route/process, acquisition waits, transaction/query duration, locks; server-pool counts and replay lag in Azure |
| Jobs | Queue depth/age, oldest outbox age, attempts, dead letters, uncertain operations |
| Streams/documents | Consumer lag, processing errors, retained history, latest-view age |
| Providers | Physical state, command/correlation records, latency, rate limiting, circuit state, reconciliation results |
| Search/cache | Index freshness/rebuild status, hit rate, fallback load |
| Files/CDN | Pending uploads, processing attempts, object versions, origin errors, edge hits/misses |
| Frontend | Reconnects, operation identity/results, unavailable dependencies, stale progress |
| Frontend diagnostics (Sentry enabled) | Readable JavaScript/component stacks, release, breadcrumbs, grouped issues, alert delivery, and correlated request traces |

### 2.4 Fault scenarios

| Scenario | Expected behavior and acceptance evidence |
|---|---|
| Concurrent overlapping reservations | One conflicting booking succeeds; primary constraints remain effective across API instances |
| Concurrent bookings by one driver across connectors | Configured quota and driver overlap protection hold across API instances |
| Expiry worker delayed or restarted | Admission preserves capacity under the [reservation quota rules](domain.md#5-reservations); resumed expiry processing records one transition, audit, and event despite duplicate checks |
| Reservation policy changed | New admission uses the updated limits; accepted slots and deadlines remain unchanged |
| Cancellation races start | One eligible transition wins; cancelled bookings cannot dispatch a start |
| Arrival deadline passes with start queued or uncertain | Unattempted work fails safely; a possible external effect retains its claim and is reconciled |
| Start applies but response is lost | One logical operation; visible uncertainty resolves against provider evidence without blind resubmission |
| Worker dies after effect before acknowledgement | Redelivery retains operation/attempt identity; SQL mutations deduplicate and provider ambiguity follows its contract |
| Duplicate/late observations | Historical evidence retained; current state follows source-time ordering |
| Replay into a replacement view | Validated rebuilt projection; no charging commands or notifications |
| Telemetry stops | Timestamped state becomes stale; silence does not imply stopped or offline |
| Stop confirmed before final usage | Session waits for final evidence; completion and summary use matching frozen facts |
| Replica lag or replica lost (Azure database profile) | Lag visible; bounded fallback protects primary; immediate write confirmation remains correct |
| Read saturation / API-worker scaling | Bounded acquisition/rejection; separate route budgets and aggregate client/server counts remain observable |
| Gateway/API process lost | Routing reaches healthy instances; browser session remains usable across gateways; in-flight operations recover by identity |
| Gateway unavailable during observation submission | Worker/provider submissions retain event identity and follow bounded retry/recovery; acknowledgement still requires durable ingestion, and no caller bypasses the gateway |
| Primary lost; managed pooler lost in Azure | Interrupted work is visible; reconnect is bounded and uncertain commits are checked |
| Managed database failover (Azure database profile) | Endpoint/pool recovery, transaction outcomes, recovery time, and any lost commits recorded |
| Broker outage and recovery | Outbox retains pending work, exposes age, and drains without duplicate mutations |
| Clustered broker leader lost | Quorum queue behavior and publisher/consumer recovery observed |
| Stream unavailable | No false durable acknowledgement; bounded callback retry/polling and visible observation uncertainty |
| Observation consumer stalled / MongoDB unavailable | Observation-view lag increases; catch-up succeeds within retention or its recovery limit is exposed |
| Search index worker instances lost and restored | Index updates pause and catch up from retained inputs; cache maintenance and observation/session-evidence processing continue |
| Cache worker instances lost and restored | Cached query views expire or become visibly stale; API fallback remains bounded, warming resumes, and search/observation processing continue |
| Cache flushed/unavailable | Bounded fallback and warm-up; no loss of authoritative records |
| Valkey backplane unavailable and restored | Cross-instance live fan-out interruption is visible; bounded polling/reconnect obtains current snapshots, then fan-out resumes |
| Search unavailable/rebuilt | Limited lookup or visible unavailability; replacement index validated before switchover |
| Provider rate-limited/down | Its work isolated; retries bounded and durable intent retained |
| Upload succeeds but completion notice is lost | Sweep verifies object and safely resumes processing |
| Media worker fails after writing variants | Retry converges, old publication remains usable, orphan cleanup is visible |
| Media worker instances lost and restored | Media/document queues pause and resume from durable state; notification delivery and charging execution remain available |
| Notification worker instances lost and restored | Notification delivery pauses and resumes from durable state; media/document processing and charging execution remain available |
| Signed upload URL reused | Immutable published bytes cannot be overwritten |
| Object storage lost | Files can fail while charging continues; processing retries retain references |
| Warm and uncached synthetic objects deleted at the R2 origin | Warm edge hits can serve until expiry/eviction; cache misses fail and unavailable media shows placeholders; purge removes stale cached publication |
| Summary/notification failure | Session stays completed; independent retry/exhaustion is inspectable |
| Notification provider rejects, delays, or loses a response | Delivery attempts retain identity and provider evidence; acceptance differs from recipient delivery, and ambiguous sends are not blindly repeated |
| Live disconnect | Reconnect obtains a current snapshot before incremental updates |
| Cross-driver/station access | API and private-file authorization reject unauthorized access |
| Identity provider unavailable | Login/refresh failures visible; established sessions can use unexpired cached-signing-key-validated tokens while the session store is reachable |
| Authentication session store unavailable | Protected routes fail explicitly; no anonymous downgrade or use of disposable cache as session authority |
| Concurrent token refresh / logout | Concurrent requests follow the identity provider's supported refresh policy; logout revokes refresh tokens and removes the shared ticket |
| Browser authentication boundaries | No OIDC tokens exposed to JavaScript; login uses gateway routes, state-changing requests require antiforgery, and API access still enforces ownership |
| Dashboard export unavailable | Bounded buffering/drop; core processing continues and export loss is observable through local diagnostics |
| Collector or observability backend lost (optional profile) | Bounded buffering/drop and inspectable pipeline loss; core processing continues and telemetry delivery resumes after recovery |
| Frontend render/async failure (Sentry enabled) | Error report identifies the deployed source location and preceding activity; repeated failures group into an issue and trigger the configured alert |
| Sentry disabled or unreachable | Disabled mode sends no Sentry telemetry; failed export is bounded and does not prevent navigation or platform requests |

Each scenario records topology/version, settings, workload, initial state, fault
injection point/duration, expected behavior, correlated observations, final state,
and cleanup. Recovery is judged by business outcomes and durable evidence, not
merely a restarted process. Tests remain separated into domain, real-dependency,
contract, UI, resilience, and load concerns.
