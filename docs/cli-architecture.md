# How the CLI Works

This document explains the runtime architecture of the Azure Functions Core Tools v5 CLI — how commands are composed, how workloads integrate, and how the console layer works.

## Startup Flow

```
Program.cs
  ├── Build DI container (Host)
  │   ├── IInteractionService → SpectreInteractionService
  │   ├── ITelemetry → AppInsightsTelemetryClient or NoOpTelemetryClient
  │   └── IWorkloadManager → WorkloadManager
  │
  ├── Start telemetry activity (wraps entire command execution)
  │
  ├── Start background version check (non-blocking)
  │
  ├── Parser.CreateCommand(interaction, workloadManager)
  │   ├── Create built-in commands (version, help, init, new, pack, start)
  │   ├── Add workload management command (install, uninstall, list, update)
  │   ├── workloadManager.LoadWorkloads()
  │   │   ├── Read ~/.azure-functions/workloads/workloads.json
  │   │   ├── For each workload: AssemblyLoadContext → load DLL → find IWorkload
  │   │   └── Fire background update check (non-blocking)
  │   ├── For each loaded workload: workload.RegisterCommands(rootCommand)
  │   └── Replace help rendering with Spectre on all commands
  │
  ├── rootCommand.Parse(args).InvokeAsync()
  │
  ├── workloadManager.PrintUpdateNoticesAsync()  ← shows workload update notices
  ├── Print CLI version upgrade notice (if newer v5 release available)
  │
  ├── Track telemetry (command name, success/failure, duration)
  ├── Flush telemetry
  │
  └── Error handling
      ├── OperationCanceledException → exit 130
      ├── GracefulException → user-friendly error, exit 1 or 2
      └── Exception → "unexpected error", exit 2
```

## Command Tree

```
func
├── init [path]           Initialize a new Functions project
├── new [path]            Create a new function from a template
├── pack [path]           Package the function app for deployment
├── start [path]          Start the Functions host (placeholder)
├── workload
│   ├── install           Install a workload package
│   ├── uninstall         Uninstall a workload
│   ├── list              List installed workloads
│   └── update            Update a workload to the latest version
├── version (hidden)      Print version info
└── help (hidden)         Print help
```

Workloads can add their own commands to the tree via `IWorkload.RegisterCommands()`.

## Command Architecture

All commands extend `BaseCommand` → `System.CommandLine.Command`.

### BaseCommand

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
       ├── Delegate to workload providers as needed
       └── Return exit code (0 = success)
```

### Option/Argument Conventions

Options and arguments are defined as `static readonly` fields on the command class:

```csharp
// Option with short alias and default value
public static readonly Option<string> NameOption = new("--name", "-n")
{
    Description = "The name of the function app project"
};

// Option with default value factory
public static readonly Option<string> FrameworkOption = new("--target-framework")
{
    Description = "The target framework (default: net10.0)",
    DefaultValueFactory = _ => "net10.0"
};

// Positional argument (via BaseCommand)
AddPathArgument(); // adds optional [path] argument
```

## Console / UI Layer

**All console I/O goes through `IInteractionService`** — never `Console.Write*` and
never raw Spectre markup (`[red]…[/]`). This enables testability (in-memory
capture), non-interactive mode (prompts return defaults), and theming.

### Theming

A single `ITheme` exposes `Style` values for semantic roles (`Command`,
`Placeholder`, `Path`, `Muted`, `Heading`, `Success`, `Error`, `Warning`, etc.).
`DefaultTheme` holds the production palette. Swapping the DI registration is the
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
    .Command("func workload search")
    .Muted(" to discover available workloads."));
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
Workload (e.g., Func.Cli.Workload.Dotnet)  ← NuGet package, loaded at runtime
```

The CLI never has a compile-time reference to any workload. Workloads are loaded dynamically via `AssemblyLoadContext`.

### Workload Lifecycle

```
Install:
  func workload install dotnet
  ├── Resolve alias: "dotnet" → "Azure.Functions.Cli.Workload.Dotnet"
  ├── Download NuGet package (TODO: currently creates placeholder)
  ├── Extract to ~/.azure-functions/workloads/dotnet/{version}/
  └── Update workloads.json manifest

Load (at CLI startup):
  ├── Read workloads.json
  ├── For each entry:
  │   ├── Create AssemblyLoadContext (isolation)
  │   ├── LoadFromAssemblyPath(dll)
  │   ├── Find type implementing IWorkload (reflection)
  │   └── Activator.CreateInstance() (parameterless ctor required)
  └── Fire background update check

Contribute:
  ├── RegisterCommands() — add commands to CLI tree
  ├── GetTemplateProviders() — provide templates for `func new`
  ├── GetProjectInitializer() — handle `func init` for the runtime
  └── GetPackProvider() — handle `func pack` build/publish step
```

### Well-Known Aliases

Users can use short names instead of full NuGet package IDs:

| Alias       | Package ID |
|-------------|-----------|
| `dotnet`    | `Azure.Functions.Cli.Workload.Dotnet` |
| `node`      | `Azure.Functions.Cli.Workload.Node` |
| `python`    | `Azure.Functions.Cli.Workload.Python` |
| `java`      | `Azure.Functions.Cli.Workload.Java` |
| `powershell`| `Azure.Functions.Cli.Workload.PowerShell` |

### Update Notifications

`WorkloadUpdateChecker` runs a background NuGet check when workloads are loaded:
- Queries `api.nuget.org/v3-flatcontainer/{id}/index.json` for each installed workload
- Caches results in `~/.azure-functions/workload-update-cache.json` (24h TTL)
- Prints notices after the command completes (waits up to 2s, then silently skips)
- Never blocks or fails the command

### Workload-Contributed Options

When a workload provides options for `func init` (e.g., `--target-framework`), they are labeled in help output:

```
  --target-framework      [dotnet] The target framework (default: net10.0)
```

The `[dotnet]` prefix indicates which workload contributed the option.

## Error Handling

The CLI uses `GracefulException` for all user-facing errors:

```csharp
throw new GracefulException(
    "Failed to create project.",           // Main message (always shown)
    "Check that dotnet SDK is installed."  // Verbose detail (shown in grey)
);
```

- `IsUserError = true` (default) → exit code 1
- `IsUserError = false` → exit code 2 (internal/unexpected)
- Unhandled exceptions → "An unexpected error occurred", exit code 2
- `Ctrl+C` / `SIGTERM` → exit code 130

## Telemetry

The CLI uses [OpenTelemetry](https://opentelemetry.io/) with the Azure Monitor exporter to send usage data to Application Insights.

### How It Works

- An `ActivitySource` ("Azure.Functions.Cli") creates a root span that wraps the entire command execution
- Command name, success/failure, duration, OS, and runtime version are recorded as span attributes
- `TracerProvider` exports via `Azure.Monitor.OpenTelemetry.Exporter` with a 3-second flush timeout on exit

### Opt-Out

Set `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT=1` or `true` to disable telemetry. When disabled, a `NoOpTelemetryClient` is used (zero startup cost — no `TracerProvider` created).

### Instrumentation Key

The telemetry instrumentation key is injected at build time via `eng/build/Telemetry.props` as an assembly attribute. Local builds use an all-zeros placeholder (which disables telemetry). CI builds override via `/p:TelemetryInstrumentationKey=<key>`.

## Version Upgrade Check

At startup, the CLI runs a non-blocking background check for newer v5 releases via the GitHub Releases API.

- **Cache**: Results are cached in `~/.azure-functions/.version-check` with a 24-hour TTL
- **Timeout**: 5-second HTTP timeout, 1-second bounded wait at end of command execution
- **Offline**: Silently skipped if the network is unavailable
- **Scope**: Only checks for v5 releases (tags starting with `5.`), ignoring drafts and prereleases
- **Rate limits**: GitHub allows 60 unauthenticated requests/hour; the 24h cache prevents hitting this

If a newer version is found, a notice is printed after the command completes:

```
💡 A newer version of Azure Functions Core Tools is available (5.1.0 → 5.2.0).
   Update with: dotnet tool update -g Azure.Functions.Cli
```

## Init and New Command Flow

### `func init`

```
1. Determine worker runtime (--worker-runtime or prompt)
2. If no workload installed for that runtime:
   ├── Offer to install it
   ├── After install, continue (no "re-run" needed)
   └── Find the project initializer from the newly loaded workload
3. Determine language (--language or prompt from workload's SupportedLanguages)
4. Delegate to IProjectInitializer.InitializeAsync(context, parseResult)
```

### `func new`

```
1. Detect worker runtime and language from project files (ProjectDetector)
2. If no project detected, run func init first (interactive flow)
3. Discover templates from all loaded workload template providers
4. Filter by detected worker runtime
5. Select template (--template or prompt)
6. Get function name (--name or prompt with editable default)
7. Delegate to ITemplateProvider.ScaffoldAsync(context)
```

### `func pack`

```
1. Detect worker runtime from project files (ProjectDetector)
2. Find matching IPackProvider from loaded workloads
3. Validate the project (provider-specific checks)
4. If --no-build: skip build step, use project dir as-is
5. Otherwise: delegate to IPackProvider.PrepareAsync() (e.g., dotnet publish)
6. Zip the output directory using System.IO.Compression.ZipFile
7. Output: <project-name>.zip in the --output directory (or current dir)
8. Cleanup temporary build artifacts via IPackProvider.CleanupAsync()
```
