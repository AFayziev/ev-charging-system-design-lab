# C# coding conventions

Applies to handwritten C# in application hosts, shared libraries, and tests.
Based on [Microsoft's C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions),
with repository-specific choices below. Framework contracts and generated code
retain their prescribed names and signatures.

## Organization and intent

- Use [domain terminology](domain.md) consistently; name by responsibility.
- Match filenames to the principal type. Keep one principal type per file;
  small related private types may remain together.
- Comments explain intent, constraints, or surprising behavior. Document public
  contracts when their units, invariants, or failure semantics need explanation.

## Types and dependencies

- Keep types and methods focused; prefer composition to inheritance.
- Default implementation types to `internal`; expose only required contracts.
- Use records for immutable data/messages and classes for identity or mutable
  lifecycle. Prefer immutable collections where practical.
- Use nullable annotations honestly. Validate boundaries; avoid `null!` and `!`
  unless an invariant is proven and cannot be expressed in the type system.
- Use constructor injection. Resolve dependencies in composition roots; avoid
  service location inside business logic and manually building service providers.
- Register shared capabilities through conventional `Add...` extension methods;
  name HTTP pipeline extensions `Use...` and endpoint extensions `Map...`.
- Bind related settings to typed options and validate required settings at startup.
- Introduce interfaces at meaningful dependency boundaries, not for every class.
  Keep persistence and transport concerns out of domain behavior.
- Prefer stable framework/library capabilities to custom infrastructure. Avoid
  generic repositories or abstraction layers that only forward calls.

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
- Make culture explicit for persisted/protocol values; use ordinal comparison
  for identifiers unless a contract defines otherwise.

## Errors and diagnostics

- Represent expected business outcomes explicitly; use exceptions for exceptional
  failures. Validate external input at the boundary.
- Catch exceptions only to handle, translate, or add useful context. Preserve
  the original exception as `InnerException` when translating it.
- Preserve cancellation as cancellation. Do not convert it into success or retry
  it as a transient dependency failure.
- Choose meaningful diagnostic properties. Log failures at the boundary
  responsible for handling them.
- Exclude credentials, tokens, signed URLs, and personal payloads from logs.
- Use standard tracing and health facilities; follow the
  [integration contracts](integrations.md) for effect retries and reconciliation.

## Tests and enforcement

- Name tests by behavior and expected outcome. Arrange, act, assert; verify
  observable results rather than private implementation details.
- Keep tests deterministic with controlled time, explicit cancellation/timeouts,
  and isolated data. Avoid timing sleeps and order-dependent tests.
- Use unit tests for logic and integration tests for real dependency contracts;
  do not mock away the behavior an integration test is intended to verify.
- [.editorconfig](../.editorconfig) defines formatting and naming.
  [Directory.Build.props](../Directory.Build.props) enables SDK analyzers and
  build-visible style warnings. Design guidelines require code review.
- Keep builds free of compiler/analyzer warnings. Suppress diagnostics narrowly
  with a concrete reason; avoid blanket project-wide suppression.

```sh
dotnet format style EvChargingSystemDesign.slnx --no-restore --diagnostics IDE0005 --severity info
dotnet format EvChargingSystemDesign.slnx --no-restore
dotnet format style EvChargingSystemDesign.slnx --no-restore --diagnostics IDE0005 --severity info --verify-no-changes
dotnet format EvChargingSystemDesign.slnx --no-restore --verify-no-changes
dotnet build EvChargingSystemDesign.slnx --no-restore
```
