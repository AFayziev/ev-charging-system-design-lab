# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Source of truth

[AGENTS.md](AGENTS.md) holds the binding repository rules (project responsibilities, implementation choices, verification scope, frontend conventions, Aspire workflow, documentation and commit rules). Read it before non-trivial work. Each topic has one owning document:

- [README.md](README.md): setup and commands
- `docs/domain.md`: business rules and vocabulary
- `docs/architecture.md`: technology-neutral design
- `docs/integrations.md`: provider contracts
- `docs/lab.md`: technology selections, profiles, and experiments
- `docs/implementation-plan.md`: task IDs and status
- `docs/backend-guidelines.md`: C# and persistence rules. Read its "Domain models, persistence, and validation" section before changing entities, mappings, or handlers.

Design docs describe the intended system, not what is implemented. Progress lives only in the plan. Update plan checkboxes with the implementation.

## Commands

.NET 10 / C# 14 backend (`EvChargingSystemDesign.slnx`), React/TypeScript frontend in `src/EvCharging.Web` (Node 24, npm). Docker with a Linux engine is required for most tests and for running the lab.

```sh
pwsh -File tools/dev.ps1              # start provider + platform AppHosts (-Detach, -Isolated)
```

Run the topology through the Aspire CLI, never `dotnet run`. Agents use detached `aspire start --apphost <csproj> --non-interactive`, then `aspire describe` and `aspire wait <resource>`. Stop the session you started. Use `--isolated` in git worktrees. The README documents the independent AppHost startup and the user-secrets that wire provider URLs.

Backend checks, required after every piece of .NET work:

```sh
dotnet format style EvChargingSystemDesign.slnx --no-restore --diagnostics IDE0005 --severity info
dotnet format EvChargingSystemDesign.slnx --no-restore
dotnet build EvChargingSystemDesign.slnx --no-restore
```

Use `--verify-no-changes` on both format commands to verify without modifying files. Warnings are not acceptable in new or changed code.

Tests (each project is run separately):

```sh
dotnet test tests/EvCharging.Domain.Tests
dotnet test tests/EvCharging.Api.Tests
dotnet test tests/EvCharging.ChargingWorker.Tests
dotnet test tests/EvCharging.Persistence.Tests         # PostgreSQL via Testcontainers
dotnet test tests/EvCharging.Gateway.Tests             # PostgreSQL via Testcontainers
dotnet test tests/EvCharging.ProviderContracts.Tests   # starts the provider AppHost
dotnet test tests/EvCharging.E2E.Tests --filter "Category=Api"   # full stack; needs Chromium for browser tests
dotnet test tests/EvCharging.Domain.Tests --filter "FullyQualifiedName~SomeTestName"   # single test
```

Frontend (`npm --prefix src/EvCharging.Web ...`): `ci`, `run typecheck`, `run build` (typecheck + vite), `run format` / `format:check`, `run test` (vitest; set `$env:TZ = "Asia/Tashkent"` first), and `test:watch`. E2E setup is in the README: build the E2E project, then install Chromium with the generated `playwright.ps1`.

Match verification to the change. Do not run the whole E2E suite after small changes. Batch related E2E scenarios into one invocation.

## Architecture

Two independent Aspire AppHosts:

- `src/EvCharging.AppHost` is the platform. Hosts are `Gateway` (YARP/BFF, browser sessions, auth), `Api` (commands/queries, vertical slices under `Features/<Feature>`), the `*Worker` hosts (charging, observation, search-index, cache, media, notification), and the Vite `Web` app.
- `simulators/EvCharging.Providers.AppHost` runs the external charging-provider simulators with their own Keycloak and PostgreSQL. `WattHarborProviderApi` is synchronous with command deduplication. `AmpTrailProviderApi` returns queued acknowledgements with no deduplication. The platform does not start them. `tools/dev.ps1` discovers their dynamic URLs and passes them to the platform.

Shared libraries:

- `Contracts`: API and messaging contracts.
- `Domain`: entities and pure policies, with no persistence concerns.
- `Persistence`: EF mappings for the charging schema, consumed by the API and workers.
- `Persistence.Migrations` and `Gateway.Migrations`: own migrations and seed data, and run as one-shot resources with schema-owning credentials. Runtime roles have narrower permissions.
- `Messaging`: Wolverine configuration and routing, with inbox/outbox. Excluded from the provider simulators.
- `ServiceDefaults`: discovery, HTTP resilience, OpenTelemetry, health. Every app and provider host calls `AddServiceDefaults`.

Rules that span files:

- All application HTTP calls to platform APIs go through the gateway, including worker submissions and provider callbacks. Only the gateway references API destinations. Use logical service names and `WithReference`, not localhost URLs.
- Charging commands have uncertain outcomes (queued acknowledgements, lost responses). The `ChargingWorker` executes them via provider adapters (`Providers/WattHarbor`) and reconciles against observed state. Default HTTP retries exclude unsafe methods, and provider effect retries need an explicit policy allowed by `docs/integrations.md`.
- Externally referenced resources keep internal keys separate from `PublicId`. Only public IDs appear in routes, contracts, errors, and cursors.
- Package versions are pinned in `Directory.Packages.props`. Container image pins live next to their AppHost resource definitions. Analyzer and formatting rules come from `.editorconfig` and `Directory.Build.props`.
- Prefer framework or library capabilities over custom machinery. A custom mechanism needs a documented capability gap.
- Frontend code is organized by feature (`app`, `features/<feature>`, `lib`). Use `@/` imports across areas. Mantine form validation handles client-side checks, and backend errors are chosen by status or machine-readable identifier, never by message text. API calls and TanStack `queryOptions` live in `features/<feature>/api.ts`.

## Git

Conventional Commits. Implementation commit bodies list the plan tasks as `Plan: M01.02`. Do not add a `Verification:` section. Commit only when asked.
