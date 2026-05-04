---
applyTo: "**/*.cs"
description: ".NET / C# coding rules enforced everywhere in this repo."
---

# .NET / C# Instructions

These rules apply to every C# file in this repo. They duplicate the
**Design Principles** and **.NET / C# Practices** sections of `AGENTS.md` so
GitHub Copilot loads them automatically when editing C# files. If something
here disagrees with `AGENTS.md`, **`AGENTS.md` wins**.

## DI and composition

- Use **primary constructor syntax** for DI:
  `internal sealed class Foo(IBar bar) : IFoo`.
- Validate non-nullable constructor dependencies with `ArgumentNullException`:
  `private readonly IBar _bar = bar ?? throw new ArgumentNullException(nameof(bar));`.
- No service locators, no `IServiceProvider.GetService` outside the composition
  root, no property injection, no Autofac.
- Bind config via `IOptions<T>` / `IOptionsMonitor<T>`. Do not read
  `IConfiguration` from business logic.
- Lifetimes: prefer Singleton, then Scoped, then Transient.

## No new static classes

- Do not introduce new `static` classes for behavior.
- Static is acceptable only for: pure utility functions, constants, extension
  method holders, and `Program.Main`.
- Anything touching I/O, the filesystem, env vars, the network, the clock, or
  process state lives behind an interface (or abstract base, or virtual on a
  concrete class) so tests can substitute it.

## Mockability

The goal is substitutability in tests, not interfaces for their own sake. Pick
the lightest option:

- **Interface** when there are (or could be) multiple implementations or the
  type crosses a boundary.
- **Abstract base class** when implementations share state / template-method
  behaviour.
- **Concrete class with `virtual` members** when there's one real
  implementation but tests need to override one or two seams.

## Visibility

- New types are `internal` by default. Widen only with a documented reason.
- Use `InternalsVisibleTo` for test access; don't make types `public` just to
  test them.

## Async / await

- Return `Task` / `Task<T>`. Use `ValueTask` / `ValueTask<T>` only when the
  operation is primarily synchronous but may sometimes go async.
- Every async method takes a `CancellationToken` and passes it through.
- Do **not** add `ConfigureAwait(false)` in CLI/app code.
- Never `.Result` / `.Wait()`. Never `async void` (except event handlers).

## Console / CLI I/O

- Do **not** call `Console.WriteLine`, `Console.Error.WriteLine`,
  `AnsiConsole.*` static helpers, `Console.ReadLine`, or `Console.ReadKey` from
  product code.
- Depend on **`IInteractionService`** (`src/Func/Console/`) for all user-facing
  output and prompts.
- Don't hard-code Spectre markup like `[red]...[/]`. Pull colour/icon/style
  through `ITheme`.

## Error handling

- Throw `GracefulException` (`Abstractions/Common/`) for **expected,
  user-facing** errors. `Program.Main` catches it, prints the message, and
  returns a non-zero exit code without a stack trace.
- Don't call `Environment.Exit` from inside a command.
- Use specific framework exceptions (`ArgumentNullException`,
  `InvalidOperationException`, ...) for programmer errors. Don't swallow
  exceptions silently.

## Logging vs user output

- `Microsoft.Extensions.Logging` with **structured templates**:
  `_logger.LogInformation("Loaded workload {WorkloadId}", id);` — never string
  interpolation.
- For user-facing CLI output, use `IInteractionService`, not `ILogger`.

## Style

- **File-scoped namespaces** in new files.
- Match the style of the file you're editing.
- No unused `using` directives.
- Don't run `dotnet format` across unrelated files; no drive-by reformatting.
- Don't disable analyzers to silence warnings, fix the underlying issue.

## XML doc summaries

`<summary>` and `</summary>` go on their own lines, even for one-liners:

```csharp
// Good
/// <summary>
/// Reads the global manifest, returning an empty one if it doesn't exist.
/// </summary>

// Bad
/// <summary>Reads the global manifest, returning an empty one if it doesn't exist.</summary>
```
