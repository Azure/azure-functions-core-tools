# Repository Structure

This document describes the Azure Functions Core Tools v5 codebase — projects, build system, CI, and how everything fits together.

## Overview

The v5 CLI is built with [System.CommandLine](https://github.com/dotnet/command-line-api) and [Spectre.Console](https://spectreconsole.net/), targeting **.NET 10**. It follows a **workload model** (similar to `dotnet` CLI) where the base CLI provides core commands and infrastructure, while language-specific functionality (init, templates, etc.) is delivered via independently installable workload packages.

## Project Layout

```
azure-functions-core-tools/
├── src/
│   ├── Func.Cli/                      # Main CLI executable (assembly: func)
│   ├── Func.Cli.Abstractions/         # Shared interfaces for workload authors
│   └── Func.Cli.Workload.Dotnet/      # .NET workload (separate branch)
├── test/
│   ├── Func.Cli.Tests/                # CLI unit tests
│   └── Func.Cli.Workload.Dotnet.Tests/ # Workload tests (separate branch)
├── eng/
│   ├── build/                         # MSBuild props/targets
│   │   ├── Packages.props             # Central package version pins
│   │   └── Engineering.targets        # Shared build targets
│   └── ci/                            # Azure Pipelines YAML
│       ├── cli-public-build.yml        # CLI PR validation + nightly
│       ├── cli-official-build.yml     # CLI release builds
│       ├── abstractions-public-build.yml  # Abstractions PR validation
│       ├── abstractions-official-build.yml # Abstractions release + NuGet pack
│       └── workload-dotnet-build.yml  # Dotnet workload CI
├── docs/                              # Developer documentation
├── out/                               # Build artifacts (gitignored)
└── nupkg/                             # NuGet package output (gitignored)
```

## Projects

### `src/Func.Cli` — The CLI Executable

The main tool users install and run. Assembly name is `func`.

| Directory         | Purpose |
|-------------------|---------|
| `Commands/`       | All CLI commands (init, new, pack, start, version, help, workload) + ProjectDetector |
| `Console/`        | `IInteractionService` abstraction + Spectre.Console implementation |
| `Workloads/`      | Workload manager, manifest, update checker |
| `Telemetry/`      | OpenTelemetry client + Azure Monitor exporter |
| `Common/`         | Shared utilities (Constants, VersionChecker) |
| `Program.cs`      | Entry point — DI container, command tree, error handling |
| `Parser.cs`       | Command tree composition — registers all commands, wires help |

### `src/Func.Cli.Abstractions` — Workload Contracts

A packable NuGet library (`Azure.Functions.Cli.Abstractions`) that defines the interfaces workloads must implement. Workload authors reference this package — they never reference `Func.Cli` directly.

Key types:
- **`IWorkload`** — Main workload entry point (id, name, register commands, provide templates, initializer, and pack provider)
- **`ITemplateProvider`** — Provides function templates for `func new`
- **`IProjectInitializer`** — Handles `func init` for a worker runtime
- **`IPackProvider`** — Handles `func pack` build/publish for a worker runtime
- **`FunctionTemplate`** / **`FunctionScaffoldContext`** / **`ProjectInitContext`** / **`PackContext`** — Data records passed to workloads
- **`GracefulException`** — User-friendly exception with optional verbose detail and exit codes

### `src/Func.Cli.Workload.Dotnet` — .NET Workload

> Lives on the `feature/dotnet-workload` branch and is independently versioned and released.

Implements `IWorkload` for .NET (C#, F#). Delegates to `dotnet new` for project and function scaffolding. See [building-a-workload.md](building-a-workload.md) for details.

## Build System

| File | Purpose |
|------|---------|
| `global.json` | Pins .NET SDK 10.0.100, allows prerelease |
| `Directory.Build.props` | Defines `RepoRoot`, `ArtifactsPath=out/`, publish/package paths |
| `Directory.Build.targets` | Imports `eng/build/Engineering.targets` |
| `Directory.Packages.props` | Enables Central Package Management, imports `eng/build/Packages.props` |
| `eng/build/Packages.props` | Pins all NuGet package versions centrally |
| `eng/build/Telemetry.props` | Injects telemetry instrumentation key as assembly attribute |
| `src/Directory.Build.props` | Sets `IsPackable=true`, package output to `nupkg/` |

Each project also has its own `Directory.Version.props` for independent versioning.

### Build Commands

```bash
dotnet restore                          # Restore packages
dotnet build                            # Build all projects in the solution
dotnet test                             # Run all tests
dotnet publish src/Func.Cli/            # Publish CLI for distribution
```

### Output Structure

```
out/
├── bin/          # Build output (per-project subdirectories)
├── pub/          # Publish output
└── pkg/          # Package output
```

## CI Pipelines

Each project has its own CI pipeline with **path-scoped triggers**, enabling independent build and release cadences.

| Pipeline | File | Triggers On |
|----------|------|------------|
| CLI public build | `eng/ci/cli-public-build.yml` | PRs, nightly schedule |
| CLI official build | `eng/ci/cli-official-build.yml` | Release branches |
| Abstractions public | `eng/ci/abstractions-public-build.yml` | PRs touching `src/Func.Cli.Abstractions/` |
| Abstractions official | `eng/ci/abstractions-official-build.yml` | Release builds, NuGet pack |
| Dotnet workload | `eng/ci/workload-dotnet-build.yml` | Changes to `src/Func.Cli.Workload.Dotnet/` |
| Code mirror | `eng/ci/code-mirror.yml` | Mirror to engineering repo |

## Test Patterns

- **Framework**: xUnit + NSubstitute
- **`TestInteractionService`**: In-memory `IInteractionService` that captures all output and returns deterministic values for prompts
- **Temp directories**: Workload/manager tests create temp dirs and clean up via `IDisposable`
- **`FakeDotnetCliRunner`**: (workload branch) Simulates `dotnet` CLI responses without invoking dotnet

```bash
dotnet test                             # All projects
dotnet test test/Func.Cli.Tests/        # CLI tests only
```

## Branching Strategy

| Branch | Purpose |
|--------|---------|
| `vnext` | Base branch for v5 development |
| `feature/vnext-init` | CLI framework, abstractions, and infrastructure |
| `feature/dotnet-workload` | .NET workload (branches from vnext-init) |
| `feature/<lang>-workload` | Future language workloads |
