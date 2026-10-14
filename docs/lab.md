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
| Notification delivery | [Simulator and selected real-provider adapter](#18-notification-adapters) |
| File processing | SkiaSharp images, PDFsharp summaries, .NET ZIP/JSON diagnostics |
| HTTP resilience | Microsoft.Extensions.Http.Resilience and Polly |
| Default diagnostics | OpenTelemetry ASP.NET Core, HTTP, and runtime instrumentation with OTLP export to Aspire Dashboard |
| Optional observability profile | OpenTelemetry Collector, Prometheus, Loki, Tempo, Grafana |
| Frontend error tracking | Hosted Sentry service, React SDK, and ASP.NET Core SDK tunnel; [configuration and constraints](#19-frontend-diagnostics) |

### 1.2 Application wiring

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

NotificationWorker selects `Simulator` (local default) or `Cloudflare` through
configuration. Missing provider configuration fails explicitly without switching
providers. Delivery semantics are defined in
[integrations](integrations.md#4-notification-providers).

| Provider | Adapter and setup |
|---|---|
| Cloudflare Email Service — selected | Discovery-independent HTTPS REST client; account ID, secret API token with Email Sending permission, and onboarded sender domain using Cloudflare DNS; email sending is in beta |
| Azure Communication Services Email — extension option | .NET email SDK; Communication Services/Email resources, credentials, and verified sender |
| Amazon SES — extension option | AWS SDK; region, credentials, and verified sender; sandbox recipients must also be verified |

Cloudflare DNS is already in use for the lab domain. Email Sending onboarding,
sender/verified-recipient configuration, and credentials are hosted-exercise
dependencies. Provider SDKs and wire formats remain inside their adapters.
[Cloudflare REST API](https://developers.cloudflare.com/email-service/api/send-emails/rest-api/),
[Azure email SDK](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/send-email),
[SES SDK](https://docs.aws.amazon.com/ses/latest/dg/send-an-email-using-sdk-programmatically.html)

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

## 2. Experiments and verification

### 2.1 Execution profiles

| Profile | Resources and purpose |
|---|---|
| Local baseline | Aspire processes/containers: multiple gateway/API instances, independent workers, one application PostgreSQL server, a separate identity database server, MongoDB, Valkey, Elasticsearch, one RabbitMQ broker, Kafka, an S3-compatible storage container, Keycloak, simulators, and Aspire Dashboard diagnostics |
| Optional `observability` | Adds the [selected observability backends](#11-technology-selections) for telemetry routing, backend queries, custom dashboards, and export/backend failure exercises; startup/health gates apply only when enabled |
| Optional Sentry | Enables [frontend diagnostics](#19-frontend-diagnostics); requires a hosted project and build-upload credentials |
| Broker leader loss | Three RabbitMQ nodes with quorum queues; leader-loss and recovery behavior |
| Cloudflare files/CDN | R2 and Cloudflare CDN deliver versioned frontend assets and published media through a custom domain; real presigned transfers, warm/cold cache behavior, and purge |
| Cloudflare notification delivery | The real email adapter replaces the notification service simulator; verified test recipients and recorded provider/delivery outcomes |
| Azure database failover | Managed PostgreSQL HA plus readable replica/pooler routes; provider-supported failover exposes application interruption and recovery |

The local baseline covers redelivery, outbox recovery, projection replay, cache
loss, and dependency interruptions. Clustered profiles cover quorum/leader behavior;
the Azure database profile covers replication and managed pooling. Local application
replicas share a host failure boundary and do not demonstrate multi-zone availability.

MongoDB, Valkey, Elasticsearch, RabbitMQ, and Kafka need no cloud subscriptions.
The synchronous and queued charging providers are controlled simulators.
`NotificationProviderApi` simulates a third-party delivery service without sending
real messages. Hosted notification delivery uses the [selected adapter](#18-notification-adapters).
Local S3 storage does not demonstrate hosted CDN or R2 signing behavior;
the files/CDN profile uses the [R2 delivery constraints](#15-storage-and-delivery).

The local profile requires no paid product subscriptions. Cloudflare CDN uses the
Free plan; R2 Standard storage has monthly free allowances with billed overages.
Managed Azure PostgreSQL, its HA standby, and its readable replica incur cloud
resource charges. Cloudflare and Azure profiles are active only for their exercises.
Real Cloudflare notification delivery uses verified test recipients; sending to
arbitrary recipients requires the Workers Paid plan. The simulator remains free
of cloud subscriptions.
[R2 pricing](https://developers.cloudflare.com/r2/pricing/),
[Cloudflare Free plan](https://www.cloudflare.com/plans/free/),
[Cloudflare Email Service pricing](https://developers.cloudflare.com/email-service/platform/pricing/),
[Azure PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)

### 2.1.1 Synthetic fixtures

| Fixture | Exercise |
|---|---|
| North Station, synchronous charging provider | Assigned operator access and authoritative command outcomes |
| South Station, queued charging provider | Unassigned operator denial and uncertain command outcomes |
| Fictional catalogue with varied equipment counts | Public browsing and concurrent sessions without sharing one charging outlet |
| Driver and operator from the Keycloak realm fixtures | Identity-linked ownership and station assignments |

Seeding is additive and retains existing station details. Reset restores charging
fixtures and removes charging workflow/file metadata while preserving browser sessions,
encryption keys, and identity-provider data. Reset is limited to relational
charging data; other stores are unaffected. Entity identifiers change when reset
recreates records. The reset operation is excluded from deployment.
[Reset procedure](../README.md#charging-data-reset).

### 2.2 Default timings

Short intervals make failure and recovery visible during a small demonstration.
These are overridable experiment settings, not production availability targets.

| Setting | Default | Meaning |
|---|---|---|
| API request timeout | 10 seconds | Cooperative deadline for inbound API requests |
| Telemetry interval | 2 seconds | Normal simulator cadence |
| Freshness limit | 10 seconds | Greater source-observation age is stale |
| Replica lag tolerance | 2 seconds | Greater lag triggers bounded fallback or a degraded view |
| Provider HTTP attempt timeout | 3 seconds | Maximum wait for one effect or outcome-query response |
| Safe effect-attempt budget | 3 total attempts | Initial attempt plus retries after 1 and 2 seconds; subject to provider safety and start eligibility |
| Automatic reconciliation | Every 5 seconds, at most 6 checks per pass | Unresolved work then requires operator investigation |

All effect layers share the operation budget; reconciliation queries use a separate
budget. An operator-triggered reconciliation starts an audited new query pass.
Scenario records define any callback/query retry settings beyond this baseline.

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
| External faults | Charging-provider simulator APIs and `NotificationProviderApi`, the third-party notification service simulator |

Verification uses correlated telemetry from the
[configured diagnostics path](#12-application-wiring).

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
| Duplicate/late observations | Historical evidence retained; current state follows sequence policy |
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
