# Agent Instructions

## Repository Overview

Azure Functions Core Tools v5 CLI — rebuilt with **System.CommandLine**,
**Spectre.Console**, and **.NET 10** using a workload-based extensibility model
(similar to `dotnet` CLI workloads).

**This repo ships a CLI.** The user's terminal is the only entry point: design
for it. No `appsettings.json`, no web-style configuration, no direct
`Console.WriteLine` calls in product code. v5 is a deliberate restructure away
from the legacy patterns in `main`; keep the bar high.

## Design Principles

These apply to **every change** in this repo, not just when explicitly invoked.

### DI-first

- All composition goes through `Microsoft.Extensions.DependencyInjection`. Build
  the service collection at startup; do not introduce service locators or ambient
  containers (the legacy Autofac container in `main` is being phased out, do not
  bring it forward).
- Inject dependencies via **primary constructor syntax**:
  `internal sealed class Foo(IBar bar) : IFoo`.
- Validate non-nullable constructor dependencies with `ArgumentNullException`:

  ```csharp
  private readonly IBar _bar = bar ?? throw new ArgumentNullException(nameof(bar));
  ```

- No property injection, no `IServiceProvider.GetService` calls in business
  logic, no service locators outside the composition root.
- Bind configuration via `IOptions<T>` / `IOptionsMonitor<T>`; do not read
  `IConfiguration` from business logic.
- Pick the narrowest sensible lifetime: `Singleton` for stateless/shared,
  `Scoped` for per-command/per-request work, `Transient` only when justified.

### Avoid static classes

- Do **not** introduce new `static` classes for behavior. Static state and
  static helpers are the main reason `main` is hard to test.
- Static is acceptable only for:
  - Pure, side-effect-free utility functions with no dependencies that would
    otherwise be injected.
  - Constants and `extension` method holders.
  - `Program.Main`.
- Anything that touches I/O, the filesystem, the environment, the network, the
  clock, or process state must be a regular class behind an interface (or
  abstract base) so it can be substituted in tests.

### Mockability (not just interfaces)

The goal is **substitutability in tests**, not interfaces for their own sake.
Pick the lightest tool for the job:

- **Interface (`IFoo`)** when there are (or could be) multiple implementations,
  or when the type crosses a boundary (I/O, network, process, clock, env).
- **Abstract base class** when implementations naturally share state or
  template-method behaviour.
- **Concrete class with `virtual` members** when there is a single real
  implementation but tests need to override one or two seams. Cheaper than
  defining an interface no production code switches on.

Whatever you pick, the type must be reachable from a test without spinning up
the full CLI.

### Testability is non-negotiable

- Every new class must be reachable from a test. If you can't see how to test
  it, the design is wrong; restructure before adding more code.
- Wrap non-deterministic dependencies (clock, environment variables, filesystem,
  process launching, HTTP) behind interfaces. Do **not** call `Environment.*`,
  `File.*`, `DateTime.Now`, `Process.Start`, etc. directly from business logic.
- Keep methods focused and side-effect-light so they can be unit-tested without
  spinning up the full CLI.

### Visibility

- New types are **`internal`** by default. Only make a type `public` when there
  is a clear, documented reason (e.g. it is part of a published API surface).
  Prefer `InternalsVisibleTo` for test access over widening visibility.
- Same rule for members: prefer `private` / `internal`, widen only when needed.

### Style

- Match the style of the file you're editing.
- Prefer **file-scoped namespaces** in new files.
- Don't leave unused `using` directives.
- Don't run `dotnet format` across unrelated files or make drive-by formatting
  changes.
- Follow the existing `stylecop.json` / analyzer rules. Don't disable analyzers
  to silence warnings, fix the underlying issue.
- Keep changes minimal and surgical. User-visible behavior changes must be
  flagged explicitly in the PR description.

## CLI Conventions

This repo ships **one product**: the `func` CLI. Treat it as a CLI first and a
.NET app second.

### Console I/O, never call it directly

Production code must **not** call:

- `Console.WriteLine` / `Console.Error.WriteLine`
- `AnsiConsole.*` static helpers
- `Console.ReadLine` / `Console.ReadKey`

Instead, depend on **`IInteractionService`** (`src/Func/Console/`). The Spectre
implementation (`SpectreInteractionService`) is the production binding; tests
substitute it with NSubstitute. This is what makes commands testable without a
real terminal. If you need a new output primitive, add it to
`IInteractionService` first, then implement it in the Spectre adapter.

### Themes & colour

- Colours, icons, and text styles come from `ITheme`
  (`src/Func/Console/Theme/`). Do not hard-code Spectre markup like
  `[red]...[/]` in commands.
- Respect `NO_COLOR` and non-TTY scenarios. `Spectre.Console` handles most of
  this; don't fight it by emitting raw ANSI.

### Commands

- One class per command in `src/Func/Commands/`, extending `FuncCliCommand`.
  Register in `Parser.cs`. Tests live in `test/Func.Tests/Commands/`.
  See the `add-command` skill for the full checklist.
- Command handlers should be **thin**: parse options into a typed request,
  delegate to a service, render the result via `IInteractionService`. No
  business logic in the handler itself.

### Exit codes

- `0` for success, non-zero for failure.
- Prefer letting `GracefulException` flow up to `Program.Main`; it sets the
  exit code and prints the friendly message without a stack trace.
- Don't call `Environment.Exit` from inside a command, it bypasses cleanup and
  makes commands untestable.

### Cancellation (Ctrl+C)

- Every async path takes a `CancellationToken` and **passes it through**.
- The token is wired up at the parser level and cancels on `SIGINT` / `Ctrl+C`.
- Honour cancellation promptly; surface `OperationCanceledException` as a
  graceful exit, not a stack trace.

### Configuration sources

A CLI's configuration surface is small:

1. **CLI options and arguments** (System.CommandLine), primary.
2. **Environment variables** for per-shell/session knobs.

Do **not** introduce `appsettings.json`, user config files, or layered
`IConfiguration` pipelines. The CLI's only entry point is the user's terminal;
we don't read app-style settings files.

### Help text and UX

- Every command and option needs a one-line description suitable for `--help`.
- Error messages should tell the user **what to do next**, not just what went
  wrong (`Run 'func init' to create a new project.`).
- Prefer subcommands over flag overloading when behaviour diverges
  significantly.

## .NET / C# Practices

### Async / await

- Return `Task` / `Task<T>` from async methods.
- Use `ValueTask` / `ValueTask<T>` when the operation is **primarily synchronous
  but may sometimes go async** (e.g. cached lookups). Don't use it by default;
  the rules around awaiting it twice are subtle.
- Every async method accepts a `CancellationToken` and passes it through.
- Do **not** add `ConfigureAwait(false)` in CLI/app code, there is no sync
  context to capture. Reserve it for library code we ship to other apps.
- Never `.Result` / `.Wait()`. Never `async void` (except event handlers).

### Testing stack

- **xUnit** + **NSubstitute** for unit tests. Use **AwesomeAssertions** for
  fluent assertions (the maintained fork of FluentAssertions, which moved to a
  paid licence). No MSTest, no Moq, no FluentAssertions.
- Common test packages are enforced by `test/Directory.Build.props`, don't
  re-declare them in individual test csprojs.
- Follow AAA (Arrange / Act / Assert).
- Cover both success and failure paths; include argument-validation tests for
  public/`internal` APIs.

### Error handling

- Throw `GracefulException` (from `Abstractions/Common/`) for **expected,
  user-facing** errors. `Program.Main` catches it, prints the friendly message,
  and returns a non-zero exit code without a stack trace.
- Use specific framework exceptions (`ArgumentNullException`,
  `InvalidOperationException`, ...) for programmer errors.
- Don't swallow exceptions silently; if you catch, log and rethrow or convert
  to a `GracefulException` with context.

### Logging

- Use `Microsoft.Extensions.Logging` with structured templates:
  `_logger.LogInformation("Loaded workload {WorkloadId}", id);`. Not string
  interpolation.
- Use scopes for per-command/per-request context.
- For user-facing CLI output, route through `IInteractionService`, not
  `ILogger`.

## Procedures

Multi-step playbooks live as skills under `.github/skills/`. Use them when the
task matches:

- **Adding a new CLI command** → `add-command` skill.
- **Creating a new workload** (Node, Python, Java, ...) → `create-workload`
  skill.
- **Reviewing a design** → `dotnet-design-pattern-review` skill.

## Build & Test Commands

```bash
dotnet restore                    # Restore NuGet packages
dotnet build                      # Build all projects
dotnet test                       # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName"  # Run specific tests
```

## Project Conventions

- **Folder names**: short name structured from source root, with project name matching the folder segments.
  - e.g.: `Func/Func.csproj`, `Abstractions/Abstractions.csproj`, `Workload/<Name>/Workload.<Name>.csproj`.
- **Assembly/namespace names**: omit by default, will be built from project name with a top-level namespace `Azure.Functions.Cli` prefixed by msbuild props.
  - e.g.: `Workload.<Name>.csproj` will automatically become `Azure.Functions.Cli.Workload.<Name>.dll` and namespace of `Azure.Functions.Cli.Workload.<Name>`.
  - Manually setting is only done when that convention is not desired. e.g.: `Func.csproj` manually sets `<AssemblyName>func</AssemblyName>` and `<RootNamespace>$(TopLevelNamespace)</RootNamespace>`.
- **Package IDs**: omit by default, will be based on assembly name.
- **CI pipelines**: `workload-<name>-public-build.yml` and `workload-<name>-official-build.yml`
- **Tests**: xUnit with NSubstitute for mocking, `FakeDotnetCliRunner` pattern for CLI wrappers
- **Error handling**: throw `GracefulException` for user-facing errors (caught in Program.cs)
- **Cancellation**: all async methods accept `CancellationToken`, pass through to child operations
- **XML doc summaries**: always put `<summary>` and `</summary>` on their own lines, even for one-line summaries:

  ```csharp
  // Good
  /// <summary>
  /// Reads the global manifest, returning an empty one if it doesn't exist.
  /// </summary>

  // Bad
  /// <summary>Reads the global manifest, returning an empty one if it doesn't exist.</summary>
  ```

## Reference

Task-triggered playbooks live in `.github/skills/<skill-name>/SKILL.md` (per
the [Agent Skills](https://agentskills.io) spec). Claude Code picks them up
via symlinks under `.claude/skills/`. Available skills:

- `add-command` — adding a new CLI command.
- `create-workload` — scaffolding a new workload (Node, Python, Java, ...).
- `dotnet-design-pattern-review` — design pattern review checklist.

The **Design Principles**, **CLI Conventions**, and **.NET / C# Practices**
sections above are always-on; they are not skills you opt into. Copilot also
auto-loads `.github/instructions/*.instructions.md` based on file paths, see
those files for path-scoped enforcement of the same rules.

Where any skill or path-scoped instruction file disagrees with `AGENTS.md`,
**`AGENTS.md` wins**.
