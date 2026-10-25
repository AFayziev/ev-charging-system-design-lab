# External Integration Contracts

WattHarbor and AmpTrail are fictional external charging systems with distinct API
contracts, implemented by the lab's provider hosts. Local notification delivery uses
a mail sink through an outbound adapter. OCPP/OCPI implementation is outside scope.
Canonical meanings are defined in [domain](domain.md); processing and persistence
are defined in [architecture](architecture.md).

## 1. Provider contracts

| Capability | WattHarbor (synchronous) | AmpTrail (queued) |
|---|---|---|
| Response | Authoritative success or rejection with Problem Details | Queued acknowledgement; applied/rejected result obtained asynchronously |
| Provider command idempotency | Same key and payload return existing command/result without another effect | No effect deduplication; repeated submissions can create additional effects |
| Queries | Command outcome and matching provider session | Command outcome and matching provider session |
| Measurement units | kW and cumulative session Wh | W and cumulative session Wh |
| Payload format | WattHarbor schema | AmpTrail schema |

Both preserve caller-owned operation and session identifiers in command/session records.
Queries support provider command/session identity and the platform correlation
identifier, including when a lost response prevents the platform learning the
provider command ID. Results distinguish queued, applied, and definitively rejected
requests. A rejection establishes that the request cannot execute later.
Lookups expose the current command result or session state. Replayed submissions
and reads do not require an interaction history, query counters, or evidence-export
endpoints.

Each integration implements a specific external system's API contract. Synchronous or
queued describes command behavior; it does not identify a shared API surface.
A provider binding selects its concrete client and connection settings independently
of the provider's display name. Clients map identities, acknowledgement stages,
state, timestamps, and units.
Unknown fields/statuses remain inspectable rather than becoming success.
Provider state and identity survive provider restart.

### 1.1 WattHarbor HTTP API

Calls use bearer tokens for the provider audience. Charging clients can submit
commands and read outcomes; simulation controls require the separate control role.
Equipment addresses are provider-owned strings. Command and session identifiers
are opaque UUIDs. `ExternalOperationId` and `ExternalSessionId` identify the calling
system's operation and session; the platform supplies its public identifiers.
The `Idempotency-Key` header supplies `ExternalOperationId` for a start or stop,
and the request body supplies `ExternalSessionId`: the new session for a start, the
session to end for a stop. Provider-generated IDs remain separate.
Request and response schemas are defined by the provider's generated OpenAPI document.

#### Public provider API

| Method and path | Purpose |
|---|---|
| `POST /commands/start` | Submit a charging start command |
| `POST /commands/stop` | Submit a command to end the session with the given `ExternalSessionId` |
| `GET /commands/{id}` | Look up a provider command by its returned identifier |
| `GET /commands/by-external-operation/{externalOperationId}` | Resolve a command by the caller's operation ID |
| `GET /sessions/{id}` | Read the matching provider session |
| `GET /sessions/by-external-session/{externalSessionId}` | Resolve a session by the caller's session ID |
| `GET /sessions/by-external-session/{externalSessionId}/snapshot` | Read the session's latest recorded observation, carrying the event identity and occurrence time its callback used |
| `GET /stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}` | Read equipment availability and rated power |

A successful start establishes that charging was applied. Rejected starts and stops
return Problem Details; command lookups expose the command kind and the recorded
outcome, including rejections.

| Condition | HTTP status | Error or outcome |
|---|---|---|
| Authentication or authorization denied | 401 / 403 | Request rejected before command creation or execution; no provider command ID |
| Unknown, disabled or occupied equipment | 409 | Definitive rejection: `connector_unavailable` |
| External session ID already used by another start | 409 | Definitive rejection: `external_session_id_conflict` |
| Idempotency key reused with a different payload or command kind | 409 | `idempotency_conflict`; original command unchanged |
| Stop for an `ExternalSessionId` the provider has no session for | 409 | Definitive rejection: `session_not_found` |

- Applied commands and business rejections are retained. Identical replays return
  the original success or business rejection even after equipment availability changes.
- Recognized rejection codes establish that the command cannot execute later.
  Transport failures and unrecognized errors require outcome lookup.
- An authentication or authorization denial establishes non-execution of the
  denied request, not other requests for the same command.
- A missing lookup does not establish that an in-flight request cannot commit later.
- A stop applies once: it ends the session and frees its connector. Stopping an
  already ended session is applied, because the requested end state holds; it is not
  a rejection. Ended sessions report `stopped` and an `endedAt` time.
  The end time is the session's `endedAt`, never a later stop's `appliedAt`.
  A stop acknowledgement does not report final energy usage.

#### Internal simulation API

| Method and path | Purpose |
|---|---|
| `PUT /simulation/stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}` | Configure simulated equipment while preserving existing occupancy. An optional energy limit makes the vehicle full: the provider ends a session on that connector once its energy reaches the limit |
| `PUT /simulation/commands/{externalOperationId}` | Configure temporary command-scoped response loss and outcome-lookup failures, which a stop can also match by its session ID; requires the simulation control role |
| `PUT /simulation/callbacks/observations` | Set the callback URL and key the provider delivers observations with |
| `PUT /simulation/sessions/{externalSessionId}/telemetry` | Configure temporary duplicate delivery, older-before-newer delivery, or interrupted delivery for one session's observations |

## 2. Command safety

WattHarbor permits bounded retry with the same provider idempotency
key. AmpTrail requires reconciliation after a queued acknowledgement or
ambiguous response.
A snapshot with no active session does not establish safe repetition: a queued
request might execute later. A stop is evidenced only by its own command record:
an ended session does not show that this stop ended it, and a missing record permits
the same bounded same-key resend as a start. A request whose response was
never recorded (for example, its worker died mid-request) and has no provider
record is closed as uncertain by the next automatic check, once it is older than
one check interval, and resent the same way; a record found for it resolves the
command and closes the request. Neither applies to AmpTrail.

Another AmpTrail effect request requires definitive rejection evidence and
the [domain eligibility rules](domain.md#5-reservations). Outcome queries are
read-only and may repeat under bounded request and admission policies.

HTTP effect retries are explicit per integration; transport timeouts and generic
resilience defaults do not supply rejection evidence. Persistence of a platform
operation or acknowledgement of a broker message does not deduplicate an external
effect.

## 3. Observations and recovery

Clients map platform chargers and connectors to provider addresses. A provider
may require an EVSE identifier in addition to its charger and connector identifiers.
That address component belongs to the integration mapping, not the platform equipment hierarchy.
Distinct platform connectors must map to independently usable provider capacity.

Each lab provider provides:

- Source event IDs unique within that provider.
- Provider session identity, source occurrence time (the only ordering field),
  physical state, and cumulative session energy where applicable.
- Matching terminal state and an explicitly final energy reading for ended sessions, however they end: the platform's stop or the provider's own decision (for example a full vehicle). A provider-ended session frees its connector, and no platform command is involved.
- Event identity, sessions, and command outcomes retained across restart.

Provider identity namespaces event and external IDs. Clients apply the
[domain ordering and measurement rules](domain.md#8-time-identity-and-ordering).

Provider callbacks target the platform gateway, which routes them to authenticated
observation ingestion. Each provider integration authenticates its callbacks with its
own credential, and the credential, not the payload, establishes the integration.
Recovery polling calls provider APIs directly; the charging worker appends the
polled observations to the same observation stream.
A callback can carry several observations, within a configured limit. It is
acknowledged only after every observation has durable stream acceptance, and one
invalid observation rejects the whole callback. Unacknowledged delivery is retried
as a whole with the same event identities under bounded scenario settings;
an ambiguous acknowledgement can therefore produce duplicates. Polling retrieves
current/session evidence when callbacks are interrupted. A current snapshot is
not a reconstruction of every missed historical observation.

Fault injection can delay acknowledgement, apply an effect then lose its response,
return rejection, rate-limit requests, interrupt telemetry, deliver duplicates or
older events, and fail snapshot reads. Injected behavior is
visible in provider records so [lab scenarios](lab.md#24-fault-scenarios) can compare
platform conclusions with simulated physical truth.

## 4. Notification providers

A provider adapter accepts a provider-independent request containing notification
identity, recipient, content, and selected provider. It returns a normalized outcome
with provider identity, external request/message IDs, and timestamps.
NotificationWorker retains that evidence in durable delivery-attempt records.

The local adapter sends frozen email content to an inspectable mail sink.
Its returned message ID establishes local capture, not real recipient delivery.
The sink provides no assumed send deduplication. Local retries require proof of
non-capture and a remaining budget; uncertainty stops automatic sending.
Reconciliation and manual resend are outside the local contract.

Real delivery uses an explicitly selected adapter in NotificationWorker and calls
the external provider directly. Its guarantees depend on the selected provider.
Provider acceptance is recorded separately from confirmed recipient delivery;
unavailable delivery evidence remains unknown.
An ambiguous send does not authorize blind retry. Adapter policies define permitted
retries and available outcome queries; broker redelivery is not an exactly-once
delivery guarantee. There is no automatic provider fallback after failure.
Each permitted delivery attempt uses the provider configured when that attempt
executes. Changing the configured provider does not establish that an uncertain
earlier send is safe to repeat.
Notification failure does not change a reservation or completed charging session.

The mail sink is the local default. There is no claim of provider-side deduplication
unless the selected service documents and verifies it. Technical configuration,
sender setup, and extension options are recorded in
[notification adapters](lab.md#18-notification-adapters);
implementation and hosted verification are tracked in the [plan](implementation-plan.md).

## 5. Known deviations from real providers

The lab keeps provider contracts simple enough to exercise component interactions.
These simplifications may differ from typical real charging providers:

- **Commands.** WattHarbor answers synchronously and deduplicates by key. Real
  providers commonly acknowledge a command and report its result later, often
  without an idempotency key. AmpTrail ([plan](implementation-plan.md), M14) is the
  representative shape, and the same-key resend above is specific to WattHarbor.
- **Session origin.** Every session starts through a platform command. Sessions
  started at the charger and unknown provider sessions are not modelled.
- **Reservations.** They exist only on the platform and are not reserved at the
  provider, so availability can change before the driver arrives.
