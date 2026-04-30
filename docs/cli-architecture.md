# How the CLI Works

This document explains the runtime architecture of the Azure Functions Core Tools v5 CLI — how commands are composed, how telemetry is wired, and how the console layer works.

> **Status**: v5 is in active development. As of this PR, the workload abstractions and the DI host that loads workloads are wired into the CLI; the runtime workload loader and `func workload install` / `uninstall` commands land in a follow-up PR. Until then, the CLI runs with zero installed workloads — built-in commands that depend on workload contributions (e.g. `func init`) report "no workloads installed" and exit cleanly.

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
  │   │     ├── Built-in commands (IBuiltInCommand): init, new, start, version, help, workload
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
├── workload
│   └── list              List installed workloads
├── version (hidden)      Print version info
└── help (hidden)         Print help
```

Workloads can add their own subcommands by calling `builder.RegisterCommand(...)` from inside `IWorkload.Configure` (see [building-a-workload.md](building-a-workload.md)). Workload commands are described in parser-independent terms via `FuncCommand` / `FuncCommandOption` / `FuncCommandArgument`; the host wraps each one in an internal `ExternalCommand` adapter that translates the descriptors to `System.CommandLine` and tracks the registering workload for diagnostics.

## Command Architecture

All commands extend `FuncCliCommand` → `System.CommandLine.Command`.

### FuncCliCommand

Provides shared infrastructure:
- **`SetAction`** wiring — connects `ExecuteAsync` to the command
- **`[path]` argument** — optional via `AddPathArgument()` + `ApplyPath(parseResult)` which calls `Directory.SetCurrentDirectory`
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
- `ProjectDetector.DetectStack(AndLanguage)` infers the stack of an existing project by inspecting files on disk (`*.csproj`, `package.json`, `host.json` `metadata.workerRuntime`, etc.).

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

### `IsInteractive` Property

Returns `true` when the console supports prompts (not redirected, not CI). Commands should check this before offering interactive flows.

## Workload System

The CLI is designed around a workload model: the base CLI provides core commands and infrastructure, while stack- and feature-specific behavior is delivered by independently installable workloads. Workloads contribute behavior through plain .NET DI.

### Architecture

```
CLI (Func.Cli)
  │
  │ references
  ▼
Abstractions (Func.Cli.Abstractions)    ← NuGet package
  │
  │ referenced by
  ▼
Workload (e.g. Func.Cli.Workload.Dotnet) ← NuGet package, loaded at runtime
```

The CLI never has a compile-time reference to any workload. Workloads will be loaded dynamically via `AssemblyLoadContext`.

### Extension Points

The abstractions library exposes the surface workload authors program against:

| Type | Role |
|------|------|
| `IWorkload` | Entry point. Identity (`PackageId`, `PackageVersion`, `DisplayName`, `Description`) plus `Configure(FunctionsCliBuilder)`. |
| `FunctionsCliBuilder` | The DI seam handed to a workload. Exposes `Services` for plain DI registrations and `RegisterCommand(...)` overloads (instance / type / factory) for contributing top-level commands. Abstract base so we can grow the surface without breaking workloads. |
| `IProjectInitializer` | Owns `func init` for a stack. Declares `Stack`, `SupportedLanguages`, contributes init options, and runs `InitializeAsync(InitContext, ParseResult, CancellationToken)`. |
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
                builder.RegisterCommand(sp => new MyFactoryCommand(...)) // optional

Build commands (Parser.CreateCommand):
  ├── Resolve every FuncCliCommand from DI:
  │     ├── Built-ins (IBuiltInCommand) — names form the reserved set
  │     └── ExternalCommand wrappers — each carries the source WorkloadInfo
  ├── Built-in collisions throw (CLI bug); workload commands that collide with
  │   built-ins or with each other are skipped with a warning that names the
  │   workload(s)
  ├── InitCommand sees IEnumerable<IProjectInitializer>, attaches their options
  ├── WorkloadListCommand sees IReadOnlyList<WorkloadInfo>
  └── HelpCommand built last with a back-reference to the constructed root

Invoke (when the user runs a command):
  └── func init resolves the right IProjectInitializer (by --stack / single-installed / prompt)
      and calls InitializeAsync.
```

### Empty State (Today)

Until the loader lands, `WorkloadRegistration.RegisterWorkloads` registers an empty `IReadOnlyList<WorkloadInfo>`. With no workloads installed:

- `func workload list` prints `No workloads installed.`
- `func init` prints "No stacks installed." and a hint to install one (exit code 1).
- `func new` continues to work the same way it does on `vnext`.

## Error Handling

The CLI uses `GracefulException` (defined in `Func.Cli.Abstractions`) for all user-facing errors:

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

## Version Upgrade Check

At startup, the CLI runs a non-blocking background check for newer v5 releases via the GitHub Releases API.

- **Cache**: Results are cached in `~/.azure-functions/.version-check` with a 24-hour TTL
- **Timeout**: 5-second HTTP timeout, 1-second bounded wait at end of command execution
- **Offline**: Silently skipped if the network is unavailable
- **Scope**: Only checks for v5 releases (tags starting with `5.`), ignoring drafts and prereleases
- **Rate limits**: GitHub allows 60 unauthenticated requests/hour; the 24h cache prevents hitting this

If a newer version is found, a notice is printed after the command completes.

## Init and New Command Flow

### `func init`

```
1. Determine stack (--stack or, when multiple initializers are installed, prompt)
2. Resolve the matching IProjectInitializer via CanHandle(stack)
   ├── No initializers installed  → "No language workloads installed." (exit 1)
   └── No matching initializer    → "No installed workload supports stack '<x>'." (exit 1)
3. Build InitContext (ProjectPath, ProjectName, Language, Force)
4. Delegate to IProjectInitializer.InitializeAsync(context, parseResult)
```

Each registered initializer also contributes options to `func init` via `GetInitOptions()`; values are read back inside `InitializeAsync` via the `ParseResult`.

### `func new`

```
1. Detect stack and language from project files (ProjectDetector)
2. If no project detected, run func init first (interactive flow)
3. Select template (--template or prompt)
4. Get function name (--name or prompt with editable default)
5. Scaffold the function
```
