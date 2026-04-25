# How the CLI Works

This document explains the runtime architecture of the Azure Functions Core Tools v5 CLI — how commands are composed, how telemetry is wired, and how the console layer works.

> **Status**: v5 is in active development. The workload extensibility model described in [building-a-workload.md](building-a-workload.md) is being staged in follow-up PRs and is not yet wired into the CLI on `vnext`.

## Startup Flow

```
Program.cs
  ├── Build IInteractionService (Spectre + DefaultTheme)
  │
  ├── Wire up OpenTelemetry SDK (when a connection string is baked in)
  │   ├── TracerProvider — exports to Azure Monitor
  │   └── MeterProvider — exports to Azure Monitor
  │
  ├── Parser.CreateCommand(interaction)
  │   ├── Build the root command and its subcommands
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
  ├── Flush + dispose the OpenTelemetry providers
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
├── pack [path]           Package the function app for deployment
├── start [path]          Start the Functions host (placeholder)
├── version (hidden)      Print version info
└── help (hidden)         Print help
```

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

The CLI uses [OpenTelemetry](https://opentelemetry.io/) with the Azure Monitor exporter to send usage data to Application Insights.

### How It Works

- `CliTelemetry` owns a single `ActivitySource` and `Meter`, both named `Azure.Functions.Cli`.
- The root activity wraps the entire command invocation; command name, exit code, OS, and runtime version land as activity tags / resource attributes.
- A `Counter<long>` records command-execution counts with the same dimensions, plus a `Histogram<double>` for command duration in milliseconds.
- `TracerProvider` and `MeterProvider` flush via `ForceFlush(3000)` and dispose on exit.

### Opt-Out

The CLI is opt-out: when no instrumentation key is baked in (the default for local builds) or `CliTelemetry.TryGetConnectionString` returns `false`, no providers are created and the `ActivitySource` / `Meter` calls become no-ops.

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

## Init, New, and Pack Command Flow

Today these commands operate on the stack inferred from the user (`--stack`) or from project files on disk. The full template / project-initializer / pack-provider extensibility model — where each stack's behavior is contributed by an installable workload — is documented in [building-a-workload.md](building-a-workload.md) and is being added in follow-up PRs.

### `func init`

```
1. Determine stack (--stack or prompt)
2. Determine language (--language or prompt from the stack's supported languages)
3. Scaffold the project for that stack
```

### `func new`

```
1. Detect stack and language from project files (ProjectDetector)
2. If no project detected, run func init first (interactive flow)
3. Select template (--template or prompt)
4. Get function name (--name or prompt with editable default)
5. Scaffold the function
```

### `func pack`

```
1. Detect stack from project files (ProjectDetector)
2. Validate the project (stack-specific checks)
3. If --no-build: skip build step, use project dir as-is
4. Otherwise: prepare the project (e.g., dotnet publish for the dotnet stack)
5. Zip the output directory using System.IO.Compression.ZipFile
6. Output: <project-name>.zip in the --output directory (or current dir)
```
