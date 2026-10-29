# EV Charging Implementation Plan

## 1. Scope and tracking

This plan sequences implementation of the [architecture](architecture.md).
[Domain rules](domain.md) and [integration contracts](integrations.md) define
behavior. The [lab reference](lab.md) defines technical setup, execution profiles,
and fault scenarios for acceptance.
Milestones deliver complete application flows within the selected topology;
component responsibilities and domain policies remain consistent throughout implementation.

Task IDs are stable tracking references; unchecked tasks are incomplete. A task
is checked when its scope is implemented and its tests pass. Milestone status is
`Planned`, `In progress`, `Blocked` (recording the dependency), or `Done` (every
required task and the acceptance criteria). Optional, hosted, and independent
follow-up tasks keep their own dependencies and do not block their milestone.
Task checkboxes and milestone status are updated in the same commit as the
implementation.

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
A small set of browser journeys covers the phone viewport.

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
| M05 | Reservations, expiry, and notifications | M04.01 | Done |
| M06 | Charging start/stop and operator investigation | M05.02; cancellation races require M05.03 | In progress |
| M07 | Charging progress, history, and completion | M06.01; completion requires M06.02 | Done |
| M08 | Live updates and cached views | M07.01 | Planned |
| M09 | Search and station discovery | M04.01, M05.01; availability requires M06.01 | Done |
| M10 | Station media and session summaries | M04.02, M05.01; summaries require M07.02 | Planned |
| M11 | Hosted R2 and Cloudflare CDN | M10.01 | Planned |
| M12 | Managed PostgreSQL pooling, replication, and failover | M04; charging failover requires M08 | Planned |
| M15 | API rate limiting | M08 | Planned |
| M13 | Lab demonstrations and fault tooling | M01–M12, M15; hosted email gate M05.07 | Planned |
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
  [`observability` profile](lab.md#21-execution-profiles) with custom dashboards
  querying each backend.
- [x] M01.13 Before M02 frontend acceptance, after M01.03, M01.04, and M01.06:
  implement [frontend diagnostics](lab.md#19-frontend-diagnostics), startup
  configuration, gateway tunnel, and build integration. Verify disabled behavior,
  capture metadata, bounded export failure, and local builds without hosted credentials.
- [x] M01.14 Optional hosted gate, after M01.13: verify that a built frontend
  reports readable stacks, release, and grouped issues to a configured Sentry project.

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
  customize the Keycloak login theme to match the application's branding.
- [x] M02.09 After M02.05 and M03.03: deliver driver registration with required
  verified email, idempotent profile provisioning before browser sign-in, and
  local verification-email capture. Verify registration and repeated sign-in
  with real dependencies, retaining existing synthetic account identities.

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
- [x] M05.05 Exercise a broker outage with reservation workloads: bookings,
  cancellations, and expiry commit while the broker is down, and each pending
  event reaches its subscribers once after recovery. Worker restart, duplicate
  expiry, and notification isolation are covered by M05.02–M05.04. Verified
  manually against the running stack, with the broker data kept and with a fresh
  empty broker. No automated test: the outage could not be reproduced reliably
  in E2E.
  Depends on M05.01–M05.04.

Acceptance: a driver books, views, and cancels eligible slots. Configured admission
limits and overlap protection hold across replicas; due, eligible bookings release
quota despite delayed background processing, and deadlines survive process loss.
Notification failure leaves accepted reservations intact and remains separately inspectable.

#### Independent follow-up tasks

- [x] M05.07 External email integration exercise, after M05.04: implement the
  Amazon SES adapter and safety contract, and verify real delivery, outcome
  evidence, usage, and cleanup with test recipients.
  Follows the [SES selection and setup](lab.md#18-notification-adapters).

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
- [ ] M06.04 Deliver continued automatic checks and assigned-operator
  session/attempt investigation with audited reconciliation requests, including
  unresolved outcomes. Depends on M04.02 and M06.01–M06.02.
  Sub-items are sequenced as follows:
  - [x] M06.04.1 Continued automatic checks: a recurring scan in the worker gives
    every operation still unresolved after its checking window (for example,
    while the worker was down or the provider unavailable) another check at a
    slower cadence until evidence settles it. Document the cadence in the lab
    parameters. Resends stay limited by the request limit. Verify recovery after
    a worker outage longer than the window, one check per concurrent scan, and
    no change to settled operations.
  - [ ] M06.04.2 Operator investigation: expose execution attempts, provider
    identifiers, outcomes, failure summaries, and checking state to assigned
    operators alongside the session's observation history, and list operations
    that continued checks have not settled. Make the unresolved operation's public
    reference available in the driver view and support lookup by that reference. Verify assignment denial and retained
    uncertainty when evidence is absent.
  - [ ] M06.04.3 Audited reconciliation requests: operator requests trigger an
    immediate check through the durable worker path with bounded admission,
    rather than directly resending effects or releasing claims without evidence.
    Verify audited and repeated requests and provider unavailability.
    Depends on M06.04.1 and M06.04.2.
- [x] M06.05 Exercise the WattHarbor flow through crash after external effect,
  provider restart, rate limiting, and circuit opening, including attempts left
  unfinished by an interrupted request. Verify no SQL transaction spans an
  external call and uncertain commits resolve by operation identity.
  Depends on M06.01–M06.02.
- [x] M06.06 Verify cancellation/start races, queued-start expiry, retained
  uncertain claims after expiry, session exclusivity, and effect limits across
  worker retries and redelivery. Depends on M05.03 and M06.01–M06.02.

Acceptance: driver start/stop and operator investigation work through the gateway,
durable workers, and the synchronous provider. Uncertain effects remain visible and
recoverable without authorizing conflicting starts. Final completion depends on M07.

### M07 — Charging progress, history, and completion

- [x] M07.01 Deliver timestamped driver progress from charging-provider
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
  - [x] M07.01.2 WattHarbor contract conformance: the simulator emits timestamped
    observations for active sessions that survive restart, delivers callbacks with
    bounded same-identity retry, and returns a snapshot carrying the same identity
    and occurrence time. It receives its callback target and credentials through a
    simulation control call, so the platform needs no knowledge of its internals.
    Add duplicate, older-timestamp, and interrupted-telemetry faults. Verify with
    provider contract tests and the flow through the real ingestion boundary.
    Depends on M07.01.1.
  - [x] M07.01.3 Progress view and query: independent observation-worker consumer
    group that deduplicates by source identity, applies time-conditional
    updates to MongoDB history and latest views, and checkpoints after the durable
    write. Serve a progress query from the latest view. Verify duplicates,
    out-of-order events, and catch-up after worker restart.
    Depends on M07.01.1.
  - [x] M07.01.4 Driver charging session page: show energy, power, elapsed time,
    the power-over-time chart, source-time freshness (`Last reported charging`
    when stale), bounded polling, and the stopping state. Polling and the chart
    stay incremental: a request does not return the whole observation history,
    and an unchanged session costs a small response. Decide how a session with
    no reading yet is shown, since the progress query reports it as not stale.
    Verify end to end with
    the simulator on desktop and phone viewports.
    Depends on M07.01.2 and M07.01.3.
  - [x] M07.01.5 Session evidence consumer: independent consumer group committing
    deduplication, state, and outbox in one relational transaction. Verify that
    replay and duplicate delivery do not refresh timestamps or repeat transitions.
    Depends on M07.01.2 and M07.01.3.
- [x] M07.02 Deliver completed-session final usage and completion notifications.
  Match provider/session evidence, conditionally freeze completion facts, and
  commit outgoing completion events with SQL state.
  Verify stop-before-usage, early terminal evidence, and late contradictions.
  Depends on M06.02, M07.01.5, and M05.04 for notification delivery.
  Sub-items are sequenced as follows:
  - [x] M07.02.1 Completion transition: complete an active or stopping session
    from matching terminal evidence with a final reading, freezing its final
    energy and end time and committing the completion event with the state.
    Verify duplicate and replayed evidence, readings that are not terminal,
    and contradictions after completion.
  - [x] M07.02.2 Completed session page: show the frozen final usage and keep
    polling until completion. Verify stop-before-usage and early terminal
    evidence through the application. Depends on M07.02.1.
  - [x] M07.02.3 Completion notification: prepare one email from the completion
    event and deliver it through the notification worker. Verify the frozen
    content and redelivery. Depends on M07.02.1 and M05.04.
- [x] M07.03 Deliver stalled-session detection and recovery polling:
  the charging worker finds active or stopping sessions whose newest reading is
  older than the freshness limit, reads the provider snapshot with backoff, and
  appends it to the observation stream. Verify that interrupted telemetry makes
  the view stale rather than stopped, that polling restores freshness and
  supplies missing final usage for a `Stopping` session, and that replay does
  not refresh timestamps. Depends on M07.01.5 and M07.02.
- [x] M07.06 Deliver provider-initiated session end: a WattHarbor connector can
  carry an energy limit, and the provider ends a session on it once the vehicle
  is full, emitting the final reading and freeing the connector. Verify that the
  platform completes the session from that reading without a stop request, that
  lost callbacks are recovered through the snapshot read, that a stop still
  pending settles, and that a later stop is applied without moving the end time.
  Depends on M07.02.

Acceptance: a driver sees session progress and completes charging with matching,
immutable final usage. Accepted observations survive document-store interruption;
replayed evidence neither repeats effects nor makes old evidence fresh.

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
  warming, and cache-hit/fallback metrics. Verify operator edits and charging
  observations reach the browser without stale updates overwriting newer
  versions, and bounded fallback during a cache outage.
  Depends on M04.02, M05.01, and M07.01.
- [ ] M08.03 Cache search read paths in Valkey, read and filled by the search
  service itself: the filter options, which only change when stations are
  indexed, and optionally popular search pages. Use expiry first, with the index
  as bounded fallback during a cache outage and hit/fallback metrics. Verify
  options are served from the cache across search replicas and that an outage
  falls back to the index. Depends on M08.02 and M09.01.3.

Acceptance: two API instances deliver updates through the backplane. Reconnecting
clients recover current snapshots. Cache loss preserves authoritative facts and
bounded fallback protects the backing stores.

### M09 — Search and station discovery

- [x] M09.01 Deliver driver text, distance, and capability search/filtering.
  Introduce Elasticsearch mappings, initial indexing, and version-aware catalog
  event consumers in the search index worker alongside API queries and frontend
  controls. Connect transactional catalog-change events and independent subscriptions
  and checkpoints. Verify discovery and operator-edit visibility. Final
  verification: with the index down, search returns its typed error and the
  gateway error when the service is down, while browsing, reservations, and
  operator edits keep working, and edits made during the outage appear once the
  index recovers.
  Depends on M04.01 and M05.01; edit coverage requires M04.02.
  Sub-items are sequenced as follows:
  - [x] M09.01.1 Station events and search projection: publish a thin station-changed
    event through the outbox, grouped by station. The search worker reads the
    committed station from the primary and indexes it, applying one station's
    events in order, with unavailable dependencies retried indefinitely. Add the
    mapping and a read-only worker role. Verify redelivered and back-to-back
    events. Depends on M04.02.
  - [x] M09.01.2 Initial indexing and rebuild: bulk-load into a versioned index
    behind an alias, then swap. Verify rebuild from empty and a rebuild racing
    live edits. Depends on M09.01.1.
  - [x] M09.01.3 Search API as its own service behind the gateway, anonymous like
    station browsing, over a read-only role on the station index: text, connector
    type, power supply, minimum power, and optional distance sorting, with an opaque
    cursor and a typed unavailable error. Browsing stays on SQL. Verify filters,
    paging, and the role's denials. Depends on M09.01.2.
  - [x] M09.01.4 Frontend discovery: a minimal catalog search row, a debounced text
    field with a minimum length, a "Near me" toggle (10 km radius), and exact
    filters behind a filter icon (their choices served from the index), with URL
    state, no-matches and search-unavailable states with a fallback to the SQL
    catalog. Verify denied geolocation.
    Depends on M09.01.3.
  - [x] M09.01.5 Edit visibility: verify through the browser that an operator edit
    appears in search. Depends on M09.01.3.
- [x] M09.02 Show advisory connector availability in discovery, derived from
  primary-side booking state (sessions and reservations) at request time and read
  separately from search results, never from provider observations or the index.
  Station level shows how many connectors are available. Verify search results
  and availability hints cannot bypass primary-side booking eligibility, and that
  a failure of either side leaves the other working.
  Depends on M05.01, M06.01 and M09.01.

Acceptance: drivers discover equipment through an independent index. Availability
hints are advisory and booking still enforces eligibility. Search failure is
visible and preserves unrelated commands for known equipment.

### M10 — Station media and session summaries

- [ ] M10.01 Deliver the station photo gallery: assigned-operator uploads and
  gallery management, and published photos for visitors and drivers, following
  the [gallery rules](domain.md#9-files), [storage design](architecture.md#10-object-storage-and-cdn),
  and [media limits](lab.md#222-station-media-limits).
  Depends on M04.02 and M05.01. Sub-items are sequenced as follows:
  - [x] M10.01.1 Storage adapter and buckets: an S3 adapter for signed upload and
    download URLs, object verification and bounded reads, immutable publication, and
    deletion; one-shot provisioning
    of private and public buckets, public read on the public bucket, and browser
    CORS for the application origin; per-host storage credentials limited to
    their operations. Verify the storage contract through upload, download,
    publication, and deletion against the local container.
  - [x] M10.01.2 Gallery records and upload start: gallery photos with public
    identifiers, kind, position, and upload and processing state. An assigned
    operator starts an upload within the limits and receives a short-lived
    signed URL for a private upload. The completion notice moves the photo to
    processing and queues the work; repeated notices have no further effect.
    Expose the limits to the editor through typed contracts. Verify limits
    under concurrently started uploads, unassigned-operator and driver
    denial, and rejected type and size.
  - [x] M10.01.3 Photo processing and publication: Wolverine messaging and a scoped
    database role in the media worker; bounded SkiaSharp decoding of the upload
    into WebP variants written under immutable per-photo keys in the public
    bucket, with rejected uploads failing with a reason; a publication
    transaction that publishes the photo, takes over the place of the photo it
    replaces, and publishes the station-changed event; a check scheduled at each
    upload's deadline for uploads that never report in. Verify undecodable,
    oversized, and mismatched files, redelivery and retries converging on one
    publication, a reused upload URL leaving published bytes unchanged, and the
    previous photo remaining published while a replacement fails. Depends on
    M10.01.2.
  - [x] M10.01.4 Gallery management: reorder, replace, and remove with
    stale-version rejection on reorder and upload start, audit records, and
    station-changed events. Removal covers photos whose uploads are pending or
    failed, and deletes their stored objects. Verify concurrent edits and that
    removal hides the photo immediately. Depends on M10.01.3.
  - [ ] M10.01.5 Driver and operator UI: station list, detail, and search contracts
    carry published variant URLs from the configured public base URL; the search
    document carries the first photo. Catalog cards and search results show the
    first photo, and station detail shows the gallery, with placeholders for
    stations without photos and items that fail to load. The operator editor
    uploads with progress and shows processing and failed states, reordering,
    replacement, and removal. Verify on desktop and phone viewports, and that
    browsing, booking, and charging keep working while storage is unavailable.
    Depends on M10.01.4.
- [ ] M10.02 Deliver authorized driver session-summary downloads. Generate
  private documents from frozen completion facts in the media worker,
  with deterministic regeneration and visible pending/failure states.
  Summary content, generation trigger, and delivery are scoped before the task
  starts. Depends on M07.02 and M10.01.1.
- [ ] M10.03 Add one short instructional video per station to the gallery: upload
  within the video limits, verification of container and duration, publication
  with a poster image, and playback on station detail. Depends on M10.01 and
  the video processing selection.

Acceptance: an assigned operator maintains a station gallery within its limits,
and visitors see only published media. Published bytes cannot be overwritten
through upload URLs, and failed uploads or processing leave the previous
publication in place. Summaries reproduce frozen usage. File failure leaves
charging completion intact.

### M11 — Hosted R2 and Cloudflare CDN

- [ ] M11.01 Add the hosted files profile: R2 credentials/endpoints, private and
  public buckets through environment configuration, and the public bucket on a
  configured subdomain of the existing Cloudflare-managed domain, with private
  buckets off public delivery routes. Run adapter contract tests against R2,
  including browser CORS, presigned transfers, metadata/checksum handling,
  publication write, and deletion.
- [ ] M11.02 Publish versioned frontend assets and media through CDN cache rules,
  with the entry page referencing the matching asset build, and add the CDN purge
  adapter to the publication/removal workflow. Verify warm versus cold requests
  after origin deletion, purge, and unavailable-media placeholders. Add repeatable
  profile setup/cleanup without changing unrelated DNS records or persisting
  credentials. Depends on M11.01.

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
  failover. Verify uncertain-commit lookup, durable work recovery,
  browser-session continuity, and authoritative mutation results.
  Depends on M12.02 and M08.
- [ ] M12.05 Automate profile teardown.
- [ ] M12.06 Verify transaction-pooling compatibility of EF Core and durable
  messaging, direct routes for session-affine coordination, and server-pool
  saturation. Depends on M12.02 and M05.01; this gate can run ahead of the
  M08-dependent failover workflow.

Acceptance: the provider performs database promotion; the application handles
interrupted transactions and connections without inventing commit outcomes or
duplicating operations. The same application binaries use configured managed
routes; transaction pooling, bounded fallback, and server-pool contention are
demonstrated. HA standby and query replica roles are separately visible.
The exercise ends with resource cleanup.

### M15 — API rate limiting

- [ ] M15.01 Enforce API rate limits consistently across replicas under the
  [resilience boundaries](architecture.md#12-resilience-boundaries): select
  gateway enforcement or instance middleware backed by a shared store, return
  Problem Details for rejected requests, and show them in the frontend. Verify
  that limits hold across replicas. Depends on M08.

Acceptance: browser and machine callers exceeding their limits are rejected
consistently regardless of the replica that receives the request.

### M13 — Lab demonstrations and fault tooling

- [ ] M13.01 Assemble repeatable driver and operator demonstrations covering
  discovery, booking, charging, final usage, files, notification, and reconciliation.
- [ ] M13.02 Add k6/Toxiproxy scenario tooling for small concurrency/pool budgets,
  queue/consumer backlogs, provider isolation, and dependency interruption.

Acceptance: every selected component participates in an observable application
flow. Local demonstrations require no cloud subscription; hosted exercises
use the same application contracts with configured provider endpoints.

### M14 — AmpTrail queued-provider integration

Deferred until the synchronous provider flows are complete and hardened. The
AmpTrail lab provider remains a stub host until then.

- [ ] M14.01 Deliver start/stop for AmpTrail with its persistent
  lab provider, client, acknowledgement/query behavior, and correlation recovery.
  Verify lost acknowledgements, delayed effects, and interrupted requests without
  blind resubmission. Add AmpTrail coverage to charging progress.
  Depends on M06.02 and M07.01.
- [ ] M14.02 Exercise the AmpTrail flow through crash after external effect,
  provider restart, rate limiting, and circuit opening, and verify per-provider
  concurrency isolation. Depends on M06.05 and M14.01.

Acceptance: driver start/stop, progress, and operator investigation work against
both providers. Uncertain AmpTrail effects remain visible and recoverable without
blind resubmission.

## 4. Component coverage

| Component / boundary | Implementation |
|---|---|
| Aspire, process hosts, OCI containers | M01; resource wiring in each milestone |
| Ingress, gateway, and runtime replicas | M01, M02, M08; rate limiting M15 |
| React, Mantine, TanStack Query | M01, M02, M04–M12 |
| Keycloak, BFF, shared ticket/key state | M02 |
| PostgreSQL, EF Core, Npgsql | M02, M03; runtime feature access M04–M07, M10 |
| Managed read replica and PgBouncer | M12; application routes introduced in M04 |
| RabbitMQ jobs/topics, Wolverine inbox/outbox | Reservation expiry M05.02; independent subscribers M05.04 onward |
| Durable scheduler / reconciliation | Reservation expiry M05.02; charging M06, files M10 |
| Provider simulators, HTTP resilience | M06, M07; queued provider M14 |
| Notification mail sink/adapters and worker | Reservation notifications M05.04; completion M07.02; external exercise M05.07 |
| Kafka and observation consumers | M07 |
| MongoDB | M07; operator investigation M06.04.2 |
| Valkey and SignalR backplane | M08 |
| Elasticsearch | M09 |
| Independent cache and search index workers | M08, M09 |
| S3 adapter / local container | Compatibility selection M01.08; station-media flow M10.01 |
| SkiaSharp, PDFsharp | M10 |
| R2 and Cloudflare CDN | M11 |
| Azure managed PostgreSQL | M12 |
| OpenTelemetry and Aspire Dashboard | M01.06 onward |
| Optional observability profile | M01.12 |
| Frontend diagnostics / Sentry | M01.13; hosted gate M01.14 |
| xUnit and Aspire testing | M01.07 onward |
| Testcontainers | M02.03 onward |
| Vitest, React Testing Library, Playwright | M01 onward |
| k6 and Toxiproxy | M13; available for earlier gates |

Milestone tasks verify relevant [fault scenarios](lab.md#24-fault-scenarios) as
their boundary becomes usable.

## 5. Pending implementation selections

| Selection | Constraint / dependency | Resolution task |
|---|---|---|
| Database retry ownership | Review bounded EF retries for API read routes separately from transactional writes and message retries; write retries require transaction replay and uncertain-commit handling | Pending review |
| Instructional video processing | Verify container and duration and produce a poster image; options include FFmpeg in the media worker or a constrained pass-through with an operator-supplied poster | M10.03 |
| API rate limiting | Enforce limits at the gateway or through instance middleware backed by a central store (for example, Redis), consistently across replicas | M15.01 |
| Hosted public asset hostname | Subdomain in the existing Cloudflare account; separate public assets and private transfer endpoints | M11.01 |
| Azure region and database resources | Provider-supported HA, readable replica/pooler capability, bounded exercise cost | M12.01 |
