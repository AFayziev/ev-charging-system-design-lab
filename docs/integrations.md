# External Integration Contracts

The lab uses two .NET charging-operator simulators and a controlled notification
receiver. They model external boundaries, not complete OCPP/OCPI implementations.
Canonical meanings are defined in [domain](domain.md); processing and persistence
are defined in [architecture](architecture.md).

## 1. Operator contracts

| Capability | Operator A | Operator B |
|---|---|---|
| Response | Synchronous authoritative applied/rejected result | Queued acknowledgement; applied/rejected result obtained asynchronously |
| Provider command idempotency | Same key and payload return existing command/result without another effect | No effect deduplication; repeated submissions can create additional effects |
| Queries | Command outcome and matching provider session | Command outcome and matching provider session |
| Measurement units | kW and cumulative session kWh | W and cumulative session Wh |
| Payload format | Provider-specific format A | Distinct provider-specific format B |

Both preserve platform correlation references in command/session records.
Queries support provider command/session identity and the platform correlation
reference, including when a lost response prevents the platform learning the
provider command ID. Results distinguish queued, applied, and definitively rejected
requests. A rejection establishes that the request cannot execute later.

Adapters map identities, acknowledgement stages, state, timestamps, and units.
Unknown fields/statuses remain inspectable rather than becoming success.
Provider state and identity survive simulator restart.

## 2. Command safety

Operator A permits bounded retry with the same provider idempotency key. Operator B
requires reconciliation after a queued acknowledgement or ambiguous response.
A snapshot with no active session does not establish safe repetition: a queued
request might execute later.

Another Operator B effect request is permitted only when definitive evidence
resolves the previous request as rejected and [domain eligibility](domain.md#5-reservations)
still holds. A rejected start ends its platform session; a subsequent start is a
new operation/session. Queries can repeat within their separate budget.

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

Provider identity namespaces connector sequences and external IDs. Normal clocks
share the lab UTC clock. Adapters apply the
[domain ordering and measurement rules](domain.md#8-time-identity-and-ordering).

Callbacks are acknowledged only after durable stream acceptance. Unacknowledged
delivery is retried with the same event identity under bounded scenario settings;
an ambiguous acknowledgement can therefore produce duplicates. Polling retrieves
current/session evidence when callbacks are interrupted. A current snapshot is
not a reconstruction of every missed historical observation.

Fault injection can delay acknowledgement, apply an effect then lose its response,
return rejection, rate-limit requests, interrupt telemetry, deliver duplicates or
older events, skew timestamps, and report anomalous energy. Injected behavior is
visible in provider records so [lab scenarios](lab.md#4-fault-scenarios) can compare
platform conclusions with simulated physical truth.

## 4. Notification receiver

The controlled receiver exposes inspectable delivery outcomes, including latency,
rejection, outage, and lost-response faults. Jobs retain delivery identity and
attempt history. An ambiguous delivery remains inspectable; broker redelivery
alone is not an exactly-once delivery guarantee. Notification failure does not
change a completed charging session.
