# Lab Profiles and Verification

The lab exercises the [architecture](architecture.md) with synthetic fixtures,
bounded load, and controlled faults. [Domain rules](domain.md) remain constant
across profiles; [simulators](integrations.md) supply inspectable external evidence.

## 1. Execution profiles

| Profile | Resources and purpose |
|---|---|
| Local baseline | Aspire processes/containers: multiple gateway/API instances, independent workers, PostgreSQL primary and streaming read replica with two PgBouncer routes, MongoDB, Valkey, Elasticsearch, one RabbitMQ broker, Kafka, an S3-compatible storage container, Keycloak, simulators, and observability |
| Broker leader loss | Three RabbitMQ nodes with quorum queues; leader-loss and recovery behavior |
| Cloudflare files/CDN | R2 and Cloudflare CDN deliver versioned frontend assets and published media through a custom domain; real presigned transfers, warm/cold cache behavior, and purge |
| Azure database failover | Managed PostgreSQL HA plus readable replica/pooler routes; provider-supported failover exposes application interruption and recovery |

The local baseline covers redelivery, outbox recovery, projection replay, cache
loss, replication lag, and dependency interruptions. Clustered profiles are used
for quorum/leader behavior. Local replicas share a host failure boundary; they
do not demonstrate multi-zone availability.

MongoDB, Valkey, Elasticsearch, RabbitMQ, and Kafka need no cloud subscriptions.
Charging operators and notification delivery are controlled simulators. Local
S3 storage does not replace the hosted CDN or prove R2 signing/edge behavior.
The hosted profile uses the [R2 custom-domain and private-file boundaries](architecture.md#10-object-storage-and-cdn).

The local profile requires no paid product subscriptions. Cloudflare CDN uses the
Free plan; R2 Standard storage has monthly free allowances with billed overages.
Managed Azure PostgreSQL, its HA standby, and its readable replica incur cloud
resource charges. Cloudflare and Azure profiles are active only for their exercises.
[R2 pricing](https://developers.cloudflare.com/r2/pricing/),
[Cloudflare Free plan](https://www.cloudflare.com/plans/free/),
[Azure PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)

## 2. Default timings

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

## 3. Verification tools and signals

| Area | Tool |
|---|---|
| Domain and backend tests | xUnit |
| Real dependency integration | Testcontainers for .NET |
| Topology tests | Aspire.Hosting.Testing |
| Frontend behavior | Vitest and React Testing Library |
| Browser workflows | Playwright |
| Load and saturation | k6 |
| Network latency/isolation | Toxiproxy |
| External faults | Operator simulators and notification receiver |

The [observability stack](architecture.md#12-resilience-boundaries) correlates the
browser request, SQL mutation, outbox relay, message, worker attempt, and provider call.

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

## 4. Fault scenarios

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
| Pooler/primary lost | Interrupted work is visible; reconnect is bounded and uncertain commits are checked |
| Managed database failover | Endpoint/pool recovery, transaction outcomes, recovery time, and any lost commits recorded |
| Broker outage and recovery | Outbox retains pending work, exposes age, and drains without duplicate mutations |
| Clustered broker leader lost | Quorum queue behavior and publisher/consumer recovery observed |
| Stream unavailable | No false durable acknowledgement; bounded callback retry/polling and visible observation uncertainty |
| Consumer stalled / MongoDB unavailable | Projection lag increases; catch-up succeeds within retention or its recovery limit is exposed |
| Cache flushed/unavailable | Bounded fallback and warm-up; no loss of authoritative records |
| Valkey backplane unavailable and restored | Cross-instance live fan-out interruption is visible; bounded polling/reconnect obtains current snapshots, then fan-out resumes |
| Search unavailable/rebuilt | Limited lookup or visible unavailability; replacement index validated before switchover |
| Provider rate-limited/down | Its work isolated; retries bounded and durable intent retained |
| Upload succeeds but completion notice is lost | Sweep verifies object and safely resumes processing |
| Media worker fails after writing variants | Retry converges, old publication remains usable, orphan cleanup is visible |
| Signed upload URL reused | Immutable published bytes cannot be overwritten |
| Object storage lost | Files can fail while charging continues; processing retries retain references |
| Warm and uncached synthetic objects deleted at the R2 origin | Warm edge hits can serve until expiry/eviction; cache misses fail and unavailable media shows placeholders; purge removes stale cached publication |
| Summary/notification failure | Session stays completed; independent retry/exhaustion is inspectable |
| Live disconnect | Reconnect obtains a current snapshot before incremental updates |
| Cross-driver/station access | API and private-file authorization reject unauthorized access |
| Identity provider unavailable | Login/refresh failures visible; established sessions can use unexpired cached-signing-key-validated tokens while the session store is reachable |
| Authentication session store unavailable | Protected routes fail explicitly; no anonymous downgrade or use of disposable cache as session authority |
| Concurrent token refresh / logout | Refresh coordinated across gateway replicas; logout removes shared session and old cookies cannot recover it |
| Browser authentication boundaries | No OIDC tokens exposed to JavaScript; login uses gateway routes, state-changing requests require antiforgery, and API access still enforces ownership |
| Observability backend lost | Bounded buffering/drop; core processing continues and delivery loss is observable |

Each scenario records topology/version, settings, workload, initial state, fault
injection point/duration, expected behavior, correlated observations, final state,
and cleanup. Recovery is judged by business outcomes and durable evidence, not
merely a restarted process. Tests remain separated into domain, real-dependency,
contract, UI, resilience, and load concerns.
