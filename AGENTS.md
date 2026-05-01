# Agent Instructions

## Repository Overview

Azure Functions Core Tools v5 CLI — rebuilt with System.CommandLine, Spectre.Console,
and .NET 10 using a workload-based extensibility model (similar to `dotnet` CLI workloads).

## Design Principles

Guidance for any code added in this repo. v5 is a deliberate restructure away from
the legacy patterns in `main`; keep the bar high.

### DI-first

- All composition goes through `Microsoft.Extensions.DependencyInjection`. Build the
  service collection at startup; do not introduce service locators or ambient
  containers (the legacy Autofac container in `main` is being phased out — do not
  bring it forward).
- Inject dependencies via **constructor injection**. No property injection, no
  service-locator lookups in business logic, no `IServiceProvider.GetService` calls
  outside the composition root.
- Prefer `IOptions<T>` / `IOptionsMonitor<T>` for configuration, bound from
  `IConfiguration` at the composition root.
- Pick the narrowest sensible lifetime: `Singleton` for stateless/shared,
  `Scoped` for per-command/per-request work, `Transient` only when justified.

### Avoid static classes

- Do **not** introduce new `static` classes for behavior. Static state and static
  helpers are the main reason `main` is hard to test.
- Static is acceptable only for:
  - Pure, side-effect-free utility functions with no dependencies that would
    otherwise be injected.
  - Constants and `extension` method holders.
  - `Program.Main`.
- Anything that touches I/O, the filesystem, the environment, the network, the
  clock, or process state must be a regular class behind an interface (or abstract
  base) so it can be substituted in tests.

### Testability is non-negotiable

- Every new class must be reachable from a test — through an interface, a concrete
  type with injectable dependencies, an `IOptions<T>`, or similar. If you can't see
  how to test it, the design is wrong; restructure before adding more code.
- Wrap non-deterministic dependencies (clock, environment variables, filesystem,
  process launching, HTTP) behind interfaces. Do not call `Environment.*`,
  `File.*`, `DateTime.Now`, `Process.Start`, etc. directly from business logic.
- Keep methods focused and side-effect-light so they can be unit-tested without
  spinning up the full CLI.

### Visibility

- New types are **`internal`** by default. Only make a type `public` when there is
  a clear, documented reason (e.g. it is part of a published API surface). Prefer
  `InternalsVisibleTo` for test access over widening visibility.
- Same rule for members: prefer `private` / `internal`, widen only when needed.

### Style

- Match the style of the file you're editing.
- Prefer **file-scoped namespaces** in new files.
- Don't leave unused `using` directives.
- Don't run `dotnet format` across unrelated files or make drive-by formatting
  changes.
- Follow the existing `stylecop.json` / analyzer rules. Don't disable analyzers to
  silence warnings — fix the underlying issue.
- Keep changes minimal and surgical. User-visible behavior changes must be flagged
  explicitly in the PR description.

## Procedures

Multi-step playbooks live as skills under `.github/skills/`. Use them when the
task matches:

- **Adding a new CLI command** → `add-command` skill.
- **Creating a new workload** (Node, Python, Java, ...) → `create-workload` skill.

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

All reusable playbooks live in `.github/skills/<skill-name>/SKILL.md` (per the
[Agent Skills](https://agentskills.io) spec). Claude Code picks them up via
symlinks under `.claude/skills/`. Available skills:

- `add-command` — adding a new CLI command.
- `create-workload` — scaffolding a new workload (Node, Python, Java, ...).
- `dotnet-best-practices` — coding patterns to apply.
- `dotnet-design-pattern-review` — review checklist.

Where any skill and this file disagree, **AGENTS.md wins** for this repository.
