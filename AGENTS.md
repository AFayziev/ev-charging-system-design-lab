# Repository guidance

## System and references

This monorepo contains an EV charging system-design lab with a .NET/C# backend
and a shared React/TypeScript frontend for drivers and backoffice operator users.
It models production component interactions under small synthetic workloads,
including concurrency, dependency failures, and uncertain external effects.

| Reference | Responsibility |
|---|---|
| [README](README.md) | System overview, requirements, and local commands |
| [Architecture](docs/architecture.md) | Components, ownership, data flows, and failure boundaries |
| [Domain](docs/domain.md) | Canonical terms, permissions, lifecycle, and business rules |
| [Integrations](docs/integrations.md) | Third-party provider contracts, simulators, delivery adapters, and command safety |
| [Lab](docs/lab.md) | Technical setup, profiles, timings, fault scenarios, and verification |
| [Plan](docs/implementation-plan.md) | Task IDs, dependencies, status, and acceptance criteria |

## Projects

Projects live under `src/`. Responsibilities describe the intended host boundaries;
implementation progress is recorded in the plan.

| Project | Responsibility |
|---|---|
| `EvCharging.AppHost` | Aspire resource graph, endpoints, references, environment, and startup dependencies |
| `EvCharging.Gateway` | YARP/BFF, browser sessions, authentication, browser/service/callback routing, and API balancing |
| `EvCharging.Api` | Commands/queries, transactional acceptance, file authorization, callbacks, and live delivery |
| `EvCharging.ChargingWorker` | Charging effects, reservation expiry, reconciliation, and recovery polling |
| `EvCharging.ObservationWorker` | Observation history/latest views, lifecycle and final-usage evidence, and stalled-session detection |
| `EvCharging.SearchIndexWorker` | Search indexing, replacement-index rebuilds, validation, and switchover |
| `EvCharging.CacheWorker` | Cached query-view updates, invalidation, warming, and recovery |
| `EvCharging.MediaWorker` | Media processing/publication, session summaries, diagnostic bundles, and upload/orphan cleanup |
| `EvCharging.NotificationWorker` | Notification jobs, simulator/real-provider adapters, retries, and persisted delivery attempts |
| `EvCharging.SynchronousChargingProviderApi` | Simulator with authoritative synchronous results and command deduplication |
| `EvCharging.QueuedChargingProviderApi` | Simulator with queued acknowledgement and no effect deduplication |
| `EvCharging.NotificationProviderApi` | Third-party notification service simulator; inspectable synthetic outcomes and controlled faults, no real delivery |
| `EvCharging.ServiceDefaults` | Shared discovery, HTTP resilience, OpenTelemetry, and health checks; a library, not a resource |

Projects dedicated to handling incoming HTTP/gRPC requests use the `Api` suffix.
Queue, stream, and scheduled-job processors use `Worker`. The proxy/BFF host uses
`Gateway`.

`infra` owns infrastructure configuration; `tests` owns verification; `tools` owns
repeatable lab commands. Package and image pins belong in the existing central
manifests, not duplicated in project files or a separate toolchain document.

## Aspire workflow

- Use the project-local [Aspire skills](.agents/skills/aspire/SKILL.md): `aspireify`
  for wiring, `aspire-orchestration` for lifecycle, and `aspire-monitoring` for diagnostics.
- Run the topology through Aspire CLI, not `dotnet run`. Target
  `src/EvCharging.AppHost/EvCharging.AppHost.csproj` explicitly. Agents use detached
  `aspire start --non-interactive`; the README contains the human foreground command.
- Use `--isolated` for git worktrees. Use `aspire wait <resource>` for readiness and
  `aspire describe` for endpoints; avoid port guessing and HTTP polling loops.
- Stop the agent-owned validation session when finished. Stop before rebuilding
  locked outputs. Do not terminate unrelated processes or remove persistent data
  to resolve a build failure.
- Connect HTTP dependencies through `WithReference` and discovery-aware clients
  or YARP destinations. Use logical service names rather than fixed localhost URLs.
  Wiring and profile definitions are in the [lab reference](docs/lab.md).
- Route application HTTP calls to platform APIs through the gateway, including
  worker submissions and provider callbacks. Only the gateway references platform
  API destinations. External provider calls and orchestration health checks use
  their own endpoints.
- Keep `AddServiceDefaults` on every application/simulator host. AppHost propagates
  its environment to child hosts; development-only `/health` and `/alive` routes
  depend on that propagation. Workers have no HTTP listeners.
- Default HTTP retries exclude unsafe methods. Provider effect retries require
  an explicit policy permitted by the integration contract; queued acknowledgements
  and lost responses require reconciliation.
- Inspect resource state, logs, and traces before changing code for a runtime issue.
  Distinguish application health from orchestrator and infrastructure health;
  healthy application hosts do not establish healthy container monitoring.
- Keep credentials, dashboard login tokens, and signed URLs out of source control,
  documentation, and verification output.

## Documentation and tracking

Write standalone technical documents without chat history, drafting commentary,
or reader-directed next steps. Record unresolved decisions factually. Keep domain
rules in `domain.md`, architecture in `architecture.md`, and technical setup
and experiments in `lab.md`; link instead of duplicating.
Each decision or technical detail has one authoritative location. Other documents
link to it. Plans record tasks, dependencies, and acceptance without restating design.

Update task checkboxes and milestone status with the implementation. Do not add
evidence paragraphs, duplicate progress summaries, or follow-up commits just to
record a commit hash. Verification belongs in the chat report or PR description.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):
`<type>[optional scope][optional !]: <description>`. Implementation commit bodies
identify completed tasks with `Plan: M01.02` or a comma-separated list of IDs.
Do not add a `Verification:` section to commit messages. Commit only when requested.
