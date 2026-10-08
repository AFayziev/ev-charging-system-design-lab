# Repository guidance

## System and references

This monorepo contains an EV charging system-design lab with a .NET/C# backend
and a shared React/TypeScript frontend for drivers and backoffice operator users.
It models production component interactions under small synthetic workloads,
including concurrency, dependency failures, and uncertain external effects.

| Reference | Responsibility |
|---|---|
| [README](README.md) | System overview, requirements, and local commands |
| [Architecture](docs/architecture.md) | Technology-neutral component roles, ownership, data flows, and failure boundaries |
| [Domain](docs/domain.md) | Canonical terms, permissions, lifecycle, and business rules |
| [Integrations](docs/integrations.md) | Third-party provider contracts, simulators, delivery adapters, and command safety |
| [Lab](docs/lab.md) | Technology selections, implementation constraints, profiles, timings, fault scenarios, and verification |
| [Plan](docs/implementation-plan.md) | Task IDs, dependencies, status, and acceptance criteria |
| [C# conventions](docs/csharp-conventions.md) | C# design and review guidelines, cleanup and validation commands |

Follow the C# conventions for application code and tests. Use the repository
EditorConfig and SDK analyzers; keep new and changed code free of warnings.

## Projects

Projects live under `src/`. Responsibilities describe the intended host boundaries;
implementation progress is recorded in the plan.

| Project | Responsibility |
|---|---|
| `EvCharging.AppHost` | Aspire resource graph, endpoints, references, environment, and startup dependencies |
| `EvCharging.Gateway` | YARP/BFF, browser sessions, authentication, and browser/service/callback routing to runtime-balanced API services |
| `EvCharging.Gateway.Migrations` | One-shot authentication-schema migrations using schema-owning credentials |
| `EvCharging.Web` | Shared React/TypeScript browser application for drivers and backoffice operators |
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
repeatable lab commands. NuGet package pins belong in `Directory.Packages.props`;
container image pins belong alongside their AppHost resource definitions. Do not
duplicate them in documentation or a separate toolchain document.

## Aspire workflow

- Use the project-local [Aspire skills](.agents/skills/aspire/SKILL.md): `aspireify`
  for wiring, `aspire-orchestration` for lifecycle, and `aspire-monitoring` for diagnostics.
- Prefer Aspire-provided integrations and wiring. Check the official documentation
  and published package dependencies before choosing a wiring approach. Use custom
  wiring only for a verified gap; differing package versions alone do not establish
  incompatibility.
- Use conventional .NET extension methods for reusable resource registration and
  wiring. Keep resource references and the application graph explicit in AppHost.
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
  The resource graph is defined in [AppHost](src/EvCharging.AppHost/AppHost.cs);
  profile definitions are in the [lab reference](docs/lab.md).
- Route application HTTP calls to platform APIs through the gateway, including
  worker submissions and provider callbacks. Only the gateway references platform
  API destinations. External provider calls and orchestration health checks use
  their own endpoints.
- Keep `AddServiceDefaults` on every application/simulator host and propagate
  the AppHost environment to child hosts. Workers have no HTTP listeners.
- Default HTTP retries exclude unsafe methods. Provider effect retries require
  an explicit policy permitted by the integration contract; queued acknowledgements
  and lost responses require reconciliation.
- Inspect resource state, logs, and traces before changing code for a runtime issue.
  Distinguish application health from orchestrator and infrastructure health;
  healthy application hosts do not establish healthy container monitoring.
- Use Aspire Dashboard and CLI for diagnostics they already expose. Do not add
  custom diagnostic endpoints or verification harnesses to duplicate those tools.
- Keep credentials, dashboard login tokens, and signed URLs out of source control,
  documentation, and verification output.

## Documentation and tracking

Write standalone technical documents without chat history, drafting commentary,
or reader-directed next steps. Record unresolved decisions factually. Keep domain
rules in `domain.md`, technology-neutral design in `architecture.md`, and concrete
technology selections, implementation constraints, and experiments in `lab.md`;
link instead of duplicating. Architecture uses component roles such as relational
database, gateway, load balancer, and orchestrator rather than product names.
Each decision or technical detail has one authoritative location. Other documents
link to it. Plans record tasks, dependencies, and acceptance without restating design.
Do not narrate implementation details that are evident from source code.
Documents describe decisions, constraints, contracts, and verification; link to
source when implementation details are needed.
Prefer concise tables and lists. Use numbered lists for ordered procedures and
bullets for unordered reference information.

Update task checkboxes and milestone status with the implementation. Do not add
evidence paragraphs, duplicate progress summaries, or follow-up commits just to
record a commit hash. Verification belongs in the chat report or PR description.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):
`<type>[optional scope][optional !]: <description>`. Implementation commit bodies
identify completed tasks with `Plan: M01.02` or a comma-separated list of IDs.
Do not add a `Verification:` section to commit messages. Commit only when requested.
