# Backend development guidelines

Design, implementation, and review guidance for backend hosts, shared libraries,
and tests. C# coding style follows
[Microsoft's conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
with repository-specific choices below. Framework contracts and generated code
retain their prescribed names and signatures.

## Organization and intent

- Use [domain terminology](domain.md) consistently; name by responsibility.
- Default to one principal type per file, named after that type;
  small related private types may remain together. Feature slices follow the
  grouping rules below.
- Comments explain intent, constraints, or surprising behavior. Document public
  contracts when their units, invariants, or failure semantics need explanation.
- Separate distinct logical steps with one blank line; keep closely related
  statements together.
- Extract long loop expressions into descriptive local variables before the loop.
  Preserve evaluation order and deferred enumeration.
- Start a wrapped call chain with its receiver or its first call; avoid grouping
  several initial calls on that line.
- Keep expression lambdas on one line when they fit within the configured line
  length; wrap the surrounding argument list independently.
- Keep interpolated expressions on one line. Split long interpolated strings into
  concatenated fragments; extract long expressions into local variables. Preserve
  string contents and interpolated-string handler conversions.
- In multiline class and record constructor signatures, use one parameter per
  line even when multiple parameters would fit on a continuation line.

## Types and dependencies

- Keep types and methods focused; prefer composition to inheritance.
- Organize application use cases under `Features/<Area>`, with query handlers for
  reads and command handlers for writes. Namespaces follow the directory layout.
  Shared infrastructure stays outside `Features`. Keep HTTP binding and response
  handling at the endpoint boundary.
- Keep small feature slices in a file named after the use case, grouping its
  request/response types, child contract types, exclusive errors, and command/query
  handler. Mapping extensions may live in separate files. Request/response models
  used by one handler stay with that handler; models reused by multiple handlers
  in the same feature may live in a dedicated feature-local file. Keep models out
  of mapping files and avoid copying shared definitions. Shared errors stay in
  their owning shared area. Split a slice when its complexity warrants it; group
  code that changes together.
- Place contracts in a shared project only when they have actual cross-project
  consumers; keep contracts owned by a single host with its feature.
- Keep Minimal API route mappings and their HTTP handlers together in the
  feature's endpoint file. Keep command/query handlers separate.
- Commands and queries return typed results for expected failures. Reuse common
  application errors for missing resources and invalid input; reserve feature-specific
  errors for business rules. Translate errors to HTTP responses at the endpoint
  boundary through shared mapping extensions. Keep exceptions for unexpected
  failures and preserve cancellation.
- Use method syntax for LINQ.
- Follow [Domain models, persistence, and validation](#domain-models-persistence-and-validation)
  when creating or changing business entities and their handlers.
- Keep API contracts and derived read models separate from business entities.
  Map between distinct models explicitly through extension methods such as
  `ToResponse`, `ToEntity`, and `ToDomain` where those representations are needed.
- Default implementation types to `internal`; expose only required contracts.
- Use records for immutable data/messages and classes for identity or mutable
  lifecycle. Prefer immutable collections where practical.
- Use named, strongly typed request and response models for API payloads; avoid
  anonymous objects, `dynamic`, and dictionaries for fixed contracts. Keep transport
  models separate from persistence entities. Reuse framework models where appropriate.
  For Minimal APIs, use `TypedResults` and explicit `Results<T1, ...>` unions for
  multiple outcomes. Simple route and query parameters may remain typed scalars.
- Use nullable annotations honestly. Validate boundaries; avoid `null!` and `!`
  unless an invariant is proven and cannot be expressed in the type system.
- Use constructor injection. Resolve dependencies in composition roots; avoid
  service location inside business logic and manually building service providers.
- Register shared capabilities through conventional `Add...` extension methods;
  name HTTP pipeline extensions `Use...` and endpoint extensions `Map...`.
- Bind related settings to typed options and use the framework's Options validation
  at startup. Do not duplicate it with manual validation in factories or helpers.
- Keep options types limited to settings and validation metadata. Construct
  dependencies in registration code.
- Missing or invalid required configuration must fail explicitly, including in
  design-time tooling. Do not substitute connection strings, credentials, or
  dependency endpoints. Defaults belong only to intentionally optional settings.
- Introduce interfaces at meaningful dependency boundaries, not for every class.
  Keep persistence and transport concerns out of domain behavior.
- Avoid generic repositories or abstraction layers that only forward calls.

## Domain models, persistence, and validation

### Model ownership

- Default to one business entity in `EvCharging.Domain`, persisted directly by
  EF Core. Keep mappings, indexes, conversions, constraints, and database access
  in `EvCharging.Persistence`; Domain has no EF Core dependency.
- Introduce separate domain and storage classes only for a concrete representation
  mismatch, such as reconstructing one business object from a different storage
  structure. A project boundary alone is insufficient. Do not maintain duplicate
  property bags with `ToDomain`/`ToEntity` round trips.
- Keep infrastructure records in their owning persistence area when they have no
  business behavior. Audit storage and provider identifier mappings do not need
  artificial domain counterparts.
- Model lifecycle entities as classes with business operations. Simple catalog
  CRUD may retain ordinary setters; it does not require an aggregate hierarchy,
  factories, or domain events merely to satisfy a pattern.
- Introduce value objects for meaningful identity-free concepts with equality or
  invariants. Cross-field validation alone does not require a value object or
  policy class. Separate policy types are useful for rules involving multiple
  objects; handlers must use applicable policies rather than leave them test-only.

### Construction and state changes

- Use private setters for state governed by business rules, including lifecycle
  status, ownership, and mutually dependent values. Expose named operations such
  as `Book`, `Cancel`, or `Complete`, rather than general-purpose state setters.
- Creation validates invariants and returns a valid entity or typed errors.
  Transitions check the current state and required facts before mutation; rejected
  operations leave state unchanged. Commands use these operations rather than
  duplicate their rules or assign properties directly.
- Use an EF-compatible constructor, normally a private parameterless constructor
  alongside the business creation operation. Materialization restores stored
  state; it must not rerun current-time creation rules or trigger side effects.
  Private setters permit normal EF materialization and generated keys.
  See [EF constructor support](https://learn.microsoft.com/en-us/ef/core/modeling/constructors).
- Prefer supported EF mapping to weakening encapsulation. Use
  [backing fields](https://learn.microsoft.com/en-us/ef/core/modeling/backing-field)
  when needed; do not add public constructors, mutable state, reflection helpers,
  or attachment methods solely for persistence or tests.
- Business operations receive required facts and an explicit current instant.
  They do not query a database, read ambient HTTP identity, obtain the system
  clock, call providers, or publish messages. Handlers supply these inputs.
- Keep foreign keys and navigations consistent when an operation assigns a
  relationship. Entity getters must not fetch related data implicitly.
- Do not add lifecycle operations before their feature needs them. When that
  feature arrives, connect its runtime handler to the domain operation and remove
  competing implementations of the same rule.

### Validation boundaries

| Boundary | Responsibility | Examples |
|---|---|---|
| Request binding and validation | Transport shape and field-level contract requirements | Parse identifiers/timestamps; required text; field length |
| Command/query handler | Authorization, resource lookup, and orchestration using loaded facts | Resolve the current driver; enforce station assignment; load a connector |
| Entity, value object, or policy | Invariants and business eligibility shared by all callers | Valid booking interval; allowed lifecycle transition; expiry eligibility |
| Database | Integrity under concurrent writers and non-application writes | Foreign keys; uniqueness; valid stored ranges; overlapping reservation exclusion |

- Assign each rule one application owner. A timestamp ordering check may belong
  to a request contract; when a booking interval must always be valid across
  callers, its invariant belongs to that domain concept. Do not classify every
  comparison as domain behavior or repeat the same rule at every layer.
- Frontend validation gives immediate feedback; backend validation remains
  authoritative. Database enforcement of the same integrity invariant is
  intentional protection against races, not a replacement for useful application
  errors.
- Translate domain errors to request fields and HTTP outcomes in the application
  layer. Domain errors have no dependency on transport property names or status
  codes. Unexpected infrastructure failures remain exceptions.

### Persistence and verification

- Handlers load entities, invoke business operations, and persist accepted changes.
  The shared persistence pipeline captures ordinary change audits and commits them
  atomically. Handlers coordinate durable effects and own explicit transactions
  when a use case spans several saves. Query handlers may project directly to
  response/read models without constructing aggregates.
- Application checks cannot establish cross-request exclusivity. Use appropriate
  database constraints or concurrency control and translate only recognized
  conflicts to expected results. See [EF concurrency handling](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
- Check the installed EF/provider capabilities before choosing a value-object
  mapping. Prefer supported mappings that preserve required indexes and constraints;
  mapped private-set scalars with a derived domain value are acceptable when
  nested mapping cannot express those requirements. Do not store two mutable
  representations of the same value.
- Unit tests cover creation, transitions, rejected-operation state, and business
  boundaries. Integration tests cover meaningful constraints, competing writers,
  authorization, and atomic persistence of related effects. Do not expose mutation
  hooks for tests or add tests solely to prove EF can use private setters.
- Review changes for direct state assignments, duplicated rules, unused policies,
  unnecessary model copies, and race-prone application-only checks. New lifecycle
  features are incomplete when their runtime path bypasses the governing model.

### Identifiers

| Identifier role | Convention |
|---|---|
| Application-owned primary/foreign keys | UUIDv7 |
| Public resource identifiers | Separate, stable UUIDv4 |
| Framework/provider-owned records | Prescribed key types |

- Map GUID-valued properties as `Guid` in EF Core entities, not `string`.
- Use EF Core/provider generation for primary keys when it meets the requirements.

## Async, resources, and data

- Use asynchronous I/O throughout a call path. Avoid `.Result`, `.Wait()`,
  `async void` outside event handlers, and `Task.Run` around I/O.
- Make I/O and long-running operations cancellable. Link cancellation with
  timeouts for bounded external work.
- Await started work; background work belongs to hosted services or durable jobs.
- Use `Task` by default. Use `ValueTask` only for a measured benefit or a required
  contract.
- Use `using`/`await using` for owned disposable resources. Do not dispose
  injected services; obtain HTTP clients through the configured client factory.
- Use `TimeProvider` for testable clocks and delays. Represent instants in UTC
  with `DateTimeOffset`, durations with `TimeSpan`, and calendar dates with `DateOnly`.
- Use `decimal` for monetary amounts, explicit currency and units, and the
  precision rules in the domain/integration contracts.
- Use parameterized queries. Materialize sequences deliberately; avoid hidden
  multiple enumeration and unbounded result sets. Keep transactions short and
  do not concurrently use one EF Core `DbContext`.
- Never use lazy loading. Load related data explicitly with projections,
  eager loading, or explicit loading; do not enable lazy-loading proxies or inject
  lazy loaders into entities.
- Keep EF migrations, snapshots, design-time factories, and design-time packages
  in the corresponding `.Migrations` project. Generate migrations and snapshots
  with `dotnet ef`. Add migration-specific SQL only for requirements the generated
  migration does not cover.
- Make culture explicit for persisted/protocol values; use ordinal comparison
  for identifiers unless a contract defines otherwise.

## Errors and diagnostics

- Represent expected business outcomes explicitly; use exceptions for exceptional
  failures. Validate external input at the boundary.
- Catch exceptions only to handle, translate, or add useful context. Preserve
  the original exception as `InnerException` when translating it.
- Translate infrastructure exceptions to HTTP responses centrally through
  ASP.NET Core exception handlers, rather than repeating catches in endpoints.
- Preserve cancellation as cancellation. Do not convert it into success or retry
  it as a transient dependency failure.
- Choose meaningful diagnostic properties. Log failures at the boundary
  responsible for handling them.
- Exclude credentials, tokens, signed URLs, and personal payloads from logs.
- Use standard tracing and health facilities; follow the
  [integration contracts](integrations.md) for effect retries and reconciliation.

## Tests and enforcement

- Use `MethodUnderTest_Scenario_ExpectedResult` for test names, following
  [Microsoft's testing guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices#naming-your-tests).
  Use the operation name for tests spanning components; keep each part concise.
  Arrange, act, assert; verify observable results rather than private details.
- Keep tests deterministic with controlled time, explicit cancellation/timeouts,
  and isolated data. Avoid timing sleeps and order-dependent tests.
- Use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` for
  test clocks instead of custom `TimeProvider` implementations.
- Use unit tests for logic and integration tests for real dependency contracts;
  do not mock away the behavior an integration test is intended to verify.
- Do not add tests or custom probes solely to confirm framework configuration,
  dependency-injection registrations, built-in telemetry, or basic framework
  behavior such as ORM CRUD and rerunning unchanged migrations. Test application
  behavior and data/security contracts; use existing diagnostics to inspect wiring.
- [.editorconfig](../.editorconfig) defines formatting and naming.
  [Directory.Build.props](../Directory.Build.props) enables SDK analyzers and
  build-visible style warnings. Design guidelines require code review.
- Keep builds free of compiler/analyzer warnings. Suppress diagnostics narrowly
  with a concrete reason; avoid blanket project-wide suppression.
