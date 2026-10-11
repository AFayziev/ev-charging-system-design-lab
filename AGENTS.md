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
| [Backend guidelines](docs/backend-guidelines.md) | Backend design, coding, persistence, testing, and review guidance |

Follow the backend guidelines for backend code and tests. Use the repository
EditorConfig and SDK analyzers; keep new and changed code free of warnings.
Before changing business entities, persistence mappings, or command/query handlers,
read [Domain models, persistence, and validation](docs/backend-guidelines.md#domain-models-persistence-and-validation).
Apply its review criteria before considering the implementation complete.

## Projects

Projects live under `src/`. Responsibilities describe the intended host boundaries;
implementation progress is recorded in the plan.

| Project | Type | Responsibility |
|---|---|---|
| `EvCharging.AppHost` | Orchestrator | Aspire resource graph, endpoints, references, environment, and startup dependencies |
| `EvCharging.Gateway` | Resource | YARP/BFF, browser sessions, authentication, and browser/service/callback routing to runtime-balanced API services |
| `EvCharging.Gateway.Migrations` | Resource | EF migrations, model snapshot, design-time tooling, and one-shot authentication-schema migrations using schema-owning credentials |
| `EvCharging.Web` | Resource | Shared React/TypeScript browser application for drivers and backoffice operators |
| `EvCharging.Api` | Resource | Commands/queries, transactional acceptance, file authorization, callbacks, and live delivery |
| `EvCharging.ChargingWorker` | Resource | Charging effects, reservation expiry, reconciliation, and recovery polling |
| `EvCharging.ObservationWorker` | Resource | Observation history/latest views, lifecycle and final-usage evidence, and stalled-session detection |
| `EvCharging.SearchIndexWorker` | Resource | Search indexing, replacement-index rebuilds, validation, and switchover |
| `EvCharging.CacheWorker` | Resource | Cached query-view updates, invalidation, warming, and recovery |
| `EvCharging.MediaWorker` | Resource | Media processing/publication, session summaries, diagnostic bundles, and upload/orphan cleanup |
| `EvCharging.NotificationWorker` | Resource | Notification jobs, simulator/real-provider adapters, retries, and persisted delivery attempts |
| `EvCharging.SynchronousChargingProviderApi` | Resource | Simulator with authoritative synchronous results and command deduplication |
| `EvCharging.QueuedChargingProviderApi` | Resource | Simulator with queued acknowledgement and no effect deduplication |
| `EvCharging.NotificationProviderApi` | Resource | Third-party notification service simulator; inspectable synthetic outcomes and controlled faults, no real delivery |
| `EvCharging.Contracts` | Library | Shared API and messaging contracts and canonical role types |
| `EvCharging.Domain` | Library | Business entities, value objects, measurements, and pure business policies |
| `EvCharging.Persistence` | Library | Charging-schema EF mappings, database access, auditing, and persistence-owned records for API/worker consumers |
| `EvCharging.Persistence.Migrations` | Resource | Charging-schema EF migrations, message-store provisioning, design-time tooling, sample-data seeding/reset, and one-shot migrations using schema-owning credentials |
| `EvCharging.ServiceDefaults` | Library | Shared discovery, HTTP resilience, OpenTelemetry, and health checks |

Projects dedicated to handling incoming HTTP/gRPC requests use the `Api` suffix.
Queue, stream, and scheduled-job processors use `Worker`. The proxy/BFF host uses
`Gateway`.

`infra` owns infrastructure configuration; `tests` owns verification; `tools` owns
repeatable lab commands. NuGet package pins belong in `Directory.Packages.props`;
container image pins belong alongside their AppHost resource definitions. Do not
duplicate them in documentation or a separate toolchain document.

## Implementation choices

Before introducing any custom machinery, check framework and library documentation
and established conventions for an existing solution. Prefer stable provided
capabilities when they meet the requirement; custom mechanisms require an identified
gap. This applies to all languages and components.

Before implementation, identify the provided API or document the concrete
capability gap using official documentation, package APIs, or library source.
Include this evidence in the task report, not repository documentation. Custom
machinery without this evidence is incomplete work and must be rejected during review.

Place code according to its responsibility and expected consumers. Keep
feature-specific behavior in its feature slice; place capabilities used across
features in the owning shared area, rather than the first feature that needs them.

Implement cross-cutting behavior through established framework pipelines and
extension points where available. Features declare their participation and
configuration rather than repeat the underlying mechanics.

Generalize shared mechanisms through common contracts or framework metadata.
Avoid concrete feature/entity branches in shared processing. Keep business-specific
intent explicit at the feature boundary.

Make participation and data collection explicit. A shared contract does not imply
that every operation or property participates.

Preserve the owning operation's transaction, cancellation, and failure semantics.
Verify meaningful behavior, including failure and retry cases, rather than merely
checking registrations or framework configuration.

Give configuration values one owner. Derive dependent settings from resource
metadata or existing options, and reuse existing registration helpers instead of
copying literals or policies. Independent public-contract assertions may repeat
expected values; tests must explicitly configure scenario settings rather than
depend on application defaults.

Follow the [identifier conventions](docs/backend-guidelines.md#identifiers)
for application-owned records.

For externally referenced resources:

- Keep internal primary/foreign keys separate from `PublicId`; enforce public
  identifier uniqueness.
- Use public identifiers in browser routes, API requests/responses, errors, and
  pagination cursors; do not expose or encode internal keys in public contracts.
- Keep equipment labels and locally scoped display numbers separate from both
  database keys and public identifiers. Treat frontend public IDs as strings.
- Add public identifiers when a feature requires external references, not to
  every persisted record.
- Preserve resource authorization independently of identifier predictability.

## Frontend

Organize code by feature: application composition in `app`, feature-owned UI and
contracts in `features/<feature>`, and shared infrastructure in `lib`. Colocate
tests with the code they cover; add directories as their contents are needed.

Use `@/` imports across application areas and relative imports for nearby files
within an area or feature.

### API access

- Keep shared Axios transport configuration in `lib`. Group named, typed backend
  operations in `features/<feature>/api.ts`; keep endpoint URLs and response
  unpacking there rather than in UI components.
- Preserve cancellation signals and native client errors. Choose user-facing
  error messages at the presentation boundary.
- Export readonly query-key arrays and parameterized key factories from the
  owning feature. Include parameters that affect results; reuse keys for queries
  and cache operations.
- Use native TanStack `queryOptions` to share keys, request functions, and query
  policies. Colocate these definitions with feature API methods; split modules
  when their contents warrant it.

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
link to it. This file links to documentation; documents do not link back to this file.
Plans record tasks, dependencies, and acceptance without restating design.
Update documentation when decisions, constraints, contracts, or experiments change;
an implementation task alone does not require more narrative. Do not restate
application logic, source configuration values, or framework defaults. Link to
source when implementation details are needed. Numerical experiment settings
belong in the lab only when they define the experiment, rather than copy a code default.
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
