# How the CLI Works

This document explains the runtime architecture of the Azure Functions Core Tools v5 CLI — how commands are composed, how telemetry is wired, and how the console layer works.

> **Status**: v5 is in active development. As of this PR, the workload abstractions, the DI host that loads workloads, profile inspection, and the `func workload install` / `uninstall` commands are wired into the CLI. Workloads are installed from a local `.nupkg` on disk; NuGet feed acquisition lands in a follow-up. Built-in commands that depend on workload contributions (e.g. `func init`) report "no workloads installed" and exit cleanly until at least one workload is installed.

## Startup Flow

```
Program.cs
  ├── Build IInteractionService (Spectre + DefaultTheme)
  │
  ├── HostApplicationBuilder
  │   ├── Register IInteractionService
  │   ├── Register OpenTelemetry (when a connection string is baked in)
  │   │   ├── ConfigureResource(CliTelemetry.ConfigureResource)
  │   │   ├── WithTracing → AzureMonitorTraceExporter
  │   │   └── WithMetrics → AzureMonitorMetricExporter
  │   └── WorkloadRegistration.RegisterWorkloads(FunctionsCliBuilder)
  │       └── For each installed workload: workload.Configure(workloadBuilder)
  │           (workloadBuilder is scoped to the WorkloadInfo so RegisterCommand
  │            calls tag commands with the owning workload; the builder also
  │            registers IProjectInitializer and any supporting services)
  │
  ├── host.StartAsync()    ← OTel listeners subscribed before any tracing
  │
  ├── Parser.CreateCommand(host.Services)
  │   ├── Build root command
  │   ├── Resolve every FuncCliCommand from DI:
  │   │     ├── Built-in commands (IBuiltInCommand): init, new, start, pack, publish, version, help, profile, workload
  │   │     └── ExternalCommand wrappers (each carries the WorkloadInfo for the
  │   │         workload that called RegisterCommand)
  │   ├── Skip workload commands that collide with built-ins or with each
  │   │   other; emit a warning to stderr that names the offending workload(s)
  │   └── Replace help rendering with Spectre on all commands
  │
  ├── Wire cancellation (Ctrl+C / SIGTERM via CancellationTokenSource)
  ├── Fire background CLI version check (non-blocking)
  │
  ├── Start root command activity (telemetry wraps the entire invocation)
  │
  ├── rootCommand.Parse(args).InvokeAsync()
  │
  ├── Print CLI version upgrade notice (if newer v5 release available)
  │
  ├── Record command metrics (name, exit code, duration)
  │
  ├── host.StopAsync(3s)  ← flushes OTel exporters with a bounded wait
  │
  └── Error handling
      ├── OperationCanceledException → exit 130
      ├── GracefulException → user-friendly error, exit 1
      └── Exception → "unexpected error", exit 1
```

## Command Tree

```
func
├── init [path]           Initialize a new Functions project
├── new [path]            Create a new function from a template
├── start [path]          Start the Functions host (placeholder)
├── profile
│   ├── list [path]       List available profiles
│   ├── show <name> [path]  Show profile details
│   └── set <name> [path]   Set the project default profile
├── quickstart [path]     Scaffold a project from a CDN-hosted template
│   ├── list [path]       List available quickstart templates
│   └── info <id> [path]  Show details for a quickstart template
├── workload
│   └── list              List installed workloads
├── version (hidden)      Print version info
├── pack (hidden)         Stub — directs users to v4 Core Tools
├── publish (hidden)      Stub — directs users to Azure CLI / v4 Core Tools
└── help (hidden)         Print help
```

Workloads can add their own subcommands by calling `builder.RegisterCommand(...)` from inside `IWorkload.Configure` (see [building-a-workload.md](building-a-workload.md)). Workload commands are described in parser-independent terms via `FuncCommand` / `FuncCommandOption` / `FuncCommandArgument`; the host wraps each one in an internal `ExternalCommand` adapter that translates the descriptors to `System.CommandLine` and tracks the registering workload for diagnostics.

## Command Architecture

All commands extend `FuncCliCommand` → `System.CommandLine.Command`.

### FuncCliCommand

Provides shared infrastructure:
- **`SetAction`** wiring — connects `ExecuteAsync` to the command
- **`[path]` argument** — optional via `AddPathArgument()`. Read the resolved value with `parseResult.GetValue(PathArgument!)`, which returns a `WorkingDirectory` the command must thread through its work explicitly. The CLI never mutates `Directory.SetCurrentDirectory`.
- **Parse result access** — commands read option values via `parseResult.GetValue(MyOption)`

### Command Lifecycle

```
1. Constructor
   ├── Set name and description (passed to base Command)
   ├── Define and add Options / Arguments
   └── Call SetAction(handler) to wire the execution method

2. Parser.CreateCommand()
   └── Adds command to root command tree

3. InvokeAsync (when user runs the command)
   └── handler(parseResult, cancellationToken)
       ├── Read option/argument values from parseResult
       ├── Use IInteractionService for prompts and output
       └── Return exit code (0 = success)
```

### Option/Argument Conventions

Options and arguments are defined as instance properties on the command class so each resolved command owns its own option instances (no shared mutable parser state across tests):

```csharp
// Option with short alias and default value
public Option<string> NameOption { get; } = new("--name", "-n")
{
    Description = "The name of the function app project"
};

// Option with default value factory
public Option<string> FrameworkOption { get; } = new("--target-framework")
{
    Description = "The target framework (default: net10.0)",
    DefaultValueFactory = _ => "net10.0"
};

// Positional argument (via FuncCliCommand)
AddPathArgument(); // adds optional [path] argument
```

## Stacks

A "stack" describes a language/runtime target (`dotnet`, `node`, `python`, `java`, `powershell`, `custom`). The `Stacks` registry in `Common/Stacks.cs` is the single source of truth for stack identifiers and the languages each one supports.

- `--stack` / `-s` is the user-facing option on `func init`.
- `IProjectDetector` (registered by a workload through `FunctionsCliBuilder.RegisterDetector`) declares the project markers (e.g. `*.csproj`, `package.json`) and worker-runtime ids the workload claims, and returns a confidence verdict for a given directory. `IWorkloadResolver` runs the spec §5.2 algorithm (selector → `FUNCTIONS_WORKER_RUNTIME` lookup → detector pass) against those contributions to pick the workload that owns a directory.

## Console / UI Layer

**All console I/O goes through `IInteractionService`** — never `Console.Write*` and
never raw Spectre markup (`[red]…[/]`). This enables testability (in-memory
capture), non-interactive mode (prompts return defaults), and theming.

### Theming

A single `ITheme` exposes `Style` values for semantic roles (`Command`,
`Placeholder`, `Path`, `Muted`, `Heading`, `Success`, `Error`, `Warning`, etc.).
`DefaultTheme` holds the production palette. Swapping the implementation is the
only change required to add a no-color or high-contrast variant.

### Output Methods

| Method | Goes To | Purpose |
|--------|---------|---------|
| `WriteLine(text)` | stdout | Plain text |
| `WriteLine(l => l.Muted("Run ").Command("func new"))` | stdout | Composed styled line via `InlineLine` |
| `Write(IRenderable)` | stdout | Spectre `Grid`/`Table`/`Panel` escape hatch |
| `WriteTitle(text)` | stdout | Product / document title |
| `WriteSectionHeader(title)` | stdout | Horizontal rule with title |
| `WriteHint(message)` | stdout | Muted informational text |
| `WriteSuccess(message)` | stdout | `✓ message` in success color |
| `WriteError(message)` | stderr | `Error: message` in error color |
| `WriteWarning(message)` | stderr | `Warning: message` in warning color |
| `WriteDefinitionList(items)` | stdout | Aligned `label    description` rows (Spectre `Grid` — no manual padding) |
| `WriteTable(columns, rows)` | stdout | Bordered data table |
| `WriteBlankLine()` | stdout | Empty line |

### `InlineLine` composition

`WriteLine(Action<InlineLine>)` provides a fluent, theme-aware builder for lines
that mix multiple styles. Role-named methods (`.Command(...)`, `.Placeholder(...)`,
`.Path(...)`, `.Muted(...)`, …) append styled segments. Literal content is escaped
automatically — callers never type `[...]` tags or call `EscapeMarkup()`.

```csharp
interaction.WriteLine(l => l
    .Muted("Run ")
    .Command("func new")
    .Muted(" to create a function."));
```

### Interactive Prompts

| Method | Returns | Non-Interactive Behavior |
|--------|---------|------------------------|
| `Confirm(prompt, default)` | `bool` | Returns `defaultValue` |
| `PromptForSelection(title, choices)` | `string` | Returns first choice |
| `PromptForInput(prompt, defaultValue)` | `string` | Returns `defaultValue` |
| `ShowStatusAsync<T>(message, action)` | `T` | Runs action, prints status text |
| `StatusAsync(message, action)` | — | Runs action, prints status text |
| `RunWithProgressAsync<T>(initialDescription, action)` | `T` | Runs action; prints each description change as a line |

`RunWithProgressAsync` exposes an `IProgressContext` to the action, which long-running operations can use to update the description, set/report numeric progress, or increment. The Spectre implementation renders a live progress bar; in non-interactive mode each description change is written as a separate line so logs remain readable.

### `IsInteractive` Property

Returns `true` when the console supports prompts (not redirected, not CI). Commands should check this before offering interactive flows.

## Workload System

The CLI is designed around a workload model: the base CLI provides core commands and infrastructure, while stack- and feature-specific behavior is delivered by independently installable workloads. Workloads contribute behavior through plain .NET DI.

### Architecture

```
CLI (Func)
  │
  │ references
  ▼
Abstractions (Azure.Functions.Cli.Abstractions)    ← NuGet package
  │
  │ referenced by
  ▼
Workload (e.g. Azure.Functions.Cli.Workloads.Dotnet) ← NuGet package, loaded at runtime
```

The CLI never has a compile-time reference to any workload. Workloads will be loaded dynamically via `AssemblyLoadContext`.

### Extension Points

The abstractions library exposes the surface workload authors program against:

| Type | Role |
|------|------|
| `IWorkload` | Entry point. Identity (`PackageId`, `PackageVersion`, `DisplayName`, `Description`) plus `Configure(FunctionsCliBuilder)`. |
| `FunctionsCliBuilder` | The DI seam handed to a workload. Exposes `Services` for plain DI registrations and `RegisterCommand(...)` overloads (instance / type / factory) for contributing top-level commands. Abstract base so we can grow the surface without breaking workloads. |
| `IProjectInitializer` | Owns `func init` for a stack. Declares `Stack`, `DisplayName`, `SupportedLanguages`, registers init options via `IInitOptionRegistry`, and runs `InitializeAsync(InitContext, ParseResult, CancellationToken)`. |
| `IInitOptionRegistry` / `CommonInitOptions` | The registry de-dupes options that multiple workloads contribute (e.g. `--no-bundles`) so each option appears once in `--help` and every workload reads the same parsed value. `CommonInitOptions` is a small factory of the shared options themselves (`NoBundle`, `BundlesChannel`). |
| `WorkloadContext` / `InitContext` | Records carrying common + command-specific inputs to providers. |
| `FuncCommand` | Parser-independent base for workload-contributed top-level commands. Describes `Name`, `Description`, `Options`, `Arguments`, `Subcommands`, and an `ExecuteAsync(FuncCommandInvocationContext, CancellationToken)`. |
| `FuncCommandOption` / `FuncCommandArgument` | Typed descriptors used by `FuncCommand`. Identity matters — pass the same descriptor instance to `context.GetValue(...)` to read parsed values. |
| `FuncCommandInvocationContext` | Read parsed option / argument values without depending on `System.CommandLine`. |

`WorkloadInfo` (the render-ready manifest entry consumed by `func workload list`) is a CLI-internal type — workload authors don't see it.

### Lifecycle

```
Configure (CLI startup):
  HostApplicationBuilder
    ├── services.AddBuiltInCommands()       ← built-ins enter DI here
    └── WorkloadRegistration.RegisterWorkloads(builder)
        ├── Discover installed workloads               ← future PR (loader)
        ├── Activate each IWorkload type
        └── workload.Configure(workloadBuilder)        ← per-workload, scoped
                                                        to a WorkloadInfo
            └── builder.Services.AddSingleton<IProjectInitializer, …>()
                builder.RegisterCommand(new MyTopLevelCommand())     // optional
                builder.RegisterCommand<MyDiCommand>()               // optional
                builder.RegisterCommand(typeof(MyByTypeCommand))     // optional

Build commands (Parser.CreateCommand):
  ├── Resolve every FuncCliCommand from DI:
  │     ├── Built-ins (IBuiltInCommand) — names form the reserved set
  │     └── ExternalCommand wrappers — each carries the source WorkloadInfo
  ├── Built-in collisions throw (CLI bug); workload commands that collide with
  │   built-ins or with each other are skipped with a warning that names the
  │   workload(s)
  ├── InitCommand sees IEnumerable<IProjectInitializer>, drives each one through an
  │   IInitOptionRegistry to register init options (shared names — like --no-bundles —
  │   collapse to a single canonical Option instance)
  ├── WorkloadListCommand depends on IWorkloadProvider
  └── HelpCommand built last with a back-reference to the constructed root

Invoke (when the user runs a command):
  └── func init resolves the right IProjectInitializer (by --stack / single-installed / prompt)
      and calls InitializeAsync.
```

### Empty State (Today)

Until any workload SDK lands, no `workloads.json` exists in `~/.azure-functions/`, so `IWorkloadProvider.GetWorkloadsAsync` resolves to an empty list. With no workloads installed:

- `func workload list` prints `No workloads installed.`
- `func init` prints "No stacks installed." and a hint to install one (exit code 1).
- `func new` continues to work the same way it does on `vnext`.

## Error Handling

The CLI uses `GracefulException` (defined in `Abstractions`) for all user-facing errors:

```csharp
throw new GracefulException(
    "Failed to create project.",           // Main message (always shown)
    "Check that dotnet SDK is installed."  // Verbose detail (shown in grey)
);
```

- `GracefulException` → exit code 1 (the message, plus verbose detail if present, is printed to stderr)
- Unhandled exceptions → "An unexpected error occurred", exit code 1
- `Ctrl+C` / `SIGTERM` → exit code 130

## Telemetry

The CLI uses [OpenTelemetry](https://opentelemetry.io/) with the Azure Monitor exporter to send usage data to Application Insights, wired through `Microsoft.Extensions.Hosting` so the providers are owned by the host.

### How It Works

- `CliTelemetry` owns a single `ActivitySource` and `Meter`, both named `Azure.Functions.Cli`.
- The OTel SDK is registered on the host via `AddOpenTelemetry().WithTracing/.WithMetrics` only when an instrumentation key is baked in. The hosted services subscribe listeners on `host.StartAsync` and flush + dispose on `host.StopAsync`.
- The root activity wraps the entire command invocation; command name, exit code, OS, and runtime version land as activity tags / resource attributes.
- A `Counter<long>` records command-execution counts with the same dimensions, plus a `Histogram<double>` for command duration in milliseconds.
- `host.StopAsync(TimeSpan.FromSeconds(3))` bounds the export flush so a slow exporter can't hang the CLI on exit.

### Opt-Out

The CLI is opt-out: when no instrumentation key is baked in (the default for local builds) or `CliTelemetry.TryGetConnectionString` returns `false`, no OTel services are added to the host and the `ActivitySource` / `Meter` calls become no-ops.

### Instrumentation Key

The telemetry instrumentation key is injected at build time via `eng/build/Telemetry.props` as an assembly attribute. Local builds use an all-zeros placeholder (which disables telemetry). CI builds override via `/p:TelemetryInstrumentationKey=<key>`.

## CLI Home Directory

The CLI persists configuration, profiles, caches, the version-check stamp, the quickstart manifest cache, workloads, and the dotnet template hive under a single "func home" root. By default this is `~/.azure-functions/`.

- **Override**: set `FUNC_CLI_HOME` to an absolute path to relocate the entire home (handy for sandboxed CI, devcontainers, or per-user installs that can't write the user profile).
- The env var is read directly (not via `IConfiguration`) so host config, global config, or project `.func/config.json` can't redirect it.
- Subsystems that need their own override on top of the home still honor it: `FUNC_CLI_WORKLOADS_HOME` (workloads root) and `FUNC_CLI_DOTNET_TEMPLATE_HIVE` (dotnet template hive) take precedence over `FUNC_CLI_HOME` when set.

## Version Upgrade Check

At startup, the CLI runs a non-blocking background check for newer v5 releases via the GitHub Releases API.

- **Cache**: Results are cached in `~/.azure-functions/.version-check` with a 24-hour TTL
- **Timeout**: 5-second HTTP timeout, 1-second bounded wait at end of command execution
- **Offline**: Silently skipped if the network is unavailable
- **Scope**: Only checks for v5 releases (tags starting with `5.`), ignoring drafts and prereleases
- **Rate limits**: GitHub allows 60 unauthenticated requests/hour; the 24h cache prevents hitting this

If a newer version is found, a notice is printed after the command completes.

## HTTP Client Defaults

`HttpClientDefaults.AddCliHttpDefaults()` applies a `User-Agent` header (`AzureFunctionsCli/{version} ({os})`) to every `IHttpClientFactory` client. Individual features configure their own timeouts per-client.

## Quickstart Manifest

`func quickstart` scaffolds a project from a CDN-hosted template manifest.

```text
1. IQuickstartManifestService.GetManifestAsync()
   ├── Fresh cache (< 24h) → return cached manifest, skip network
   ├── CDN fetch with ETag/If-None-Match → 200 (cache + return) or 304 (return cached)
   └── Network failure → stale cache fallback (any age), or error if no cache
2. QuickstartManifest.Filter(language, resource, iac, search) → filtered entries
3. Command scaffolds selected template
```

- **Cache**: `~/.azure-functions/quickstart/` (manifest JSON + ETag metadata), 24h TTL
- **Validation**: only `v`-prefixed gitRef entries; HTTPS URLs on `github.com` from trusted orgs
- **Override**: `FUNC_CLI_QUICKSTART_MANIFEST_URL` env var replaces CDN URL

## Init and New Command Flow

### `func init`

```
1. Determine stack (--stack or, when multiple initializers are installed, prompt)
2. Resolve the matching IProjectInitializer via CanHandle(stack)
   ├── No initializers installed  → "No language workloads installed." (exit 1)
   └── No matching initializer    → "No installed workload supports stack '<x>'." (exit 1)
3. Detect existing project state:
   ├── .func/config.json present  → fully initialized; refuse without --force
   ├── host.json only             → adopt: write .func/config.json, skip scaffolding
   └── empty                      → scaffold via IProjectInitializer.InitializeAsync
4. Build InitContext (WorkingDirectory, ProjectName, Language, Force)
5. Delegate to IProjectInitializer.InitializeAsync(context, parseResult) (skipped in adopt mode)
```

Each registered initializer also contributes options to `func init` via `GetInitOptions(IInitOptionRegistry)`; values are read back inside `InitializeAsync` via the `ParseResult`. The registry collapses same-named contributions across workloads so shared options (e.g. `--no-bundles`) show up once in `--help` and every contributing workload reads the canonical instance.

### `func new`

```
1. Resolve the owning workload from the directory (IWorkloadResolver: --stack → FUNCTIONS_WORKER_RUNTIME → IProjectDetector pass)
2. If no project detected, run func init first (interactive flow)
3. Select template (--template or prompt)
4. Get function name (--name or prompt with editable default)
5. Scaffold the function
```
