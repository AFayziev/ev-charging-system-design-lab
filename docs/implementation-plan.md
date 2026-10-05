# EV Charging Implementation Plan

## 1. Scope and tracking

This plan sequences implementation of the [architecture](architecture.md).
[Domain rules](domain.md) and [integration contracts](integrations.md) define
behavior. The [lab reference](lab.md) defines technical setup, execution profiles,
and fault scenarios for acceptance.
Milestones assemble the selected topology; component responsibilities and domain
policies remain consistent throughout implementation.

Task IDs are stable tracking references. Unchecked tasks are incomplete; checked
tasks satisfy their implementation and verification requirements. Milestone status
is one of `Planned`, `In progress`, `Blocked`, or `Done`. A blocked item records the
specific dependency. `Done` requires every required task and the milestone's
acceptance criteria.
Optional and hosted gates remain independently tracked and do not
block baseline local acceptance. Optional-profile gates apply when that profile
is enabled; hosted gates require their provider setup.
Milestone status is recorded in the table below.

Task checkboxes and milestone status are updated in the same commit as the
implementation. The [commit conventions](../AGENTS.md#commits) link task IDs to
implementation history. Verification summaries belong in pull request descriptions
when applicable.

## 2. Suggested order and dependencies

The first milestone boots the complete local topology with stub application and
simulator hosts, real dependency containers, and protocol-level smoke checks.
Subsequent milestones implement application flows within those hosts. Frontend
work and fault verification accompany each flow. Instrumentation begins with the
first processes and expands as functional boundaries become active.

| Order / ID | Milestone | Required predecessors | Status |
|---|---|---|---|
| M00 | Repository and design baseline | None | Done |
| M01 | Bootable Aspire topology and dependency smoke checks | M00 | In progress |
| M02 | Domain persistence, read/write routes, and pooling | M01 | Planned |
| M03 | Gateway authentication and shared frontend shell | M02 | Planned |
| M04 | Durable jobs, scheduling, and notification delivery | M02 | Planned |
| M05 | Station catalog and reservations | M03, M04 | Planned |
| M06 | Charging providers and start/stop execution | M05 | Planned |
| M07 | Observation stream, projections, and completion | M06 | Planned |
| M08 | Live delivery and cache recovery | M07 | Planned |
| M09 | Search and station discovery | M07 | Planned |
| M10 | Local files, media, summaries, and diagnostics | M07 | Planned |
| M11 | Hosted R2 and Cloudflare CDN | M10 | Planned |
| M12 | Managed PostgreSQL failover | M08 | Planned |
| M13 | Complete lab demonstrations and fault verification | M01–M12; hosted gate M04.08 | Planned |

Independent work becomes available at several points:

- M03 and M04 can proceed independently after M02.
- The M04.08 hosted email gate depends on M04.05 and provider setup; it does not
  block M05 or the local demonstrations.
- M08, M09, and M10 can proceed independently after M07. File storage and media
  groundwork can begin after M04; completed-session documents depend on M07.
- M11 follows the local file contracts. M12 can proceed independently of M09–M11
  once charging, observation, and reconnect behavior are available.

The first authenticated booking demonstration ends at M05. Start/stop execution
and uncertain outcome recovery are demonstrable at M06. A completed charging
session with final usage is demonstrable at M07. The full local feature set is
available after M08–M10; hosted behavior is verified by M04.08, M11, and M12.

## 3. Milestone tasks and acceptance

### M00 — Repository and design baseline

- [x] M00.01 Establish top-level `src`, `infra`, `tests`, `docs`, and `tools` directories.
- [x] M00.02 Add editor, Git attribute, and ignore configuration.
- [x] M00.03 Record system scope, architecture, domain vocabulary/policies,
  simulator contracts, and lab profiles.

Acceptance: the repository contains the standalone design references and the
monorepo baseline.

### M01 — Bootable Aspire topology and dependency smoke checks

- [x] M01.01 Add the .NET solution, SDK/version configuration, central NuGet
  management, and pinned dependency/image versions compatible with the architecture.
- [x] M01.02 Add the Aspire C# AppHost, YARP gateway, API host, and independent
  charging, observation, search index, cache, media, and notification worker hosts using
  [settled host responsibilities](architecture.md#3-components-and-request-topology).
  Add stub API hosts for the synchronous and queued provider simulators and the
  third-party notification service simulator (`NotificationProviderApi`). Wire HTTP
  dependencies with Aspire references and service discovery: gateway destinations,
  gateway-routed platform clients, and direct external simulator clients. Share
  defaults for HTTP resilience, dashboard telemetry, and process health.
- [x] M01.03 Add the React/TypeScript application with Vite, React Router,
  Mantine, TanStack Query, and frontend lockfile.
- [x] M01.04 Wire frontend hosting with gateway-routed backend paths,
  logical gateway/API services with runtime-managed replicas, parameters,
  endpoints, and development secrets. Verify discovery, replica reachability,
  SPA navigation, and development WebSocket routing.
  Use the [application wiring](lab.md#12-application-wiring).
- [x] M01.05 Establish OpenAPI, Problem Details, standard W3C trace correlation,
  bounded request cancellation, health endpoints in all environments, and
  development-only instance diagnostics.
  Stub hosts expose diagnostics; unimplemented business routes cannot report success.
- [x] M01.06 Verify baseline diagnostics across the topology: correlated
  logs/traces, metrics, instance identity, and resource health.
- [ ] M01.07 Add xUnit/Testcontainers/Aspire testing foundations and Vitest,
  React Testing Library, and Playwright workflow harnesses.
- [ ] M01.08 Select and pin the free local S3-compatible container; verify it can
  support the file operations specified in the architecture before adopting it.
- [ ] M01.09 Wire all local baseline resources through Aspire: PostgreSQL primary
  and streaming read replica, two PgBouncer routes, Keycloak and its dedicated
  database, MongoDB, Valkey, Elasticsearch, RabbitMQ, Kafka in KRaft mode, and local
  S3 storage. Configure credentials, volumes, endpoints, references, and readiness
  waits using integrations or custom container resources as appropriate.
- [ ] M01.10 Add bounded protocol-level smoke checks through intended application
  connections: primary/replica SQL via poolers, replica read-only/replication behavior,
  MongoDB write/read, Valkey cache/Pub/Sub, Elasticsearch index/query, RabbitMQ
  publish/consume, Kafka append/read, S3 upload/download/presigned transfer, Keycloak
  reachability/discovery, and simulator API stub reachability. Test records
  use isolated synthetic namespaces and are cleaned up.
- [ ] M01.11 Verify clean first startup, restart with retained volumes, stop/reset,
  application-to-container networking, gateway/API instance loss, and smoke-check
  failure reporting when a dependency is unavailable. Document prerequisites and
  the local start/test/stop/reset commands.
- [ ] M01.12 Optional profile gate, after M01.06: wire the
  [`observability` profile](lab.md#21-execution-profiles). Verify export routing,
  backend queries, custom dashboards, restart, and its
  [fault scenarios](lab.md#24-fault-scenarios).
- [ ] M01.13 After M01.03, M01.04, and M01.06, implement
  [frontend diagnostics](lab.md#19-frontend-diagnostics), startup configuration,
  gateway tunnel, and build integration. Verify disabled behavior, capture metadata,
  bounded export failure, and local builds without hosted credentials.
- [ ] M01.14 Optional hosted gate, after M01.13: verify the Sentry execution
  profile against a configured project using a built frontend,
  [diagnostic signals](lab.md#23-verification-tools-and-signals), and
  [fault scenarios](lab.md#24-fault-scenarios).

Acceptance:

- One documented AppHost command starts every resource in the local baseline,
  including the frontend, application stubs, and external simulator API stubs.
- All required resources reach their expected healthy state within bounded waits,
  and protocol smoke checks prove credentials, routing, and usable connections.
- The browser loads the SPA from the frontend host without traversing YARP;
  API requests pass through the gateway and are traceable across gateway/API instances.
- Selected-instance diagnostics prove ingress/gateway routing through logical
  services and runtime replica balancing; losing one gateway/API instance routes
  new requests to a healthy instance;
  dependency loss produces a bounded, named smoke-check failure.
- Restart/reset behavior is reproducible, and telemetry reaches Aspire Dashboard.

Health checks remain workload-specific as functionality is added. Passing this
milestone establishes infrastructure availability and wiring; authentication,
durable business handling, and domain workflows have their own later acceptance
criteria.

[Aspire AppHost](https://aspire.dev/get-started/app-host/),
[Health checks](https://aspire.dev/fundamentals/health-checks/),
[Topology testing](https://aspire.dev/testing/write-your-first-test/)

### M02 — Domain persistence, read/write routes, and pooling

- [ ] M02.01 Implement canonical identifiers, UTC/time-zone handling, state
  dimensions, measurement types, and domain policy tests from the domain reference.
- [ ] M02.02 Add PostgreSQL migrations for equipment, assignments, reservations,
  sessions, operations/attempts, deadlines, and file metadata; define persistence
  boundaries for authentication and host-specific Wolverine message-storage schemas.
- [ ] M02.03 Add synthetic equipment, driver, operator-assignment, and provider
  mappings; provide reproducible fixture seeding and lab-data reset.
- [ ] M02.04 Extend the M01 database topology with application-specific
  least-privilege roles and a dedicated direct migration connection; verify grants
  and transaction-pooling behavior against the actual application schema.
- [ ] M02.05 Implement separate long-lived Npgsql data sources, EF Core command
  and no-tracking query contexts, and a dedicated gateway-session primary pool.
- [ ] M02.06 Add database constraints and conditional updates for booking overlap,
  session exclusivity, operation identity, and permitted transitions.
- [ ] M02.07 Implement bounded acquisition/query/transaction waits, lag detection,
  primary fallback admission, and metrics for both client and server pools.
- [ ] M02.08 Verify transaction-pooling compatibility, replica read-only access,
  saturation, replica interruption, and uncertain-commit lookup by operation identity.

Acceptance: seeded catalog queries use the replica route, transactional checks
use the primary, and immediate write results remain authoritative during replica
lag. A modest workload exposes pool waiting without unbounded primary fallback.
Constraint checks use real PostgreSQL across competing connections. No SQL
transaction remains open during an external call.

### M03 — Gateway authentication and shared frontend shell

- [ ] M03.01 Configure the bootstrapped Keycloak instance with application realm,
  synthetic users, and the gateway's confidential OIDC client; verify its dedicated
  PostgreSQL database/pool configuration.
- [ ] M03.02 Implement the documented `/auth` routes and gateway-proxied
  `/identity` resources, including matching public issuer/redirect configuration.
- [ ] M03.03 Implement the shared PostgreSQL `ITicketStore`, protected token state,
  Data Protection key sharing, per-session refresh coordination, and logout removal.
- [ ] M03.04 Add antiforgery validation, validated return URLs, cookie attributes,
  browser header/cookie stripping, bearer forwarding, and API JWT validation.
  Keep machine routes outside browser session/token-substitution policies.
- [ ] M03.05 Build the driver/operator navigation shell, session discovery,
  sign-in/sign-out states, and shared API/error handling through the gateway.
- [ ] M03.06 Establish ownership/assignment authorization and audit infrastructure
  for later protected workflows.
- [ ] M03.07 Verify gateway loss, concurrent refresh, logout, session-store outage,
  Keycloak outage, and browser authentication boundaries with real dependencies.

Acceptance: both roles sign in through the gateway origin. A browser session
survives gateway-instance loss; JavaScript receives no OIDC tokens. Invalid
antiforgery requests and forged identity headers fail. Authorization is enforced
by the API, and session-store failure cannot grant anonymous access.

### M04 — Durable jobs, scheduling, and notification delivery

- [ ] M04.01 Integrate the bootstrapped RabbitMQ instance with Wolverine and
  EF Core/PostgreSQL inbox/outbox persistence, embedded relay/recovery in each host
  family, broker confirmations, and independent consumer queues.
- [ ] M04.02 Define versioned envelopes and competing-consumer jobs versus topic
  exchanges/subscriber queues, preserving correlation and trace context.
- [ ] M04.03 Implement persisted deadlines/delayed work and recurring scheduler
  producers in their owning worker hosts with stable job identities, due-state
  rescan after restart, and duplicate-safe submission across replicas.
- [ ] M04.04 Configure bounded concurrency, per-workload/provider isolation,
  explicit retry ownership, dead-letter inspection, and queue/outbox age signals.
- [ ] M04.05 Implement the third-party notification service simulator in
  NotificationProviderApi and a provider-independent delivery adapter contract.
  Add simulator and Cloudflare Email Service adapters selected explicitly by
  configuration;
  the simulator remains the local default. Keep credentials in secrets and provider
  setup separate from job handling. Add durable delivery-attempt records; host
  delivery in the notification worker with its own queue, concurrency budget,
  and durable-message storage schema. Verify synthetic faults and adapter response
  mapping, retry/ambiguity behavior, and configuration without real delivery.
  Business event triggers are integrated in subsequent milestones.
- [ ] M04.06 Verify commit-before-publish, broker outage/drain, duplicate delivery,
  consumer crash, embedded-relay recovery after host loss, duplicate recurring
  producers, scheduler restart, and poison-message isolation.
- [ ] M04.07 Add the three-node RabbitMQ quorum-queue profile and verify broker
  leader loss and publisher/consumer recovery.
- [ ] M04.08 Hosted gate: configure Cloudflare Email Service and verify the real
  adapter with verified test recipients, including acceptance versus recipient-delivery
  evidence. Record setup, outcomes, and usage; remove exercise resources as applicable.
  Depends on M04.05 and the [hosted setup](lab.md#18-notification-adapters).

Acceptance: committed test work survives broker/process interruption. Duplicate
delivery cannot duplicate a transactional mutation. Topic subscribers receive
their own copies independently, and scheduled work survives worker restart.
Failed notification delivery remains separately inspectable. Hosted email evidence
is required for M04.08, independently of the local milestone gate.

### M05 — Station catalog and reservations

- [ ] M05.01 Add driver-facing station/equipment details and assigned-operator
  station edits, using the documented read/write routes and resource authorization.
- [ ] M05.02 Add reservation create/list/detail/cancel endpoints and transactional
  deadline/outbox changes governed by the reservation policy.
- [ ] M05.03 Add driver catalog, station detail, booking, and reservation screens;
  immediate mutation results remain visible while list projections catch up.
- [ ] M05.04 Add operator station content forms and edit audit records; media
  controls depend on M10.
- [ ] M05.05 Execute expiry work with current-state checks and safe restart behavior.
- [ ] M05.06 Connect reservation events to notification jobs and verify delivery
  against the third-party service simulator, independently of the booking transaction.
- [ ] M05.07 Verify competing bookings, adjacent slots, cross-driver access,
  unauthorized station edits, deadline restart, and replica-lag UI behavior.

Acceptance: an authenticated driver reserves and cancels eligible slots while an
assigned operator edits station content. Competing API instances preserve booking
constraints. Time-zone display preserves the UTC booking interval. Notification
failure leaves accepted reservations intact.

### M06 — Charging providers and start/stop execution

- [ ] M06.01 Implement the synchronous and queued .NET provider simulators with
  persistent physical state, command/session/correlation query records, and
  inspectable fault controls.
- [ ] M06.02 Implement provider adapters, payload/unit normalization, authenticated
  calls, explicit timeout/circuit policies, and per-provider concurrency limits.
- [ ] M06.03 Add start/stop API acceptance with durable operation identity,
  connector execution claims, inbox/outbox work, and concurrency protection.
- [ ] M06.04 Implement charging-worker handlers that recheck eligibility, persist
  attempt identity and shared effect-attempt budgets, then call the selected provider.
- [ ] M06.05 Add durable outcome queries, scheduled reconciliation, exhaustion
  handling, and audited reconciliation requests from assigned operator users.
- [ ] M06.06 Add driver start/stop and operation-status screens plus operator
  session/attempt investigation; bounded polling displays pending/uncertain outcomes.
- [ ] M06.07 Verify cancellation/start races, queued-start expiry, uncertain claims
  after expiry, rejected starts/stops, overruns, and idempotency-key payload mismatch.
- [ ] M06.08 Verify both lost-response contracts, crash after external effect,
  provider restart, rate limiting/outage isolation, and recovery by correlation
  identity when a response did not reveal the provider command ID.

Acceptance: start and stop execute through RabbitMQ workers for both providers.
An uncertain effect remains recoverable and cannot authorize a conflicting start.
Retries across transport and job layers obey the same operation budget. Queued
provider effects are not blindly repeated after a timeout. Stop confirmation can await final
usage; completion evidence processing is supplied by M07.

### M07 — Observation stream, projections, and completion

- [ ] M07.01 Implement simulator observations, authenticated callbacks and recovery
  polling, stable source event IDs/sequences, and matching final-session evidence.
  Route provider callbacks and worker observation submissions through the gateway
  with source/service authentication, without browser-session authentication.
- [ ] M07.02 Configure observation topics and application producers on the
  bootstrapped Kafka instance; validate/envelope observations and append durably
  before acknowledging callbacks, keyed by provider and connector.
- [ ] M07.03 Add independent consumer groups in the observation worker for MongoDB
  history/latest views and SQL lifecycle/usage evidence, with safe offsets,
  deduplication, and trace propagation.
- [ ] M07.04 Implement sequence-conditional latest views, source-time freshness,
  cumulative energy validation, checkpoint recovery, and stalled-session detection.
- [ ] M07.05 Implement completion transactions, frozen final usage, and completion
  events; connect notification delivery to those events.
- [ ] M07.06 Add progress/history query endpoints and driver/operator observation
  views with freshness labels, canonical units, and diagnostic anomalies.
- [ ] M07.07 Add isolated observation-view replay/rebuild tooling that cannot send
  provider effects or notifications and preserves original observation times.
- [ ] M07.08 Verify stream outage/ambiguous append, consumer crash/stall, MongoDB
  outage, duplicates/out-of-order evidence, timestamp skew, missing/decreasing energy,
  stop before final usage, terminal evidence before stop, and late contradictions.

Acceptance: the driver completes a charging session whose final facts match the
simulator. Accepted observations survive document-store interruption. Replaying
retained history rebuilds projections without external effects or freshening old
evidence. Final usage remains unchanged by later contradictory observations.

### M08 — Live delivery and cache recovery

- [ ] M08.01 Implement hot catalog/progress views against Valkey with
  StackExchange.Redis, expiry, and version-conditional writes. Assign asynchronous
  updates/invalidation to the cache worker; keep query access and MongoDB/SQL fallback in the API.
- [ ] M08.02 Add API cache bypass budgets and stampede protection, independent cache
  worker warm-up/recovery, and cache-hit/fallback metrics.
- [ ] M08.03 Add authenticated SignalR endpoints, independent live-update
  subscriptions, Valkey backplane fan-out, and API live-route affinity through YARP.
- [ ] M08.04 Integrate browser live updates with TanStack Query, version checks,
  bounded reconnect, snapshot recovery, and polling fallback.
- [ ] M08.05 Show live connection health separately from charger observation
  freshness and preserve pending operations across reconnects.
- [ ] M08.06 Verify cross-instance fan-out with pinned versions, cache flush/outage,
  cache-worker loss/recovery independently of search and observations, backplane
  outage/recovery, API loss, missed updates, and lost affinity targets.

Acceptance: two API instances deliver updates through the backplane. A disconnected
client restores current state from a snapshot before applying later versions.
Cache loss preserves authentication and charging facts; bounded fallback avoids
overloading authoritative stores.

### M09 — Search and station discovery

- [ ] M09.01 Integrate the bootstrapped Elasticsearch instance with its .NET client;
  define text, location, capability, and advisory connector-availability index fields.
- [ ] M09.02 Add version-aware station/event and observation consumers in the search
  index worker with separate subscriptions/checkpoints from cache/observation
  workers; transactional booking eligibility remains primary-side.
- [ ] M09.03 Add discovery endpoints and a focused driver search/filter interface
  using distance, connector capabilities, and advisory availability.
- [ ] M09.04 Implement bounded SQL lookup during search failure and visible stale
  or unavailable discovery results.
- [ ] M09.05 Add search index worker rebuild, validation, switchover, freshness
  metrics, and verification for delayed updates, duplicates, outage, rebuild,
  and worker loss/recovery independently of cache and observations.

Acceptance: search uses the independent index and exposes its freshness limits.
An advisory result cannot bypass booking constraints. A replacement index is
validated before publication, and discovery failure does not block unrelated
commands for known equipment.

### M10 — Local files, media, summaries, and diagnostics

- [ ] M10.01 Extend the M01 S3 smoke checks into storage-adapter contract verification
  for file workflows, including HEAD, publication copy, deletion, and retry behavior.
- [ ] M10.02 Implement the `AWSSDK.S3` storage adapter, private/public buckets,
  CORS, presigned authorization, file ownership/state records, and transfer limits.
- [ ] M10.03 Add private upload allocation, direct browser transfer, verification,
  SkiaSharp variants, immutable publication keys, and transactional publication events;
  processing runs in the media worker.
- [ ] M10.04 Add operator upload/publication controls and driver station media with
  placeholders and old-publication retention during processing failures.
- [ ] M10.05 Generate private PDFsharp session summaries from frozen completion
  facts in the media worker; add authorized driver download and deterministic
  regeneration.
- [ ] M10.06 Generate sanitized ZIP/JSON diagnostic bundles from SQL attempts,
  MongoDB observations, and provider evidence in the media worker; add
  assigned-operator download.
- [ ] M10.07 Add missing-notice reconciliation, abandoned-upload/orphan cleanup,
  retry-safe partial-write recovery, and file-processing queue signals.
- [ ] M10.08 Verify upload-notice loss, worker crash after writing variants, reusable
  upload URLs, expired/private URLs, cross-user access, storage outage, and summary
  failure after session completion; verify media worker loss and recovery
  without interrupting notification delivery or charging execution.

Acceptance: station media is visible only after publication. Reusing an original
upload URL cannot overwrite published bytes. Summaries reproduce frozen usage;
diagnostics expose correlated evidence without secrets. File failure leaves
charging completion intact.

### M11 — Hosted R2 and Cloudflare CDN

- [ ] M11.01 Add the hosted files profile, R2 credentials/endpoints, and private
  and public buckets through environment configuration.
- [ ] M11.02 Attach the public bucket to a configured subdomain of the existing
  Cloudflare-managed domain; keep private buckets off public delivery routes.
- [ ] M11.03 Run adapter contract tests against R2, including browser CORS,
  presigned transfers, metadata/checksum handling, publication copy, and deletion.
- [ ] M11.04 Publish versioned frontend assets and media through CDN cache rules;
  the frontend entry page references the matching asset build.
- [ ] M11.05 Implement the CDN purge adapter and publication/removal workflow,
  exposing hit/miss/age and purge results for synthetic assets.
- [ ] M11.06 Verify warm versus cold requests, origin deletion, cache expiry/purge,
  unavailable-media placeholders, and separation of private transfer URLs from CDN URLs.
- [ ] M11.07 Add repeatable profile setup/cleanup and record usage/cost evidence
  without changing unrelated DNS records or persisting credentials.

Acceptance: real edge caching and R2 signing are demonstrated on the lab domain.
Authentication/API routes and private files are excluded from shared caching.
Deleting synthetic origins produces observable warm-hit versus cold-miss behavior,
and purge removes the old publication. Setup and cleanup are reproducible.

### M12 — Managed PostgreSQL failover

- [ ] M12.01 Add the Azure database profile with primary, provider-managed HA
  standby, separate readable replica, managed pooler routes, and configuration/secrets.
- [ ] M12.02 Run migrations and fixtures using the direct route; verify primary,
  replica, identity, and gateway-session pool budgets and read-only permissions.
- [ ] M12.03 Run active charging/observation workloads during provider-supported
  failover and record endpoint/pool recovery and transaction interruption.
- [ ] M12.04 Verify uncertain-commit lookup, operation resubmission, durable work
  recovery, browser-session continuity, and replica lag/fallback during the exercise.
- [ ] M12.05 Record any observed lost commits, recovered outcomes, bounded waits,
  and measured recovery time; automate teardown and record remaining-resource costs.

Acceptance: the provider performs database promotion; the application handles
interrupted transactions and connections without inventing commit outcomes or
duplicating operations. HA standby and query replica roles are separately visible.
The exercise ends with recorded business outcomes and resource cleanup.

### M13 — Complete lab demonstrations and fault verification

- [ ] M13.01 Assemble repeatable driver and operator demonstrations covering
  discovery, booking, charging, final usage, files, notification, and reconciliation.
- [ ] M13.02 Add k6/Toxiproxy scenario tooling for small concurrency/pool budgets,
  queue/consumer backlogs, provider isolation, and dependency interruption.
- [ ] M13.03 Complete and index evidence for the
  [fault scenarios](lab.md#24-fault-scenarios) in their applicable profiles.
- [ ] M13.04 Verify the [diagnostic signals](lab.md#23-verification-tools-and-signals)
  across integrated workflows; extend M01.12 dashboards when that profile is enabled.
- [ ] M13.05 Add reproducible restore/build, migrations, seeding/reset, test commands,
  profile start/stop, cloud cleanup, and troubleshooting references under `tools`
  and `docs` as needed.
- [ ] M13.06 Verify startup from a clean checkout and rerun the full demonstration
  after controlled faults; document retained state and explicit recovery limits.

Acceptance: every selected component participates in an observable application
flow. Every applicable lab fault scenario has repeatable injection and recorded
business outcomes. Local demonstrations require no cloud subscription; hosted exercises
use the same application contracts with configured provider endpoints.

## 4. Component and verification coverage

| Component / boundary | Implementation | Principal verification |
|---|---|---|
| Aspire, process hosts, OCI containers | M01; resource wiring in each milestone | M01, M13 startup/reset |
| Ingress, gateway, and runtime replicas | M01, M03, M08 | Logical destination health, replica selection/loss, affinity, authentication continuity |
| React, Mantine, TanStack Query | M01, M03, M05–M11 | Role workflows, stale/pending states, reconnect, private access |
| Keycloak, BFF, shared ticket/key state | M03 | Browser boundaries, refresh/logout races, identity/session-store outage |
| PostgreSQL, EF Core, Npgsql | M02, M05–M07, M10 | Conflicts, ordering, commits, ownership, completion |
| Read replica and PgBouncer | M02, M12 | Lag, read-only routes, pool saturation, reconnect/failover |
| RabbitMQ jobs/topics, Wolverine inbox/outbox | M04 onward | Publish/ack crash windows, redelivery, independent subscribers |
| Durable scheduler / reconciliation | M04–M07, M10 | Restart, deadlines, exhaustion, expiry races, missing notices |
| Provider simulators, HTTP resilience | M06, M07 | Lost responses, idempotency differences, rate limits, physical evidence |
| Notification provider simulator/adapters and worker | M04, M05, M07 | Synthetic faults, real email delivery, ambiguity, independent retry |
| Kafka and observation consumers | M07 | Durable acceptance, lag, offset recovery, replay |
| MongoDB | M07, M10 | Latest/history ordering, outage catch-up, diagnostic queries |
| Valkey and SignalR backplane | M08 | Cache flush, bounded fallback, fan-out loss/recovery |
| Elasticsearch | M09 | Index freshness, outage, validated rebuild |
| Independent cache and search index workers | M08, M09 | Separate lag/checkpoint recovery, worker loss, warming/rebuild without session effects |
| S3 adapter / local container | M01, M10 | Protocol smoke checks, transfer authorization, partial writes, cleanup, immutable publication |
| SkiaSharp, PDFsharp, ZIP/JSON | M10 | Processing recovery, frozen summary facts, diagnostic sanitization |
| R2 and Cloudflare CDN | M11 | Real signing, cache hits/misses, origin deletion, purge |
| Azure managed PostgreSQL | M12 | Provider failover, pool/session recovery, uncertain commits |
| OpenTelemetry and Aspire Dashboard | M01.06 onward; M13 views | Correlated evidence, resource health, bounded export during outage |
| Optional observability profile | M01.12; M13 profile scenarios | Telemetry routing, queries/custom dashboards, bounded buffering/drop during outage |
| Frontend diagnostics / Sentry | M01.13; hosted gate M01.14 | [Signals](lab.md#23-verification-tools-and-signals), [fault scenarios](lab.md#24-fault-scenarios) |
| xUnit, Testcontainers, Aspire testing | M01 onward | Domain, dependency, contract, topology, resilience verification |
| Vitest, React Testing Library, Playwright | M01 onward | UI semantics and authenticated browser workflows |
| k6 and Toxiproxy | M13; available for earlier gates | Saturation and network-fault evidence |

Scenario definitions remain in [the lab reference](lab.md#24-fault-scenarios).
Milestone gates verify relevant faults as their boundary becomes usable; M13
closes coverage gaps and repeats the integrated demonstrations.

## 5. Pending implementation selections

| Selection | Constraint / dependency | Resolution task |
|---|---|---|
| Local S3 container product/image | Free local use; required S3 operations and browser presigned transfers; R2 contract parity | M01.08; adapter verification in M10.01/M11.03 |
| Cloudflare email sender and verified test recipients | Domain onboarding, scoped credentials, and real-delivery setup; cloud integration is already selected | M04.08 |
| Hosted public asset hostname | Subdomain in the existing Cloudflare account; separate public assets and private transfer endpoints | M11.02 |
| Azure region and database resources | Provider-supported HA, readable replica/pooler capability, bounded exercise cost | M12.01 |
