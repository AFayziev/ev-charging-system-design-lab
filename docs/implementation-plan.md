# EV Charging Implementation Plan

## 1. Scope and tracking

This plan sequences implementation of the [architecture](architecture.md).
[Domain rules](domain.md) and [integration contracts](integrations.md) define
behavior. The [lab reference](lab.md) defines technical setup, execution profiles,
and fault scenarios for acceptance.
Milestones deliver complete application flows within the selected topology;
component responsibilities and domain policies remain consistent throughout implementation.

Task IDs are tracking references. Completed task IDs remain stable. Unchecked
tasks are incomplete. Tasks and subtasks are checked only when their full scope
has been reviewed, required verification has passed, and the changes are ready
to commit. Initial implementation or a successful build alone does not establish
completion. Milestone status is one of `Planned`, `In progress`, `Blocked`, or
`Done`. A blocked item records the specific dependency. `Done` requires every
required task and the milestone's
acceptance criteria.
Optional and hosted gates remain independently tracked and do not
block baseline local acceptance. Optional-profile gates apply when that profile
is enabled; hosted gates require their provider setup.
Independent follow-up tasks retain their IDs and specify their own dependencies
and acceptance gates; they do not block their original milestone.
Milestone status is recorded in the table below.

Task checkboxes and milestone status are updated in the same commit as the
implementation. Verification summaries belong in pull request descriptions
when applicable.

## 2. Suggested order and dependencies

The bootable topology, authentication, domain model, and seeded persistence form
the baseline. Subsequent user-facing tasks deliver frontend interactions,
gateway-routed API endpoints, persistence, required background processing, and
end-to-end verification together. Infrastructure integration accompanies its
first application consumer. Each flow includes authorization, bounded failure
handling, visible error states, and relevant resilience checks.
Shared integration contracts are introduced with their first consumer and reused
by later flows.

Driver-facing screens follow the approved [UI concept](mockups/driver-ui-concept.png).
User-facing flows support desktop and portrait smartphones, with readable layouts,
touch-friendly controls, accessible navigation, and no horizontal page scrolling.
End-to-end verification covers both desktop and phone viewports.

Entry dependencies identify the earliest usable slice of a milestone. Completion
requires its task dependencies and acceptance criteria. Existing checks count
toward acceptance; repetition is needed only when changes invalidate their results.

| ID | Milestone | Entry dependencies | Status |
|---|---|---|---|
| M00 | Repository and design baseline | None | Done |
| M01 | Bootable Aspire topology | M00 | Done |
| M02 | Gateway authentication and shared frontend shell | M01; frontend gate M01.13 | Done |
| M03 | Domain and seeded persistence baseline | M02 | Done |
| M04 | Station browsing and operator edits | M03 | Done |
| M05 | Reservations, expiry, and notifications | M04.01 | In progress |
| M06 | Charging start/stop and operator investigation | M05.02; cancellation races require M05.03 | In progress |
| M07 | Charging progress, history, and completion | M06.01; completion requires M06.02 | In progress |
| M08 | Live updates and cached views | M07.01 | Planned |
| M09 | Search and station discovery | M04.01, M05.01; availability requires M07.01 | Planned |
| M10 | Station media, session summaries, and diagnostics | M04.02, M05.01; summaries require M07.02 | Planned |
| M11 | Hosted R2 and Cloudflare CDN | M10.01 | Planned |
| M12 | Managed PostgreSQL pooling, replication, and failover | M04; charging failover requires M08 | Planned |
| M13 | Complete lab demonstrations and fault verification | M01–M12, M04.03; hosted email gate M05.07 | Planned |
| M14 | AmpTrail queued-provider integration | M06, M07 | Planned |

M04.01 is the first charging-domain feature: a visitor browses
stations and equipment from the seeded relational data. Notifications, live
updates, discovery, and media add independent consumers to established flows.
The hosted email, files/CDN, and managed-database exercises have independent
provider setup and acceptance gates.

## 3. Milestone tasks and acceptance

### M00 — Repository and design baseline

- [x] M00.01 Establish top-level `src`, `infra`, `tests`, `docs`, and `tools` directories.
- [x] M00.02 Add editor, Git attribute, and ignore configuration.
- [x] M00.03 Record system scope, architecture, domain vocabulary/policies,
  simulator contracts, and lab profiles.

Acceptance: the repository contains the standalone design references and the
monorepo baseline.

### M01 — Bootable Aspire topology

- [x] M01.01 Add the .NET solution, SDK/version configuration, central NuGet
  management, and pinned dependency/image versions compatible with the architecture.
- [x] M01.02 Add the Aspire C# AppHost, YARP gateway, API host, and independent
  charging, observation, search index, cache, media, and notification worker hosts using
  [settled host responsibilities](architecture.md#3-components-and-request-topology).
  Add stub API hosts for the WattHarbor and AmpTrail lab providers and add a
  local notification mail sink. Wire HTTP
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
- [x] M01.07 Add xUnit/Aspire testing foundations and Vitest,
  React Testing Library, and Playwright workflow harnesses.
- [x] M01.08 Select and pin the free local S3-compatible container; verify it can
  support the file operations specified in the architecture before adopting it.
- [x] M01.09 Wire all local baseline resources through Aspire: one application
  PostgreSQL server with logical read/write/direct connections, Keycloak and its
  dedicated database, MongoDB, Valkey, Elasticsearch, RabbitMQ, Kafka in KRaft mode, and local
  S3 storage. Configure credentials, volumes, endpoints, references, and readiness
  waits using integrations or custom container resources as appropriate. Provide
  local management UI access for every baseline store and broker; verify backend
  connectivity, both PostgreSQL servers, loopback access, and exclusion from
  deployment.
- [x] M01.10 Verify baseline HTTP routing with the existing topology/browser tests;
  verify dependency wiring with Aspire readiness and management-tool connectivity.
- [x] M01.11 Verify local startup/stop and restart with retained volumes; document
  prerequisites and local start/inspect/test/stop commands.

Acceptance:

- Documented AppHost commands start the platform and independent provider
  simulators, including the frontend, application stubs, and simulator API stubs.
- All required resources reach their expected healthy state within bounded waits,
  and management tools establish authenticated backend connectivity.
- The browser loads the SPA from the frontend host without traversing YARP;
  API requests pass through the gateway and are traceable across gateway/API instances.
- Selected-instance diagnostics prove ingress/gateway routing through logical
  services and runtime replica balancing.
- Startup/stop and retained-volume restart are reproducible, and telemetry reaches
  Aspire Dashboard.

Health checks remain workload-specific as functionality is added. Passing this
milestone establishes infrastructure availability and wiring; authentication,
durable business handling, and domain workflows have their own later acceptance
criteria.

[Aspire AppHost](https://aspire.dev/get-started/app-host/),
[Health checks](https://aspire.dev/fundamentals/health-checks/),
[Topology testing](https://aspire.dev/testing/write-your-first-test/)

#### Independent follow-up tasks

- [ ] M01.12 Optional profile gate, after M01.06: wire the
  [`observability` profile](lab.md#21-execution-profiles). Verify export routing,
  backend queries, custom dashboards, restart, and its
  [fault scenarios](lab.md#24-fault-scenarios).
- [x] M01.13 Before M02 frontend acceptance, after M01.03, M01.04, and M01.06:
  implement [frontend diagnostics](lab.md#19-frontend-diagnostics), startup
  configuration, gateway tunnel, and build integration. Verify disabled behavior,
  capture metadata, bounded export failure, and local builds without hosted credentials.
- [ ] M01.14 Optional hosted gate, after M01.13: verify the Sentry execution
  profile against a configured project using a built frontend,
  [diagnostic signals](lab.md#23-verification-tools-and-signals), and
  [fault scenarios](lab.md#24-fault-scenarios).

### M02 — Gateway authentication and shared frontend shell

- [x] M02.01 Configure the bootstrapped Keycloak instance with application realm,
  synthetic users, and the gateway's confidential OIDC client; verify its dedicated
  PostgreSQL database/pool configuration.
- [x] M02.02 Establish authentication persistence: session/key schema migrations,
  least-privilege runtime and migration credentials, and a dedicated long-lived
  Npgsql primary data source with bounded waits and pool metrics.
- [x] M02.03 Implement the shared PostgreSQL `ITicketStore`, protected token state,
  Data Protection key sharing, conventional cookie-handler renewal/removal, and
  library-managed token refresh/revocation. Introduce PostgreSQL Testcontainers
  tests for protected shared-session persistence and expiry.
- [x] M02.04 Implement the `/auth` routes with direct browser redirects to Keycloak
  and matching issuer/callback configuration. Include logout antiforgery protection,
  validated return URLs, and browser cookie attributes.
- [x] M02.05 Add antiforgery validation for browser API requests, browser
  header/cookie stripping, bearer forwarding, and API JWT validation.
  Enforce authenticated access and driver/operator role policies at the API.
  Keep machine routes outside browser session/token-substitution policies.
- [x] M02.06 Build the driver/operator navigation shell, session discovery,
  sign-in/sign-out states, and shared API/error handling through the gateway.
- [x] M02.07 Verify browser navigation to Keycloak, OIDC discovery and callbacks,
  gateway/API instance loss, concurrent refresh under the supported provider policy,
  logout, session-store outage,
  Keycloak outage, and browser authentication boundaries with real dependencies.

Acceptance: login and logout start at the gateway, with Keycloak-hosted login pages
and gateway callbacks. Both roles can sign in. A browser session survives
gateway-instance loss; JavaScript receives no OIDC tokens. Invalid
antiforgery requests and forged identity headers fail. Authentication and role
policies are enforced by the API, and session-store failure cannot grant anonymous
access.

#### Independent follow-up tasks

- [ ] M02.08 Optional, after M02.06 and an established frontend visual design:
  customize the Keycloak login theme to match the application's branding and
  verify authentication flows with the themed pages.
- [x] M02.09 After M02.05 and M03.03: deliver driver registration with required
  verified email, idempotent profile provisioning before browser sign-in, and
  local verification-email capture. Verify registration and repeated sign-in
  with real dependencies, retaining existing synthetic account identities.
- [ ] M02.10 After M02.09: deliver verified account-email changes and explicit
  synchronization to the stored driver profile.

### M03 — Domain and seeded persistence baseline

- [x] M03.01 Implement canonical identifiers, UTC/time-zone handling, state
  dimensions, measurement types, and tests of invariants used by delivered features.
- [x] M03.02 Add PostgreSQL migrations for equipment, assignments, reservations,
  sessions, operations/attempts, and file metadata; define boundaries
  between domain persistence and host-specific Wolverine message-storage schemas.
- [x] M03.03 Add synthetic equipment, driver, operator-assignment, and provider
  mappings linked to the M02 identity fixtures; provide reproducible fixture seeding
  and lab-data reset.
- [x] M03.04 Extend database roles with least-privilege domain access, read-only
  query credentials, and dedicated migration credentials.

Acceptance: charging entities, migrations, synthetic data, and scoped
database roles are available. Charging and authentication schemas have independent
owners and migration histories. Reset preserves authentication data.

### M04 — Station browsing and operator edits

- [x] M04.01 Deliver public station browsing: station list/detail API contracts,
  gateway routes, frontend catalog/detail screens, and loading, empty, missing,
  and failure states. Introduce the long-lived Npgsql read data source and
  no-tracking EF Core queries with bounded waits and pool telemetry. Verify the
  anonymous and authenticated browser-to-database flows, read-route permissions,
  session-discovery failure isolation, and database
  unavailability through the feature.
- [x] M04.02 Deliver assigned-operator station editing: edit forms, validated API
  commands, station-assignment authorization, and persisted audit records.
  Introduce the independent write data source and EF Core command context with
  bounded transaction waits. Return authoritative mutation results from the
  write route. Verify saved changes, driver denial, unassigned-operator denial,
  and visible write failures. Depends on M04.01.

Acceptance: visitors browse seeded equipment and an assigned operator
edits station content. Read and write routes use separate pools. Permission and
dependency failures are visible without reporting unsuccessful writes as saved.

#### Independent follow-up tasks

- [ ] M04.03 Deferred to final fault verification, after feature delivery:
  exercise browsing/editing during database interruption and gateway/API
  instance loss. Use the actual feature workload to verify read/write routing,
  aggregate client-pool budgets, saturation, bounded rejection, and recovery.
  Depends on M04.01 and M04.02; required for M13 acceptance.

### M05 — Reservations, expiry, and notifications

- [x] M05.01 Deliver reservation creation from station detail and a driver reservation
  list with booking confirmation, typed API contracts, and persisted bookings.
  Add ownership checks,
  UTC/time-zone display, primary-side booking validation, overlap constraints,
  public identifiers, and authoritative mutation responses. Verify creation,
  adjacent/competing bookings across API instances, cross-driver denial, and
  visible validation, conflict, and dependency failures through the application.
  Depends on M04.01.
- [x] M05.08 Add configurable reservation admission following the
  [domain policy](domain.md#5-reservations) and
  [lab experiment settings](lab.md#221-reservation-policy-experiments).
  Bind and validate typed options, enforce quotas and driver overlap atomically,
  and expose relevant limits to the booking form through typed API contracts.
  Verify boundaries, concurrent admission with quotas greater than one, and
  policy changes without altering accepted slots or deadlines.
  Arrival-deadline enforcement is delivered with M06.01; slot-end expiry remains
  in effect until charging start is available. Depends on M05.01.
  - [x] Bind configurable reservation limits to typed options and validate them at startup.
  - [x] Enforce past-start, duration, and advance-horizon rules through domain creation.
  - [x] Enforce per-driver quota and overlap atomically across replicas, including
    eligible overdue quota release.
  - [x] Capture policy values per operation and persist accepted arrival deadlines.
  - [x] Expose limits to the booking form and verify that policy changes affect
    new admissions without altering accepted slots or deadlines.
- [x] M05.02 Add durable messaging and automatic reservation expiry. Introduce
  RabbitMQ/Wolverine delivery and PostgreSQL inbox/outbox persistence for expiry
  work and scoped host-family storage/roles.
  Use Wolverine's provided relay, scheduling, and durable message recovery.
  Add persisted expiry scheduling, bounded consumers, current-state checks, and
  an overdue reservation sweep at startup and periodically. Schedule new bookings
  and sweep existing overdue active reservations. Remove the unused separate
  deadline record; reservation timestamps remain authoritative. Verify visible
  expiry, quota release, and one transition/audit despite duplicate processing or
  an admission race. Use a focused restart check to verify the application's
  durable scheduling integration rather than the library's recovery implementation.
  Depends on M05.01 and M05.08.
  - [x] Wire RabbitMQ/Wolverine and host-family PostgreSQL inbox/outbox storage,
    including storage provisioning with migration credentials.
  - [x] Integrate booking transactions and deliver reservation-created messages to
    ChargingWorker; verify atomic acceptance and eventual worker receipt.
  - [x] Add expiry scheduling, processing, and an overdue reservation sweep;
    verify admission capacity independently of expiry processing, audit
    uniqueness, and the durable scheduling integration through
    the reservation feature.
- [x] M05.03 Deliver reservation cancellation in the driver's reservation list
  with permitted transitions, atomic state/outbox changes, audit records,
  stable cancellation/expiry transition times, and immediate authoritative results.
  Verify repeated cancellation and expiry races. Depends on M05.01 and M05.02.
- [x] M05.04 Deliver reservation-created/cancelled/expired notifications through
  the notification worker and local mail sink. Add independent topic subscriptions/queues,
  persisted attempts, safe bounded retries, uncertainty handling, concurrency isolation,
  and inspectable failures under the [notification contract](integrations.md#4-notification-providers).
  Verify recipient/event mapping and failures
  without changing accepted reservations. Depends on M05.01–M05.03.
  - [x] Complete reservation lifecycle events and transactional publication paths.
  - [x] Add topic routing and an independent notification subscription.
  - [x] Add recipient data, notification records, and durable send jobs.
  - [x] Implement direct local mail-sink delivery and persisted attempts with safe failure handling.
  - [x] Review the complete flow and verify notification isolation and outcomes.
- [ ] M05.05 Exercise application outcomes during interrupted message processing:
  accepted bookings eventually expire, duplicate processing does not repeat
  transitions or notifications, and failed work does not block unrelated bookings
  or subscribers. Use selected broker/worker interruptions and existing diagnostics
  to verify these guarantees without reproducing messaging-library durability tests.
  Observe queue/outbox age and independently delivered subscriber outcomes.
  Depends on M05.01–M05.04.
- [ ] M05.06 Add three-node RabbitMQ quorum queues and verify leader loss and
  publisher/consumer recovery with reservation/notification workloads.
  Depends on M05.05.

Acceptance: a driver books, views, and cancels eligible slots. Configured admission
limits and overlap protection hold across replicas; due, eligible bookings release
quota despite delayed background processing, and deadlines survive process loss.
Notification failure leaves accepted reservations intact and remains separately inspectable.

#### Independent follow-up tasks

- [ ] M05.07 External email integration exercise, after M05.04: select a provider,
  implement its adapter and safety contract, and verify real delivery, outcome
  evidence, usage, and cleanup with test recipients.
  Depends on [provider selection and hosted setup](lab.md#18-notification-adapters).

### M06 — Charging start/stop and operator investigation

- [x] M06.01 Deliver driver start and operation-status interactions for
  WattHarbor, following the [domain start rules](domain.md#51-start-and-expiry)
  and [provider contract](integrations.md#1-provider-contracts). The interactive
  [start confirmation mockup](mockups/start-charging.html) records the UI states
  and responsive interactions.
  Depends on M05.01 and M05.02. The sub-items are sequenced as follows:
  - [x] M06.01.1 Driver start UI and acceptance/status API: deliver the reservation
    action with typed API responses and visible operation states; persist
    session/operation intent and outgoing work atomically. Enforce ownership,
    eligibility, connector exclusivity, and operation identity. Verify the UI/API
    flow, duplicate submissions, payload mismatches, competing starts, unauthorized
    access, and dependency failures. Retain pending status after navigation/reload,
    support responsive interactions, and distinguish acceptance from confirmation.
  - [x] M06.01.2 WattHarbor integration: implement persistent provider
    commands/sessions, command deduplication, and outcome queries.
    Add the authenticated client with identity, payload, and unit mapping
    and bounded timeouts. Verify matching results and retained provider state.
    Depends on M06.01.1.
  - [x] M06.01.3 Worker execution: consume accepted start work, recheck eligibility,
    persist attempts and start request limits, call the provider outside SQL transactions,
    and apply matching confirmed/rejected outcomes atomically. Record ambiguous
    outcomes as uncertain. Verify the confirmed/rejected driver flows and claim
    handling, including definitive access denial on the initial provider request.
    Keep unresolved-state wording accurate for the implemented behavior.
    Make the owner's status action perform a bounded provider lookup without
    resending the start; retain unresolved claims after missing results or failures.
    Depends on M06.01.2.
  - [x] M06.01.4 Uncertain start resolution, following
    [command safety](integrations.md#2-command-safety),
    [start deadlines](domain.md#51-start-and-expiry), and the
    [execution records](architecture.md#92-startstop-execution).
    Depends on M06.01.3. Sub-items are sequenced as follows:
    - [x] M06.01.4.1 Automatic outcome checks: reuse existing lookups and atomic
      resolution with bounded Wolverine scheduling. Manual checks remain available
      without restarting automatic work. Rate limiting is outside this slice.
      Show when checks are running and when automatic checking has ended.
      Verify lost-response resolution, missing results, outages, and repeated
      checks through the public integration; retain uncertainty and claims after
      missing results or later 401/403 responses. No query counters or lookup history.
    - [x] M06.01.4.2 Safe WattHarbor effect retries: reuse the operation, key, and
      saved payload with one retry owner and the existing persisted effect limit.
      Recheck eligibility before each attempt. Verify deadline enforcement,
      bounded attempts across redelivery, and no regression of resolved outcomes.
      Depends on M06.01.4.1.
  - [x] M06.01.5 No-show expiry: switch automatic expiry to accepted arrival
    deadlines, resolve unexecuted queued starts, and update booking explanations.
    Verify pre-attempt expiry and retained claims for
    unresolved authorized attempts through the application. Verify existing effects
    can still be reconciled after the deadline without authorizing another start.
    Depends on M06.01.4.
- [x] M06.02 Deliver driver stop with the same operation tracking and recovery;
  display confirmed stop separately from pending final usage. Verify rejected
  and uncertain stops, overruns, and recovery after worker loss.
  Depends on M06.01. Sub-items are sequenced as follows:
  - [x] M06.02.1 WattHarbor stop contract: add an idempotent stop command,
    retained command/session outcomes, and an ended session state to the provider
    and its documented contract. Stopping an already ended session is applied;
    an unknown session is a definitive rejection. Verify replays, concurrent and
    competing stops, key reuse, response loss, and connector release.
  - [x] M06.02.2 Stop acceptance: make operation limits and guards kind-neutral,
    accept a driver stop for an active session with one live stop per session,
    and expose typed stop status. Verify ownership, idempotency, conflicts, and
    rejected stops returning the session to active.
    Depends on M06.02.1.
  - [x] M06.02.3 Stop execution and recovery: execute stops with numbered attempts,
    automatic outcome checks, and same-key resends, keeping a confirmed stop in
    stopping until final usage exists. Verify rejected, uncertain, and redelivered
    stops and recovery after worker interruption.
    Depends on M06.02.2.
  - [x] M06.02.4 Driver stop UI and end-to-end flow: show stop progress, unresolved
    stops, and confirmed stop with pending final usage. An unresolved stop offers
    no driver status action; it awaits automatic checks and operator investigation.
    Verify overruns blocking later starts through the application.
    Depends on M06.02.3.
- [ ] M06.04 Deliver recovery after exhausted automatic checks and
  assigned-operator session/attempt investigation with audited reconciliation
  requests, including unresolved outcomes. Depends on M04.02 and M06.01–M06.02.
  Sub-items are sequenced as follows:
  - [ ] M06.04.1 Automatic recovery sweep: give an unresolved operation whose
    checking window ended (for example, while the worker was down or the provider
    unavailable) a fresh bounded batch of checks, through one re-arm operation on
    the operation with a bounded number of rounds. Run it periodically in the
    worker, like reservation expiry. Document the bound and interval in the lab
    parameters. Resends stay limited by the request limit. Verify recovery after
    a worker outage longer than the window, bounded rounds, and no change to
    settled operations.
  - [ ] M06.04.2 Operator investigation: expose execution attempts, provider
    identifiers, outcomes, failure summaries, checking state, and exhausted
    automatic work to assigned operators. Make the unresolved operation's public
    reference available in the driver view and support lookup by that reference.
    Verify assignment denial and retained uncertainty when evidence is absent.
  - [ ] M06.04.3 Audited reconciliation requests: operator requests re-arm the
    checks through the durable worker path with bounded admission, rather than
    directly resending effects or releasing claims without evidence. Verify
    audited and repeated requests and provider unavailability.
    Depends on M06.04.1 and M06.04.2.
- [x] M06.05 Exercise the WattHarbor flow through crash after external effect,
  provider restart, rate limiting, and circuit opening, including attempts left
  unfinished by an interrupted request. Verify no SQL transaction spans an
  external call and uncertain commits resolve by operation identity.
  Depends on M06.01–M06.02.
- [ ] M06.06 Verify cancellation/start races, queued-start expiry, retained
  uncertain claims after expiry, session exclusivity, and effect limits across
  worker retries and redelivery. Depends on M05.03 and M06.01–M06.02.

Acceptance: driver start/stop and operator investigation work through the gateway,
durable workers, and the synchronous provider. Uncertain effects remain visible and
recoverable without authorizing conflicting starts. Final completion depends on M07.

### M07 — Charging progress, history, and completion

- [ ] M07.01 Deliver timestamped driver progress from charging-provider
  observations. The provider is a black box that honors the documented contract;
  the work is on the platform side. Depends on M06.01. Sub-items are sequenced as
  follows:
  - [x] M07.01.1 Observation contract and ingestion boundary: document the
    WattHarbor observation contract (source event identity, occurrence time as
    the only ordering field, state, power, cumulative energy, callback and
    snapshot shapes, retry and acknowledgement rules). Add the authenticated gateway-routed callback
    endpoint, payload validation, an adapter that normalizes provider payloads to
    canonical units, a versioned envelope, and a durable Kafka append partitioned
    by provider and connector, acknowledged only after confirmation. Verify with
    contract-shaped payloads: authentication denial, invalid payloads, no
    acknowledgement when the append fails, duplicate delivery, and normalization.
  - [ ] M07.01.2 WattHarbor contract conformance: the simulator emits timestamped
    observations for active sessions that survive restart, delivers callbacks with
    bounded same-identity retry, and returns a snapshot carrying the same identity
    and occurrence time. It receives its callback target and credentials through a
    simulation control call, so the platform needs no knowledge of its internals.
    Add duplicate, older-timestamp, and interrupted-telemetry faults. Verify with
    provider contract tests and the flow through the real ingestion boundary.
    Depends on M07.01.1.
  - [ ] M07.01.3 Progress view and query: independent observation-worker consumer
    group that deduplicates by source identity, applies time-conditional
    updates to MongoDB history and latest views, and checkpoints after the durable
    write. Serve a progress query from the latest view. Verify duplicates,
    out-of-order events, and catch-up after worker restart.
    Depends on M07.01.1.
  - [ ] M07.01.4 Driver charging session page: show energy, power, elapsed time,
    the power-over-time chart, source-time freshness (`Last reported charging`
    when stale), bounded polling, and the stopping state. Verify end to end with
    the simulator on desktop and phone viewports.
    Depends on M07.01.2 and M07.01.3.
  - [ ] M07.01.5 Session evidence, stalled detection, and recovery polling:
    independent consumer group committing deduplication, state, and outbox in one
    relational transaction. Stalled-session detection submits durable
    reconciliation work to the charging worker, which polls the provider snapshot
    and submits it through the gateway with service credentials. Verify that
    interrupted telemetry makes the view stale rather than stopped, that polling
    restores freshness, and that replay does not refresh timestamps.
    Depends on M07.01.2 and M07.01.3.
- [ ] M07.02 Deliver completed-session final usage and completion notifications.
  Match provider/session evidence, validate cumulative usage, conditionally
  freeze completion facts, and commit outgoing completion events with SQL state.
  Verify stop-before-usage, early terminal evidence, and late contradictions.
  Depends on M06.02, M07.01, and M05.04 for notification delivery.
- [ ] M07.03 Deliver assigned-operator observation history and anomaly inspection
  alongside SQL attempts and provider evidence. Verify authorization, timestamp
  skew, duplicate/out-of-order observations, and missing/decreasing energy in
  these views. Depends on M06.04 and M07.01.
- [ ] M07.04 Exercise progress/completion through stream outage and ambiguous
  append, observation-worker loss/stall, MongoDB outage, and offset recovery.
  Add isolated observation-view replay/rebuild and verify it preserves source
  times without sending charging commands or notifications.
  Depends on M07.01 and M07.02.

Acceptance: a driver sees session progress and completes charging with matching,
immutable final usage. Accepted observations survive document-store interruption;
replay rebuilds views without repeating effects or making old evidence fresh.

### M08 — Live updates and cached views

- [ ] M08.01 Deliver live progress and operation updates to the browser. Introduce
  authenticated SignalR endpoints, independent event subscriptions, Valkey
  backplane fan-out, and gateway affinity alongside TanStack Query integration,
  version checks, bounded reconnect, snapshot recovery, and polling fallback.
  Display connection health separately from observation freshness and retain
  pending operations. Verify cross-instance delivery and reconnect through the UI.
  Depends on M07.01.
- [ ] M08.02 Serve catalog/progress query views through Valkey with expiry and
  version-conditional writes. Introduce transactional catalog-change events,
  independent cache-worker updates/invalidation, bounded SQL/MongoDB fallback,
  stampede protection, warming, and cache-hit/fallback metrics. Verify operator
  edits and charging observations reach the browser without stale updates
  overwriting newer versions. Depends on M04.02, M05.01, and M07.01.
- [ ] M08.03 Exercise feature workloads through cache flush/outage, cache-worker
  loss/recovery, backplane outage, missed updates, API loss, and affinity-target
  loss. Verify bounded fallback and independent recovery from search/observations.
  Depends on M08.01 and M08.02.

Acceptance: two API instances deliver updates through the backplane. Reconnecting
clients recover current snapshots. Cache loss preserves authoritative facts and
bounded fallback protects the backing stores.

### M09 — Search and station discovery

- [ ] M09.01 Deliver driver text, distance, and capability search/filtering.
  Introduce Elasticsearch mappings, initial indexing, and version-aware catalog
  event consumers in the search index worker alongside API queries and frontend
  controls. Connect transactional catalog-change events and independent subscriptions
  and checkpoints. Verify discovery and operator-edit visibility.
  Depends on M04.01 and M05.01; edit coverage requires M04.02.
- [ ] M09.02 Enrich discovery with observation-driven advisory availability and
  freshness labels. Verify search results cannot bypass primary-side booking
  eligibility. Depends on M07.01 and M09.01.
- [ ] M09.03 Exercise discovery during delayed/duplicate updates, search outage,
  and search index worker loss. Add bounded SQL lookup, visible degraded results,
  replacement-index rebuild/validation/switchover, and freshness metrics; verify
  recovery independently of cache and observations. Depends on M09.01 and M09.02.

Acceptance: drivers discover equipment through an independent index with visible
freshness limits. Rebuilds publish validated replacements and search failure
preserves unrelated commands for known equipment.

### M10 — Station media, session summaries, and diagnostics

- [ ] M10.01 Deliver assigned-operator upload/publication and driver station
  media. Introduce the S3 adapter, private/public buckets, CORS, direct presigned
  browser transfers, ownership/state records, HEAD verification, transfer limits,
  media-worker processing, SkiaSharp variants, immutable publication keys, and
  transactional publication events. Verify the storage contract through upload,
  download, copy, and deletion, plus placeholders and retained old publications
  during failures. Depends on M04.02 and M05.01.
- [ ] M10.02 Deliver authorized driver session-summary downloads. Generate
  private PDFsharp documents from frozen completion facts in the media worker,
  with deterministic regeneration and visible pending/failure states.
  Depends on M07.02 and M10.01.
- [ ] M10.03 Deliver assigned-operator diagnostic-bundle downloads using sanitized
  ZIP/JSON evidence from SQL attempts, MongoDB observations, and provider records.
  Verify private access and secret exclusion. Depends on M07.03 and M10.01.
- [ ] M10.04 Exercise media/document flows through upload-notice loss, worker
  crash after writing variants, storage outage, partial writes, expired/reused
  upload URLs, cross-user access, and summary failure after session completion.
  Add missing-notice reconciliation and abandoned-upload/orphan cleanup with
  observable queue/recovery outcomes. Verify media failures remain independent
  of notification delivery and charging execution. Depends on M10.01–M10.03.

Acceptance: station media appears only after publication and published bytes
cannot be overwritten through original upload URLs. Summaries reproduce frozen
usage; diagnostic downloads expose authorized evidence without secrets. File
failure leaves charging completion intact.

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

### M12 — Managed PostgreSQL pooling, replication, and failover

- [ ] M12.01 Add the Azure database profile with primary, provider-managed HA
  standby, separate readable replica, managed pooler routes, and configuration/secrets.
  Preserve the local connection contracts without application routing changes.
- [ ] M12.02 Run migrations and synthetic data setup using the direct route.
  Introduce replica-lag detection and bounded primary fallback in feature queries
  when activating the readable replica; expose stale/degraded results in the UI.
  Verify primary, replica, identity, and gateway-session pool budgets and read-only
  permissions. Depends on M12.01 and M04.
- [ ] M12.03 Run active charging/observation workloads during provider-supported
  failover and record endpoint/pool recovery and transaction interruption.
  Depends on M12.02 and M08.
- [ ] M12.04 Verify uncertain-commit lookup, operation resubmission, durable work
  recovery, browser-session continuity, authoritative mutation-result display,
  and replica lag/fallback during the exercise.
- [ ] M12.05 Record any observed lost commits, recovered outcomes, bounded waits,
  and measured recovery time; automate teardown and record remaining-resource costs.
- [ ] M12.06 Verify transaction-pooling compatibility of EF Core and durable
  messaging, direct routes for session-affine coordination, server-pool budgets and
  saturation, replica read-only behavior, lag detection, replica interruption, and
  bounded primary fallback. Depends on M12.02 and M05.01; this gate can run
  ahead of the full M08-dependent failover workflows.

Acceptance: the provider performs database promotion; the application handles
interrupted transactions and connections without inventing commit outcomes or
duplicating operations. The same application binaries use configured managed
routes; transaction pooling, bounded fallback, and server-pool contention are
demonstrated. HA standby and query replica roles are separately visible.
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

### M14 — AmpTrail queued-provider integration

Deferred until the synchronous provider flows are complete and hardened. The
AmpTrail lab provider remains a stub host until then.

- [ ] M14.01 Deliver start/stop for AmpTrail with its persistent
  lab provider, client, acknowledgement/query behavior, and correlation recovery.
  Verify lost acknowledgements, delayed effects, and interrupted requests without
  blind resubmission. Add AmpTrail coverage to charging progress.
  Depends on M06.02 and M07.01.
- [ ] M14.02 Exercise both provider flows through crash after external effect,
  provider restart, rate limiting, circuit opening, and per-provider concurrency
  isolation. Depends on M06.05 and M14.01.

Acceptance: driver start/stop, progress, and operator investigation work against
both providers. Uncertain AmpTrail effects remain visible and recoverable without
blind resubmission.

## 4. Component and verification coverage

| Component / boundary | Implementation | Principal verification |
|---|---|---|
| Aspire, process hosts, OCI containers | M01; resource wiring in each milestone | M01 startup/restart; M03 fixtures/reset; M13 clean-checkout demonstrations |
| Ingress, gateway, and runtime replicas | M01, M02, M08 | Logical destination health, replica selection/loss, affinity, authentication continuity |
| React, Mantine, TanStack Query | M01, M02, M04–M12 | Role workflows, stale/pending states, reconnect, private access |
| Keycloak, BFF, shared ticket/key state | M02 | Browser boundaries, refresh/logout races, identity/session-store outage |
| PostgreSQL, EF Core, Npgsql | M02, M03; runtime feature access M04–M07, M10 | Route permissions, client pooling, conflicts, ordering, commits, ownership, completion |
| Managed read replica and PgBouncer | M12; application routes introduced in M04 | Lag, read-only routes, server-pool saturation, reconnect/failover |
| RabbitMQ jobs/topics, Wolverine inbox/outbox | Reservation expiry M05.02; independent subscribers M05.04 onward | Publish/ack crash windows, redelivery, independent subscribers |
| Durable scheduler / reconciliation | Reservation expiry M05.02; charging M06, files M10 | Restart, deadlines, exhaustion, expiry races, missing notices |
| Provider simulators, HTTP resilience | M06, M07; queued provider M14 | Lost responses, idempotency differences, rate limits, physical evidence |
| Notification mail sink/adapters and worker | Reservation notifications M05.04; completion M07.02; external exercise M05.07 | Local capture, real email delivery, ambiguity, independent retry |
| Kafka and observation consumers | M07 | Durable acceptance, lag, offset recovery, replay |
| MongoDB | M07, M10 | Latest/history ordering, outage catch-up, diagnostic queries |
| Valkey and SignalR backplane | M08 | Cache flush, bounded fallback, fan-out loss/recovery |
| Elasticsearch | M09 | Index freshness, outage, validated rebuild |
| Independent cache and search index workers | M08, M09 | Separate lag/checkpoint recovery, worker loss, warming/rebuild without session effects |
| S3 adapter / local container | Compatibility selection M01.08; station-media flow M10.01 | Transfer authorization, partial writes, cleanup, immutable publication |
| SkiaSharp, PDFsharp, ZIP/JSON | M10 | Processing recovery, frozen summary facts, diagnostic sanitization |
| R2 and Cloudflare CDN | M11 | Real signing, cache hits/misses, origin deletion, purge |
| Azure managed PostgreSQL | M12 | Provider failover, pool/session recovery, uncertain commits |
| OpenTelemetry and Aspire Dashboard | M01.06 onward; M13 views | Correlated evidence, resource health, bounded export during outage |
| Optional observability profile | M01.12; M13 profile scenarios | Telemetry routing, queries/custom dashboards, bounded buffering/drop during outage |
| Frontend diagnostics / Sentry | M01.13; hosted gate M01.14 | [Signals](lab.md#23-verification-tools-and-signals), [fault scenarios](lab.md#24-fault-scenarios) |
| xUnit and Aspire testing | M01.07 onward | Domain, contract, topology, resilience verification |
| Testcontainers | M02.03 onward | Isolated real-dependency integration tests within functional milestones |
| Vitest, React Testing Library, Playwright | M01 onward | UI semantics and authenticated browser workflows |
| k6 and Toxiproxy | M13; available for earlier gates | Saturation and network-fault evidence |

Scenario definitions remain in [the lab reference](lab.md#24-fault-scenarios).
Milestone gates verify relevant faults as their boundary becomes usable; M13
closes coverage gaps and repeats the integrated demonstrations.

## 5. Pending implementation selections

| Selection | Constraint / dependency | Resolution task |
|---|---|---|
| Database retry ownership | Review bounded EF retries for API read routes separately from transactional writes and message retries; write retries require transaction replay and uncertain-commit handling | Pending review |
| API rate limiting | Deferred separately from M06.01.4.1; enforce limits at the gateway or through instance middleware backed by a central store (for example, Redis), consistently across replicas | Pending design and implementation |
| External email provider and test recipients | Provider selection, sender onboarding, scoped credentials, and real-delivery setup | M05.07 |
| Hosted public asset hostname | Subdomain in the existing Cloudflare account; separate public assets and private transfer endpoints | M11.02 |
| Azure region and database resources | Provider-supported HA, readable replica/pooler capability, bounded exercise cost | M12.01 |
