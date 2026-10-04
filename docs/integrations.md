# External Integration Contracts

`SynchronousChargingProviderApi` and `QueuedChargingProviderApi` simulate external
charging systems with different command contracts. `NotificationProviderApi`
simulates a third-party notification service; a real provider can replace it through
an outbound adapter. OCPP/OCPI implementation is outside scope.
Canonical meanings are defined in [domain](domain.md); processing and persistence
are defined in [architecture](architecture.md).

## 1. Provider contracts

| Capability | Synchronous provider | Queued provider |
|---|---|---|
| Response | Synchronous authoritative applied/rejected result | Queued acknowledgement; applied/rejected result obtained asynchronously |
| Provider command idempotency | Same key and payload return existing command/result without another effect | No effect deduplication; repeated submissions can create additional effects |
| Queries | Command outcome and matching provider session | Command outcome and matching provider session |
| Measurement units | kW and cumulative session kWh | W and cumulative session Wh |
| Payload format | Synchronous provider schema | Distinct queued provider schema |

Both preserve platform correlation references in command/session records.
Queries support provider command/session identity and the platform correlation
reference, including when a lost response prevents the platform learning the
provider command ID. Results distinguish queued, applied, and definitively rejected
requests. A rejection establishes that the request cannot execute later.

Adapters map identities, acknowledgement stages, state, timestamps, and units.
Unknown fields/statuses remain inspectable rather than becoming success.
Provider state and identity survive simulator restart.

## 2. Command safety

The synchronous provider permits bounded retry with the same provider idempotency
key. The queued provider requires reconciliation after a queued acknowledgement or
ambiguous response.
A snapshot with no active session does not establish safe repetition: a queued
request might execute later.

Another queued-provider effect request requires definitive rejection evidence and
the [domain eligibility rules](domain.md#5-reservations). Queries can repeat within
their separate budget.

HTTP effect retries are explicit per adapter; transport timeouts and generic
resilience defaults do not supply rejection evidence. Persistence of a platform
operation or acknowledgement of a broker message does not deduplicate an external
effect.

## 3. Observations and recovery

Each simulator provides:

- Source event IDs unique within that provider.
- Monotonic sequences scoped to its connector.
- Provider session identity, source occurrence time, physical state, and
  cumulative session energy where applicable.
- Matching terminal state and an explicitly final energy reading for ended sessions.
- Event identity, sequence continuity, sessions, and command outcomes retained
  across restart.

Provider identity namespaces connector sequences and external IDs. Adapters apply the
[domain ordering and measurement rules](domain.md#8-time-identity-and-ordering).

Provider callbacks target the platform gateway, which routes them to authenticated
observation ingestion. Recovery polling calls provider APIs directly; the charging
worker submits raw evidence and normalized fields through the gateway to the same
ingestion path.
Callbacks are acknowledged only after durable stream acceptance. Unacknowledged
delivery is retried with the same event identity under bounded scenario settings;
an ambiguous acknowledgement can therefore produce duplicates. Polling retrieves
current/session evidence when callbacks are interrupted. A current snapshot is
not a reconstruction of every missed historical observation.

Fault injection can delay acknowledgement, apply an effect then lose its response,
return rejection, rate-limit requests, interrupt telemetry, deliver duplicates or
older events, skew timestamps, and report anomalous energy. Injected behavior is
visible in provider records so [lab scenarios](lab.md#24-fault-scenarios) can compare
platform conclusions with simulated physical truth.

## 4. Notification providers

A provider adapter accepts a provider-independent request containing notification
identity, recipient, content, and selected provider. It returns a normalized outcome
with provider identity, external request/message IDs, and timestamps.
NotificationWorker retains that evidence in durable delivery-attempt records.

`NotificationProviderApi` is the local simulator of a third-party service. It
accepts synthetic delivery requests and exposes inspectable outcomes, including
latency, rejection, outage, and lost-response faults. It sends no real email or SMS.

Real delivery uses an explicitly selected adapter in NotificationWorker and calls
the external provider directly. Cloudflare Email Service is the selected email adapter.
Provider acceptance is recorded separately from confirmed recipient delivery;
unavailable delivery evidence remains unknown.
An ambiguous send does not authorize blind retry. Adapter policies define permitted
retries and available outcome queries; broker redelivery is not an exactly-once
delivery guarantee. There is no automatic switch to another provider after failure.
Notification failure does not change a reservation or completed charging session.

The simulator is the local default. There is no claim of provider-side deduplication
unless the selected service documents and verifies it. Technical configuration,
sender setup, and extension options are recorded in
[notification adapters](lab.md#18-notification-adapters);
implementation and hosted verification are tracked in the [plan](implementation-plan.md).
