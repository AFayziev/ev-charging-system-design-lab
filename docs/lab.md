# EV Charging System Design Lab

The lab implements the [architecture](architecture.md) with synthetic fixtures,
bounded load, and controlled faults. This reference defines technical setup,
execution profiles, experiment settings, and verification.
[Domain rules](domain.md) remain constant across profiles;
[integration contracts](integrations.md) define simulator and real-provider behavior.
The [implementation plan](implementation-plan.md) tracks delivery and pending selections.

## 1. Technical setup

### 1.1 Libraries and version sources

| Area | Selection |
|---|---|
| Backend | .NET 10, C# 14, ASP.NET Core 10 Minimal APIs, OpenAPI, Problem Details |
| Orchestration | Aspire 13.6 C# AppHost, local processes and Docker Linux containers |
| Frontend | React 19, TypeScript, Vite, React Router, Mantine, TanStack Query |
| Identity handlers | ASP.NET Core OpenID Connect and cookie authentication |
| Live delivery | SignalR .NET/JavaScript clients; StackExchange.Redis backplane on Valkey |
| Relational access | PostgreSQL 17; EF Core 10/Npgsql provider and long-lived Npgsql data sources |
| Documents | Official MongoDB C# driver |
| Cache | StackExchange.Redis connected to Valkey |
| Search | Official Elastic .NET client |
| Messaging | Wolverine RabbitMQ transport and EF Core/PostgreSQL durability |
| Observation stream | Confluent.Kafka client; Kafka in KRaft mode |
| Object storage | AWSSDK.S3 for local S3-compatible storage and R2 |
| File processing | SkiaSharp images, PDFsharp summaries, .NET ZIP/JSON diagnostics |
| HTTP resilience | Microsoft.Extensions.Http.Resilience and Polly |
| Instrumentation | OpenTelemetry ASP.NET Core, HTTP, and runtime instrumentation with OTLP export |

Exact SDK, package, and image versions are defined in [global.json](../global.json),
[Directory.Packages.props](../Directory.Packages.props), and
[ContainerVersions.props](../infra/ContainerVersions.props). Frontend dependencies
use a lockfile. Aspire SDK and hosting packages share a release family.

### 1.2 Application wiring

AppHost models each runnable .NET host using project references. The shared
`EvCharging.ServiceDefaults` library is referenced by all application and
simulator hosts and is not a runnable resource.

`WithReference` injects dependency endpoints. Gateway destinations use YARP's
`AddServiceDiscoveryDestinationResolver`; HTTP clients use
`Microsoft.Extensions.ServiceDiscovery`. Logical URIs such as `https+http://api/`
prefer HTTPS and fall back to HTTP when only HTTP is available.
[Service discovery](https://aspire.dev/fundamentals/service-discovery/)

The gateway is the only application host that references the platform API.
ChargingWorker references and waits for the gateway; its named `gateway` HTTP
client uses `https+http://gateway/api/`. YARP matches `/api/{**remainder}` and
removes `/api` before forwarding, preserving the remaining path and query.
`/api` reaches the API root, and nested routes reach the corresponding API path.
Charging-provider clients retain their separate external service references.
NotificationWorker's `notification-provider-api` client discovers the local
`NotificationProviderApi` third-party service simulator through its Aspire reference.
[YARP request transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0)

Local host endpoints remain available for diagnostics. Service references define
application routing; they do not enforce network isolation.

The multi-instance topology exposes a discoverable endpoint for each gateway and
API instance. HAProxy's backend list contains individual gateway endpoints; YARP's
clusters contain individual API endpoints. These destinations bypass Aspire's
automatic balancing proxy so HAProxy and YARP perform their own selection and
health handling. Endpoint addresses come from AppHost/discovery rather than fixed
ports. This wiring is verified by selected-instance diagnostics and instance-loss
tests.
[Aspire networking](https://aspire.dev/fundamentals/networking-overview/)

`AddServiceDefaults` configures discovery, standard HTTP resilience, health checks,
and OpenTelemetry logs, metrics, and traces. Automatic retries exclude POST, PUT,
PATCH, DELETE, and CONNECT; effect retries require an explicit provider policy.
YARP proxy forwarding uses its destination resolver rather than the default
`IHttpClientFactory` handler pipeline.
[HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)

HTTP hosts map `/health` and `/alive` in development. `/alive` runs checks tagged
`live`; `/health` runs all registered checks. The baseline self-check establishes
process responsiveness; workload dependency checks are separate. AppHost uses
`WithHttpHealthCheck` and `WaitFor` for HTTP dependencies and propagates its
`DOTNET_ENVIRONMENT` to child hosts. Workers use the shared defaults without HTTP
listeners.

OTLP export is enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured. Aspire
supplies dashboard export settings for the baseline. The optional `observability`
profile sends OTLP through the Collector, which exports traces to Tempo, metrics
to Prometheus, and logs to Loki, with Grafana querying these backends. It also
forwards telemetry to Aspire Dashboard so resource diagnostics remain available.
Health requests are excluded from tracing. Configuration and credentials come
from environment settings or .NET user secrets.

### 1.3 PostgreSQL access and pooling

Command and query handlers select separate data sources in the same API host.
EF Core manages transactions and migrations; read contexts use no tracking and
explicit projections. Parameterized Npgsql SQL supports PostgreSQL-specific queries.
Connections are borrowed briefly from reusable data sources and returned on disposal;
connection pooling and ORM context pooling are separate mechanisms.

PostgreSQL range/exclusion constraints with `btree_gist` enforce booking conflicts.
Session claims use conditional updates/constraints; operation identity has a unique
constraint. These mechanisms enforce the [domain rules](domain.md#5-reservations).
[Range constraints](https://www.postgresql.org/docs/17/rangetypes.html#RANGETYPES-CONSTRAINT),
[btree_gist](https://www.postgresql.org/docs/17/btree-gist.html)

Local primary and replica routes use separate PgBouncer containers in transaction
mode. Session pooling is a comparison route for session-affine features. Session
state, advisory locks, temporary objects, and prepared statements require pooler
compatibility checks. Migrations use a dedicated direct connection.

Client pool limits multiply across processes and routes; server pools also depend
on pooler instances and database/user combinations. Gateway session persistence
has its own primary-route pool within the aggregate connection budget.

Azure exercises use managed PgBouncer endpoints on port 6432 and direct connections
on 5432. Npgsql multi-host routing is an optional direct-connection comparison.
[Npgsql lifecycle](https://www.npgsql.org/doc/basic-usage.html),
[Npgsql routing](https://www.npgsql.org/doc/failover-and-load-balancing.html),
[PgBouncer compatibility](https://www.pgbouncer.org/features.html),
[Azure PgBouncer](https://learn.microsoft.com/en-us/azure/postgresql/connectivity/concepts-pgbouncer)

### 1.4 Message persistence and envelopes

Each message-processing API/worker host family uses a distinct Wolverine storage
schema in the application database. Replicas share that schema and durability
coordination. Each host registers its own handlers/listeners. SQL mutations and
outgoing envelopes commit together; non-SQL store updates use idempotent writes
before acknowledgement. The hosted durability runtime recovers stranded inbox/outbox
work.

Message envelopes include event ID, schema version, entity ID/version, occurrence
and receipt times, correlation/causation IDs, and trace context. They exclude secrets
and private file URLs. Stable job IDs and database uniqueness deduplicate scheduling.
[Wolverine durability](https://wolverinefx.net/guide/durability/),
[PostgreSQL storage](https://wolverinefx.net/guide/durability/postgresql),
[EF Core integration](https://wolverinefx.net/guide/durability/efcore/)

### 1.5 Storage and delivery

AWSSDK.S3 uses configured endpoints, credentials, and provider-compatible
signing/checksum settings. Adapter operations cover upload/download, HEAD
verification, publication copy, deletion, and presigned transfers. Contract tests
cover both the selected local service and R2. Browser transfers require storage
CORS rules for the application origin.

Public assets use an R2 custom domain in the same Cloudflare account/zone as the
bucket. Exact hostnames are environment configuration. `r2.dev` does not provide
CDN caching. Private buckets have no public custom-domain access; presigned URLs
use the R2 S3 API domain rather than the public asset domain.
[R2 .NET client](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/),
[Public buckets](https://developers.cloudflare.com/r2/buckets/public-buckets/),
[Presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/)

### 1.6 Frontend and live clients

Mantine supplies UI components. TanStack Query manages cached server data, mutations,
and bounded refetching; SignalR updates refresh or invalidate views. Mutation retries
retain request identity. Gateway session discovery uses `/auth/session`.

Valkey cache and SignalR backplane traffic use separate key/channel prefixes,
connections, and budgets through StackExchange.Redis. Live-route cookie affinity
keeps negotiation and transport on the same API. Authentication-session and
live-affinity cookies serve separate purposes. Client/server contract checks cover
fan-out, disconnect, and reconnection.
[SignalR scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0),
[Valkey compatibility](https://valkey.io/topics/migration/),
[YARP affinity](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/session-affinity?view=aspnetcore-10.0)

### 1.7 Gateway authentication conventions

ASP.NET Core OpenID Connect and cookie handlers implement the
[gateway authentication design](architecture.md#131-gateway-authentication).

| Browser route | Responsibility |
|---|---|
| `/auth/login` | Start login with a validated local return URL |
| `/auth/callback` | Complete OIDC and establish the browser session |
| `/auth/session` | Sign-in state, display claims, and antiforgery token; no OIDC tokens |
| `/auth/logout` | Antiforgery-protected logout of the application and provider session |
| `/identity/*` | Required Keycloak login/protocol resources; administrative routes stay private |

Keycloak's public hostname, issuer, and redirects match the gateway identity route;
server-side exchanges use internal connectivity. A PostgreSQL-backed `ITicketStore`
holds tickets and token state. The browser cookie contains a protected session
reference with Secure, HttpOnly, and SameSite=Lax settings. Shared Data Protection
keys/application identity and per-session refresh coordination support gateway replicas.
Keycloak uses a dedicated PostgreSQL database and pool, included in connection budgets.

Before attaching an access token upstream, the gateway strips browser-supplied
authorization/identity headers and its session cookie. API token validation checks
signature, issuer, audience, and expiry. API/live authentication failures return
unauthorized responses rather than HTML login redirects.
Worker and provider callback routes validate their service/source credentials and
forward the authenticated machine identity. Browser token substitution and cookie
handling apply only to browser-session routes; machine callers do not enter the
interactive OIDC flow.
[Keycloak reverse proxy](https://www.keycloak.org/server/reverseproxy),
[Ticket store](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0),
[Antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0),
[YARP authentication](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0)

### 1.8 Notification adapters

NotificationWorker selects `Simulator` (local default) or `Cloudflare` through
configuration. The simulator uses the discovery-aware client described in
[application wiring](#12-application-wiring). In Cloudflare mode the worker has
no simulator reference or startup dependency; missing configuration fails
explicitly without switching providers. Delivery semantics are defined in
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

## 2. Experiments and verification

### 2.1 Execution profiles

| Profile | Resources and purpose |
|---|---|
| Local baseline | Aspire processes/containers: multiple gateway/API instances, independent workers, PostgreSQL primary and streaming read replica with two PgBouncer routes, MongoDB, Valkey, Elasticsearch, one RabbitMQ broker, Kafka, an S3-compatible storage container, Keycloak, simulators, and Aspire Dashboard diagnostics |
| Optional `observability` | Adds the [selected observability backends](architecture.md#2-technology-selections) for telemetry routing, backend queries, custom dashboards, and export/backend failure exercises; startup/health gates apply only when enabled |
| Broker leader loss | Three RabbitMQ nodes with quorum queues; leader-loss and recovery behavior |
| Cloudflare files/CDN | R2 and Cloudflare CDN deliver versioned frontend assets and published media through a custom domain; real presigned transfers, warm/cold cache behavior, and purge |
| Cloudflare notification delivery | The real email adapter replaces the notification service simulator; verified test recipients and recorded provider/delivery outcomes |
| Azure database failover | Managed PostgreSQL HA plus readable replica/pooler routes; provider-supported failover exposes application interruption and recovery |

The local baseline covers redelivery, outbox recovery, projection replay, cache
loss, replication lag, and dependency interruptions. Clustered profiles are used
for quorum/leader behavior. Local replicas share a host failure boundary; they
do not demonstrate multi-zone availability.

MongoDB, Valkey, Elasticsearch, RabbitMQ, and Kafka need no cloud subscriptions.
The synchronous and queued charging providers are controlled simulators.
`NotificationProviderApi` simulates a third-party delivery service without sending
real messages. Hosted notification delivery uses the [selected adapter](#18-notification-adapters).
Local S3 storage does not demonstrate hosted CDN or R2 signing behavior;
the files/CDN profile uses the [R2 delivery boundaries](architecture.md#10-object-storage-and-cdn).

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

The [initial topology milestone](implementation-plan.md#3-milestone-tasks-and-acceptance)
starts every local baseline resource with stub application/simulator hosts and
isolated protocol smoke checks. Resource availability is verified separately from
the business and fault acceptance scenarios below.

### 2.2 Default timings

Short intervals make failure and recovery visible during a small demonstration.
These are overridable experiment settings, not production availability targets.

| Setting | Default | Meaning |
|---|---|---|
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
| SQL and pooling | Client/server counts by route/process, acquisition waits, transaction/query duration, locks, replay lag |
| Jobs | Queue depth/age, oldest outbox age, attempts, dead letters, uncertain operations |
| Streams/documents | Consumer lag, processing errors, retained history, latest-view age |
| Providers | Physical state, command/correlation records, latency, rate limiting, circuit state, reconciliation results |
| Search/cache | Index freshness/rebuild status, hit rate, fallback load |
| Files/CDN | Pending uploads, processing attempts, object versions, origin errors, edge hits/misses |
| Frontend | Reconnects, operation identity/results, unavailable dependencies, stale progress |

### 2.4 Fault scenarios

| Scenario | Expected behavior and acceptance evidence |
|---|---|
| Concurrent overlapping reservations | One conflicting booking succeeds; primary constraints remain effective across API instances |
| Cancellation races start | One eligible transition wins; cancelled bookings cannot dispatch a start |
| Slot ends with start queued or uncertain | Unattempted work fails safely; a possible external effect retains its claim and is reconciled |
| Start applies but response is lost | One logical operation; visible uncertainty resolves against provider evidence without blind resubmission |
| Worker dies after effect before acknowledgement | Redelivery retains operation/attempt identity; SQL mutations deduplicate and provider ambiguity follows its contract |
| Duplicate/late observations | Historical evidence retained; current state follows sequence policy |
| Replay into a replacement view | Validated rebuilt projection; no charging commands or notifications |
| Telemetry stops | Timestamped state becomes stale; silence does not imply stopped or offline |
| Stop confirmed before final usage | Session waits for final evidence; completion and summary use matching frozen facts |
| Replica replay paused or replica lost | Lag visible; bounded fallback protects primary; immediate write confirmation remains correct |
| Read saturation / API-worker scaling | Bounded acquisition/rejection; separate route budgets and aggregate client/server counts remain observable |
| Gateway/API process lost | Routing reaches healthy instances; browser session remains usable across gateways; in-flight operations recover by identity |
| Gateway unavailable during observation submission | Worker/provider submissions retain event identity and follow bounded retry/recovery; acknowledgement still requires durable ingestion, and no caller bypasses the gateway |
| Pooler/primary lost | Interrupted work is visible; reconnect is bounded and uncertain commits are checked |
| Managed database failover | Endpoint/pool recovery, transaction outcomes, recovery time, and any lost commits recorded |
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
| Concurrent token refresh / logout | Refresh coordinated across gateway replicas; logout removes shared session and old cookies cannot recover it |
| Browser authentication boundaries | No OIDC tokens exposed to JavaScript; login uses gateway routes, state-changing requests require antiforgery, and API access still enforces ownership |
| Dashboard export unavailable | Bounded buffering/drop; core processing continues and export loss is observable through local diagnostics |
| Collector or observability backend lost (optional profile) | Bounded buffering/drop and inspectable pipeline loss; core processing continues and telemetry delivery resumes after recovery |

Each scenario records topology/version, settings, workload, initial state, fault
injection point/duration, expected behavior, correlated observations, final state,
and cleanup. Recovery is judged by business outcomes and durable evidence, not
merely a restarted process. Tests remain separated into domain, real-dependency,
contract, UI, resilience, and load concerns.
