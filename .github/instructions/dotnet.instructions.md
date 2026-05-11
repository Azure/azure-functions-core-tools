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
  `internal sealed class Foo(IBar bar) : IFoo`. The same applies to non-DI
  classes (exceptions, builders) where a single ctor + field/property
  initializers can be collapsed.
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

Throw the **most specific** exception type at the failure site. Services and
helpers shouldn't shape exceptions around CLI presentation; commands are the
boundary that decides what's user-facing.

- Framework / domain exceptions in services / helpers for known failure modes:
  - `FileNotFoundException` / `DirectoryNotFoundException` for missing paths.
  - `InvalidOperationException` for "operation isn't valid in current state".
  - Domain exceptions like `InvalidWorkloadException` for subsystem-specific
    validation failures.
  - `ArgumentNullException` / `ArgumentException` for programmer errors on
    method contracts.
- **Commands wrap.** A command knows which exceptions from its dependencies
  are user errors. Catch those at the command and wrap in
  `GracefulException(message, isUserError: true)` (preserve the inner
  exception). Anything you don't catch is a runtime bug and surfaces with a
  stack trace.
  - Keep the `try` **narrow**, ideally one method call. A wide `try` makes
    a generic catch (e.g. `FileNotFoundException`) ambiguous about which
    call site produced it. A one-call `try` plus the `<exception>` tags
    on that method's xmldoc fully define what each catch arm means.
  - Catching framework types (`FileNotFoundException`,
    `InvalidOperationException`, ...) is fine **only when** the `try` is
    narrow and the called method's xmldoc documents that type as a
    user-facing failure mode. Otherwise promote the failure to a domain
    exception so the catch is unambiguous.
- `GracefulException` (`Abstractions/Common/`) is the CLI-facing wrapper.
  `Program.Main` only catches `GracefulException`. Do **not** add broad
  framework types to the top-level catch list, that hides bugs as user
  errors.
- Don't call `Environment.Exit` from inside a command.
- Default to rethrowing with context or converting to a more specific
  domain exception. Swallowing is allowed only for genuine best-effort
  cleanup paths (e.g. removing a leftover temp directory after another
  failure), and the `catch` must include a comment explaining why losing
  the exception is acceptable. Never `catch (Exception)` in business
  logic.

## Logging vs user output

- `Microsoft.Extensions.Logging` with **structured templates**:
  `_logger.LogInformation("Loaded workload {WorkloadId}", id);` — never string
  interpolation.
- For user-facing CLI output, use `IInteractionService`, not `ILogger`.

## Modern C# constructs

These are enforced by `.editorconfig` (see IDE0007/0008/0090/0161/0200/0290 and
IDE0300-0305). Write them this way the first time so the linter never has to
fire:

- **Collection expressions**: `[]` not `Array.Empty<T>()`; `[a, b]` not
  `new[] { a, b }`; `[]` not `new List<T>()`; `[.. source]` not
  `source.ToArray()`.
- **Target-typed `new()`** when the LHS type is written: `Grid grid = new();`
  not `var grid = new Grid();` or `Grid grid = new Grid();`. Pairs with the
  `var` rule below: write the type once on the left, infer it on the right.
- **Method groups** over wrapper lambdas: `SetAction(ExecuteAsync)` not
  `SetAction((p, ct) => ExecuteAsync(p, ct))`.
- **File-scoped namespaces** in new files.
- **`var`** only when the RHS makes the type obvious (`new T(...)`, a cast,
  a literal). When the type is hidden behind a method or property call, write
  the type explicitly. Tests opt out of this one in `.editorconfig` to reduce
  rename churn.

## Style

- Match the style of the file you're editing.
- No unused `using` directives.
- Don't run `dotnet format` across unrelated files; no drive-by reformatting.
- Don't disable analyzers to silence warnings, fix the underlying issue.

## Code comments

Comment to explain *why*, not *what*. XML doc summaries
are one or two sentences; reach for `<remarks>` only when there's a single
non-obvious clarification, and avoid stacking `<para>` blocks. Don't cite
spec sections or document URLs from inside code (`§6.2`,
`workload-package-layout §5.4`). Don't justify naming choices, list
convention origins ("matches tsconfig.json…"), or narrate the alternative
you didn't pick. Skip comments that restate the next line (DI registrations,
obvious defaults, "Empty when no X" on a collection). Lead with the
operative verb. Keep pointers to follow-up issues, cross-platform quirks,
and rationale that isn't visible from the surrounding code.

## Code comments

Comment to explain *why*, not *what*. XML doc summaries
are one or two sentences; reach for `<remarks>` only when there's a single
non-obvious clarification, and avoid stacking `<para>` blocks. Don't cite
spec sections or document URLs from inside code (`§6.2`,
`workload-package-layout §5.4`). Don't justify naming choices, list
convention origins ("matches tsconfig.json…"), or narrate the alternative
you didn't pick. Skip comments that restate the next line (DI registrations,
obvious defaults, "Empty when no X" on a collection). Lead with the
operative verb. Keep pointers to follow-up issues, cross-platform quirks,
and rationale that isn't visible from the surrounding code.

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
