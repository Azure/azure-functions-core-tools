# Repository Structure

This document describes the Azure Functions Core Tools v5 codebase — projects, build system, CI, and how everything fits together.

## Overview

The v5 CLI is built with [System.CommandLine](https://github.com/dotnet/command-line-api) and [Spectre.Console](https://spectreconsole.net/), targeting **.NET 10**. It follows a **workload model** (similar to the `dotnet` CLI) where the base CLI provides core commands and infrastructure while language-specific functionality (templates, init, pack) is delivered via independently installable workload packages. The base CLI, the host that loads workloads, the public abstractions library, and the `func workload install` / `uninstall` commands are all in the tree today; NuGet feed-based acquisition lands in a follow-up PR.

## Project Layout

```
azure-functions-core-tools/
├── src/
│   ├── Func/                          # Main CLI executable (assembly: func)
│   └── Abstractions/                  # Shared types for workload authors (NuGet)
|   └── Workloads/
|       ├── <Name>/                    # Ungrouped workload projects
|       └── <kind>/
|           └── <Name>/                # Grouped workload projects, e.g. Stacks/Python
├── test/
│   └── Func.Tests/                    # CLI unit tests
|   └── Workloads/
|       ├── <Name>.Tests/              # Ungrouped workload tests
|       └── <kind>/
|           └── <Name>.Tests/          # Grouped workload tests, e.g. Stacks/Python.Tests
├── eng/
│   ├── build/                         # MSBuild props/targets
│   │   ├── Packages.props             # Central package version pins
│   │   ├── Engineering.targets        # Shared build targets
│   │   └── Telemetry.props            # Telemetry key injection
│   └── ci/                            # Azure Pipelines YAML
│       ├── cli-public-build.yml          # CLI PR validation + nightly
│       ├── cli-official-build.yml        # CLI release builds
│       ├── abstractions-public-build.yml # Abstractions PR validation
│       ├── abstractions-official-build.yml # Abstractions release + NuGet pack
│       ├── code-mirror.yml               # Mirror to engineering repo
│       └── templates/                    # Shared pipeline templates
├── docs/                              # Developer documentation
└── out/                               # Build artifacts (gitignored)
```

`Azure.Functions.Cli.slnx` solution folders should mirror the source and test
hierarchy for workload projects. Grouped workloads belong under solution
folders like `/src/Workloads/<kind>/` and `/test/Workloads/<kind>/`; ungrouped
workloads use `/src/Workloads/` and `/test/Workloads/`.

## Projects

### `src/Func` — The CLI Executable

The main tool users install and run. Assembly name is `func`.

| Directory          | Purpose |
|--------------------|---------|
| `Commands/`        | Built-in commands (init, new, pack, start, version, help) + `FuncCliCommand`, `FuncRootCommand`, `SpectreHelpAction`, `WorkloadHints` |
| `Commands/Workload/` | `func workload` parent command + `func workload list` |
| `Console/`         | `IInteractionService` abstraction + Spectre.Console implementation, theme |
| `Common/`          | Shared utilities — `Constants`, `Stacks` (stack registry), `VersionChecker` |
| `Hosting/`         | `DefaultFunctionsCliBuilder` (internal `FunctionsCliBuilder` impl), `BuiltInCommands` DI registrations, and `WorkloadRegistration` (workload bootstrap seam) |
| `Workloads/`       | `ExternalCommand` — internal `FuncCliCommand` adapter that wraps a workload-supplied `FuncCommand` and carries the owning `WorkloadInfo`. `WorkloadDetectorContribution` mirrors the same pattern for `IProjectDetector` registrations. |
| `Workloads/Resolution/` | `IWorkloadResolver` + supporting types (`ILocalSettingsReader`, `IDirectoryMarkerMatcher`) implementing the spec §5.2 resolution algorithm |
| `Workloads/Invocation/` | `IWorkloadInvoker` + `WorkloadProtocolException` implementing the spec §6.3 invocation error contract (id-prefixed messages, "please file an issue" hint for protocol failures) |
| `Telemetry/`       | `CliTelemetry` (ActivitySource + Meter), Azure Monitor exporter wiring, activity / metric extensions |
| `Program.cs`       | Entry point — host, telemetry, command tree, error handling |
| `Parser.cs`        | Command tree composition — resolves every `FuncCliCommand` from DI, partitions into built-ins (`IBuiltInCommand`) and workload-contributed (`ExternalCommand`), wires Spectre help |

### `src/Abstractions` — Workload Contracts

A packable NuGet library (`Azure.Functions.Cli.Abstractions`) hosting the public types workload authors program against. Workload authors reference this package — they never reference `Func` cli directly.

| File / Type | Role |
|-------------|------|
| `Common/GracefulException` | User-friendly exception with optional verbose detail; drives non-zero exit codes |
| `Workloads/IWorkload` | Workload entry point — identity properties + `Configure(FunctionsCliBuilder)` |
| `Workloads/FunctionsCliBuilder` | DI seam (abstract base) — `Services` for plain DI plus `RegisterCommand(...)` overloads for top-level commands |
| `Workloads/FuncCommand` | Parser-independent base for workload-contributed top-level commands (Name, Description, Options, Arguments, Subcommands, ExecuteAsync) |
| `Workloads/FuncCommandOption`, `FuncCommandArgument`, `FuncCommandInvocationContext` | Typed descriptors and the invocation context used by `FuncCommand` |
| `Workloads/IProjectInitializer` | `func init` extension point for a stack |
| `Workloads/WorkloadContext`, `InitContext` | Records carrying inputs to providers |

## Build System

| File | Purpose |
|------|---------|
| `global.json` | Pins .NET SDK 10.0.100, allows prerelease |
| `Directory.Build.props` | Defines `RepoRoot`, `ArtifactsPath=out/`, publish/package paths |
| `Directory.Build.targets` | Imports `eng/build/Engineering.targets` |
| `Directory.Packages.props` | Enables Central Package Management, imports `eng/build/Packages.props` |
| `eng/build/Packages.props` | Pins all NuGet package versions centrally |
| `eng/build/Telemetry.props` | Injects telemetry instrumentation key as assembly attribute |
| `src/Directory.Build.props` | Sets `IsPackable=true`, package output to `out/pkg/` |

Each project also has its own `Directory.Version.props` for independent versioning.

### Build Commands

```bash
dotnet restore                          # Restore packages
dotnet build                            # Build all projects in the solution
dotnet test                             # Run all tests
dotnet publish src/Func/                # Publish CLI for distribution
```

### Output Structure

```
out/
├── bin/          # Build output (per-project subdirectories)
├── pub/          # Publish output
└── pkg/          # Package output
```

## CI Pipelines

Each component has its own CI pipeline with **path-scoped triggers**, enabling independent build and release cadences.

| Pipeline | File | Triggers On |
|----------|------|-------------|
| CLI public build | `eng/ci/cli-public-build.yml` | PRs touching the CLI, nightly schedule |
| CLI official build | `eng/ci/cli-official-build.yml` | Release branches |
| Abstractions public | `eng/ci/abstractions-public-build.yml` | PRs touching `src/Abstractions/` |
| Abstractions official | `eng/ci/abstractions-official-build.yml` | Release builds, NuGet pack |
| Code mirror | `eng/ci/code-mirror.yml` | Mirror to engineering repo |

## Test Patterns

- **Framework**: xUnit + NSubstitute
- **`TestInteractionService`**: In-memory `IInteractionService` that captures all output and returns deterministic values for prompts
- **Temp directories**: Tests that touch the filesystem create temp dirs and clean up via `IDisposable`

```bash
dotnet test                             # All projects
dotnet test test/Func.Tests/        # CLI tests only
```

## Branching Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Stable v4 release branch |
| `vnext` | Base branch for v5 development |
