# Building a New Workload

This guide walks through building a workload for the Azure Functions Core Tools v5 CLI. A workload is a NuGet package that the CLI loads at runtime to extend its behavior, most commonly to provide `func init` / `func new` support for a specific language stack (e.g. Node.js, Python, Java), but a workload can also contribute brand-new subcommands.

> For the underlying on-disk and on-feed layout and the `kind` discriminator, see the [package layout proposal](../proposed/workload-package-layout.md). This guide describes the current workload SDK and stack contracts.

## Architecture

```
┌─────────────────────────────────┐
│  Func (the CLI executable)      │
│  Builds a host, loads workloads │
│  via AssemblyLoadContext        │
└──────────┬──────────────────────┘
           │ references (compile-time)
           ▼
┌─────────────────────────────────┐
│  Abstractions                   │
│  NuGet package with the         │
│  Workload base class,           │
│  FunctionsCliBuilder,           │
│  IProjectInitializer, …         │
└──────────┬──────────────────────┘
           │ referenced by (compile-time)
           ▼
┌─────────────────────────────────┐
│  Your Workload                  │
│  e.g. Workloads.Node             │
│  NuGet package, loaded at       │
│  runtime via reflection         │
└─────────────────────────────────┘
```

**Key principle**: workloads reference only `Abstractions`, never `Func`. The CLI loads workloads dynamically — there is no compile-time dependency from the CLI to any workload.

## How a Workload Plugs In

The CLI builds a `HostApplicationBuilder` at startup. Each installed workload is loaded into its own `AssemblyLoadContext`, instantiated, and handed a `FunctionsCliBuilder`:

```
1. CLI builds the host (HostApplicationBuilder)
2. For each entry in ~/.azure-functions/workloads.json:
     ├── Load the workload assembly into an isolated AssemblyLoadContext
     ├── Activate the Workload type named in the package's workload.json
     └── Call workload.Configure(builder)        ← workload registers services here
3. CLI builds the root command tree from DI
     ├── Built-in commands resolve workload-contributed services they consume
     │   (e.g. InitCommand consumes IEnumerable<IProjectInitializer>)
     └── Every command registered through builder.RegisterCommand(...) is
         added as a root subcommand, tagged with the workload that registered it
4. Command dispatch
```

The shape mirrors WebJobs' `IWebJobsStartup`. `Configure(FunctionsCliBuilder)` is the workload's only seam, and everything beyond it is plain .NET DI.

## Quick Start

1. Create a class library project under `src/Workloads/<kind>/<Name>/` that targets `net11.0` and packs as `PackageType=FuncCliWorkload`; omit `<kind>/` for workloads that do not belong to a grouping
2. Reference `Azure.Functions.Cli.Abstractions` (with `PrivateAssets=all` and `ExcludeAssets=runtime` so it isn't shipped inside your package)
3. If this package provides a project stack, declare its `StackIdentifier`, `Title`, and `Description` in the project
4. Subclass `Workload`, register its stack and capabilities in `Configure`, and mark the assembly with `[assembly: CliWorkload<YourWorkload>()]`
5. Let the workload SDK generate `workload.json`. A workload that only contributes commands does not need a stack
6. Add a `README.md` next to the csproj — `Release.props` auto-includes it as `<PackageReadmeFile>` so it surfaces on NuGet feeds and in `func workload list`
7. Build, package, and explicitly install your unsigned development package with `func workload install <path-to-nupkg> --allow-untrusted`. See [workload publisher trust](workload-publisher-trust.md).

## Step 1: Create the Project

Use `src/Workloads/<kind>/<Name>/` for grouped workloads. Stack workloads use
`Stacks` as the kind, while workloads without a grouping can live directly
under `src/Workloads/<Name>/`.

```bash
mkdir src/Workloads/<kind>/Node
cd src/Workloads/<kind>/Node
```

For ungrouped workloads, omit the `<kind>/` segment from the commands above.

Create the `Workloads.Node.csproj`. The csproj is the single source of truth for packaging; there are no per-folder `Directory.Build.props` for workloads.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Title>Node.js</Title>
    <Description>Azure Functions CLI tooling for Node.js projects.</Description>
    <StackIdentifier>node</StackIdentifier>
    <PackAsWorkload>true</PackAsWorkload>
    <WorkloadKind>workload</WorkloadKind>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Azure.Functions.Cli.Workloads.Node.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="$(SrcRoot)Abstractions/Abstractions.csproj" />
  </ItemGroup>

</Project>
```

`<StackIdentifier>` supplies `StackDescriptor.Identifier`, the stable spelling accepted by `func init --stack` and stored in `.func/config.json`. `WorkerRuntime` defaults to this ID. Override it when the host uses a different name: the Go workload declares `<StackIdentifier>go</StackIdentifier>` and `<WorkerRuntime>native</WorkerRuntime>`. `Title` is a human label, never an input alias.

The SDK projects this declaration into NuGet (`stack:<id>`, `alias:<id>`, `worker-runtime:<runtime>`, title and description) and into the package's `workload.json`. The CLI associates it with the **evaluated NuGet package ID**, not a naming convention. For example, a project may declare `<StackIdentifier>comet</StackIdentifier>` and `<PackageId>Contoso.DevKit</PackageId>`. It appears in `func workload search --stack` and installs with `func workload install Contoso.DevKit --exact`. Declaring a stack does not create a `func setup` dependency recipe.

Search uses the `FuncCliWorkload` package type, not the optional `func-workload` tag. For searches over 1,000 results, the feed needs to confirm the package type in its results. Otherwise the CLI reports an error rather than scanning the whole feed or returning a partial inventory. Narrow the search or install a known package ID with `--exact`.

Search reads metadata for the exact version and source. Installation captures the actual package manifest in that version's registry entry. Help and project commands use the installed descriptor without querying the feed. A preview can change its title while an installed stable version continues to show its own title.

### Runtime-specific workload packages

Set `RuntimeIdentifiers` when a workload's payload differs by platform. The Workload SDK then produces one user-facing pointer package and one implementation package per RID:

```xml
<PropertyGroup>
  <RuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>
  <PackAsWorkload>true</PackAsWorkload>
  <WorkloadKind>content</WorkloadKind>
  <WorkloadAlias>python-worker</WorkloadAlias>
</PropertyGroup>
```

- The pointer keeps the project package ID, uses package type `FuncCliWorkload`, and contains a `kind: rid-pointer` manifest whose `packages` map selects the implementation for each RID.
- Each implementation uses the package ID `<pointer-package-id>.<rid>`, package type `FuncCliWorkloadRidPackage`, and retains the authored `workload` or `content` kind.
- The SDK writes `runtimeIdentifier` into every implementation manifest and adds the matching `rid:<rid>` package tag. Do not author either value independently.
- Implementation payloads ship under `tools/<rid>/` rather than `tools/any/`; the CLI derives the content root from the manifest's `runtimeIdentifier`.
- Publish the pointer and every implementation at the same exact version and to the same feed. The CLI resolves only the mapped package ID at the pointer's exact version from the source that supplied the pointer.

Users install, update, uninstall, and list the pointer identity. The CLI records the selected physical implementation and RID as ownership details; `func workload list --json` exposes both logical and physical package IDs.

> **Pack scope.** The package must contain the publish output (workload `.dll`, `.deps.json`, optional `.pdb`, every transitive managed dependency, and any `runtimes/<rid>/` assets the dependencies ship). Use the Workload SDK's `PackAsWorkload` targets rather than manually copying only the primary assembly when a workload has a runtime dependency closure.

Add a sibling `Directory.Version.props` for the workload version:

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>
    <VersionSuffix>preview.1</VersionSuffix>
  </PropertyGroup>
</Project>
```

And a `release_notes.md`:

```markdown
# Azure.Functions.Cli.Workloads.Node

## 1.0.0-preview.1

- Initial scaffold of the Node.js workload (entry point + stub project initializer).
```

The SDK generates the manifest and packages the workload's publish output. The generated `entryPoint` and the assembly's `CliWorkload` attribute identify the workload to activate. Do not maintain a second copy of the stack declaration in C# or hand-author SDK-generated tags.

Runtime (`kind: workload`) manifests may also declare an optional single string
`templateRoot`, such as `"content/templates"`, relative to the **physical package root**
(not `tools/any/` or `tools/<rid>/`). Missing or null means no declared template root.
The CLI validates this declaration and persists it in the installed workload registry.
The declaration alone does not install a TemplateEngine package.
Content, meta, and RID-pointer manifests cannot declare a non-null `templateRoot`.
For SDK-generated manifests, set `<WorkloadTemplateRoot>content/templates</WorkloadTemplateRoot>`
in the workload project. For multi-RID runtime workloads, the SDK omits the root
from the generated pointer manifest and retains it in each implementation manifest;
the authored property remains available for content layout.
To bundle files from another project, add
`Pack="true"` to its `ProjectReference`. The workload SDK packs that project's
copy-to-output items, using each item's `TargetPath` as its package path. Add
`TargetPathPrefix` to the same reference to place those items under a directory
in both the build output and the package. The .NET stack workload references
`Workloads.Templates.DotNet` with `TargetPathPrefix="$(WorkloadTemplateRoot)"`,
so the template project stays layout-agnostic.

Go, Java, Node.js, PowerShell, and Python use the same wiring with empty template
projects under `src/Workloads/Templates/` and one skipped placeholder test per
project under `test/Workloads/Templates/`. Until template files are added, each
template project contains a `placeholder.txt` so the declared `content/templates`
directory exists in the package. Remove it when adding templates.

Paths must be relative according to the current platform's `Path.IsPathRooted`
rules. The CLI normalizes both slash styles, combines the declaration with the
package directory, and uses `Path.GetFullPath` to reject paths that resolve outside
or to the package root itself. Containment is separator-bounded and compared
case-insensitively on Windows, ordinally elsewhere. `.` and `..` segments and
repeated separators are resolved; registry paths use `/` without trailing separators.
This is lexical containment, not symbolic-link resolution or a guarantee that
directory names are valid on every filesystem. Authors must choose names supported
on their target platforms.

The registry stores package identity, not an absolute installation path. When reading entries, the workload store hydrates the non-serialized `PackageRoot` from the configured workload home, package id, and version. Entries saved or returned by ownership changes are bound to that same store's paths.

Add a `README.md` next to the csproj. `Release.props` auto-wires it as `<PackageReadmeFile>` (and the Functions icon as `<PackageIcon>`), so both surface on NuGet feeds without extra packing config. Use it to describe the workload's purpose, the language(s) it supports, and any prerequisites.

## Step 2: Subclass `Workload`

`Workload` is an abstract class in `Azure.Functions.Cli.Abstractions` (under `Azure.Functions.Cli.Workloads`). The CLI loader instantiates the type named in `workload.json`, then calls `Configure(builder)`. Command-only workloads use a public parameterless constructor. A stack workload opts in to its package's descriptor by declaring a public constructor that takes exactly one `StackDescriptor`. If that constructor exists, the loader always uses it and reports a missing stack declaration rather than passing `null`.

```csharp
// NodeWorkload.cs
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Node.NodeWorkload>()]

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Entry-point for the Node.js workload. Registers the project initializer
/// and any Node-specific services.
/// </summary>
public sealed class NodeWorkload(StackDescriptor descriptor) : Workload
{
    private readonly NodeProjectStack _stack = new(descriptor);

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(_stack);
        builder.Services.AddSingleton<IProjectStack>(_stack);
        builder.Services.AddSingleton<IProjectInitializer, NodeProjectInitializer>();
        // builder.RegisterCommand<NodeTopLevelCommand>();
        // …any other services your providers depend on
    }
}

internal sealed class NodeProjectStack(StackDescriptor descriptor) : IProjectStack
{
    public StackDescriptor Descriptor { get; } = descriptor;
    public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } =
        [new("javascript", "JavaScript", "js"), new("typescript", "TypeScript", "ts")];
}
```

The loader passes the descriptor from this package version's registry entry directly to the entry-point constructor. Share the resulting stack with this workload's initializers, project factories, and start option contributors. Lookups match CLI IDs case-insensitively, but an `IProjectStack` registration must preserve its package version's exact ID, runtime, display name, and description. A command-only package does not need to register a stack. Do not register an unkeyed `StackDescriptor` in the shared service collection: it cannot distinguish between installed owners.

For a packaged worker, the first argument to `FunctionsWorkerReference.FromWorkload` identifies the **worker to install or resolve**, not the CLI stack. For example, a stack that uses the Go worker calls `FromWorkload("go", stack.Descriptor.WorkerRuntime)` even if its CLI identifier is `comet`. The host receives the runtime from the descriptor.

Language descriptors are ordered. Give each a stable CLI ID, a display label, and explicit input aliases. `IsMatch` trims and matches the ID and aliases case-insensitively, not the label. For C#, declare `new("csharp", "C#", "c#")`; both `--language CSharp` and `--language c#` select the same descriptor. Commands emit `csharp`, not a lowercased display label. Shared languages are resolved within the selected stack.

## Step 3: Implement IProjectInitializer

`IProjectInitializer` extends `func init`. Each registered initializer owns a stack, contributes any extra options it needs, and scaffolds the project.

```csharp
// NodeProjectInitializer.cs
using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

internal sealed class NodeProjectInitializer(NodeProjectStack stack) : IProjectInitializer
{
    public IProjectStack Stack { get; } = stack;

    // Workload-specific options. Registered through the supplied IInitOptionRegistry,
    // which is what lets options like `--no-bundles` that multiple stacks contribute
    // appear once in --help and resolve to the same instance for every workload that
    // reads them back. Stash the canonical instance returned by GetOrAdd and read
    // values from it inside InitializeAsync.
    public Option<string> PackageManagerOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        PackageManagerOption = registry.GetOrAdd(new Option<string>("--package-manager")
        {
            Description = "The package manager to use (npm, yarn, pnpm)",
            DefaultValueFactory = _ => "npm"
        });

        return [PackageManagerOption];
    }

    public async Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        var packageManager = parseResult.GetValue(PackageManagerOption) ?? "npm";
        var languageId     = context.Language?.LanguageIdentifier;
        var projectPath    = context.WorkingDirectory.Info.FullName;

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "package.json"),
            GeneratePackageJson(context.ProjectName ?? "my-functions-app"),
            cancellationToken);

        // …host.json, local.settings.json, tsconfig.json, etc.
    }

    // ... helper methods to generate file contents ...
}
```

### Contexts

`InitContext` supplies `WorkingDirectory`, `ProjectName`, the resolved `LanguageDescriptor?`, and `Force`. Do not parse the original user input again. Persisted project settings use `StackDescriptor.Identifier` and, for stacks that distinguish languages, `LanguageDescriptor.LanguageIdentifier`. When adopting an existing project, the CLI resolves `FUNCTIONS_WORKER_RUNTIME` against `WorkerRuntime` before writing any CLI config.

Workload-specific option values flow through the `ParseResult`, so the CLI never has to know about your options.

## Step 4 (optional): Contribute New Commands

For workloads that aren't tied to a stack, such as a Durable Functions command, omit `StackIdentifier` and register top-level commands through `builder.RegisterCommand(...)`. Workload commands derive from `FuncCommand` (in `Azure.Functions.Cli.Commands`), which is parser-independent: you describe the command's name, description, options, arguments, and subcommands using lightweight descriptors, and read parsed values through a `FuncCommandInvocationContext` at execution time. The CLI tracks which workload each command came from for diagnostics and collision reporting.

```csharp
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;

internal sealed class DurableCommand : FuncCommand
{
    private static readonly FuncCommandOption<string> InstanceOption =
        new("--instance-id", "-i", "Orchestration instance id");

    public override string Name => "durable";

    public override string Description => "Manage Durable Functions";

    public override IReadOnlyList<FuncCommand> Subcommands =>
    [
        new ListInstancesCommand(),
        new StartNewCommand(),
    ];

    public override Task<int> ExecuteAsync(
        FuncCommandInvocationContext context,
        CancellationToken cancellationToken) => Task.FromResult(0);

    private sealed class ListInstancesCommand : FuncCommand
    {
        public override string Name => "get-instances";

        public override string Description => "List orchestration instances.";

        public override IReadOnlyList<FuncCommandOption> Options => [InstanceOption];

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken)
        {
            var instanceId = context.GetValue(InstanceOption);
            // …list instances…
            return Task.FromResult(0);
        }
    }

    private sealed class StartNewCommand : FuncCommand
    {
        public override string Name => "start-new";

        public override string Description => "Start a new orchestration.";

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
}

public sealed class DurableWorkload : Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Pick the overload that fits how your command is constructed:
        //   builder.RegisterCommand(new DurableCommand());        // simple instance
        //   builder.RegisterCommand<DurableCommand>();            // DI-constructed (recommended)
        //   builder.RegisterCommand(typeof(DurableCommand));      // by runtime Type
        builder.RegisterCommand<DurableCommand>();
    }
}
```

This produces `func durable get-instances`, `func durable start-new`, and so on. Reading parsed values goes through the supplied `FuncCommandInvocationContext` using the **same descriptor instance** the command exposes through `Options` / `Arguments` — descriptors are looked up by reference identity, so don't construct a new descriptor every time you read a value.

> The host wraps your `FuncCommand` in an internal adapter that carries the owning `WorkloadInfo`, so `func` can identify the source workload in collision warnings and diagnostics. Don't register raw `System.CommandLine.Command` services through `builder.Services` — the parser only consumes commands registered through `RegisterCommand`.

## Template Post Actions

Workloads can implement `Azure.Functions.Cli.Templates.PostActions.IPostActionProcessor`
and register it during `Configure`:

```csharp
public override void Configure(FunctionsCliBuilder builder)
{
    builder.AddTemplateComponent<MyPostActionProcessor>();
}
```

`IPostActionProcessor` extends the CLI-owned `ITemplateComponent`. Its `Guid Id`
matches a template's `postActions[].actionId`, and its
`Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken)`
returns `true` on success or `false` when the action failed and manual instructions
should be displayed. Forward cancellation to any asynchronous work. Unexpected
exceptions propagate; they are not converted into successful actions.

The context contains the action ID, read-only arguments, description, manual
instructions, `ContinueOnError`, output directory, relative primary output paths,
file-change metadata, a source-file matcher, and a `Reporter`. Collections are
independent, immutable snapshots.
Both primary outputs and file-change target paths use `/` directory separators
on all platforms. File changes describe effects evaluated by the engine, not a
filesystem audit.
Workloads need only the `Abstractions` reference: no `Microsoft.TemplateEngine`
types, engine settings, or engine service providers cross the boundary.

When available, `context.FileMatcher.MatchTargetPaths(pattern)` applies template
glob semantics to source-relative paths and returns the corresponding generated
target paths. `FileMatcher` is null when source-path metadata is unavailable.

`context.GetConfiguredFiles("files")` resolves the named argument's source
patterns to absolute generated paths. It supports semicolon-delimited strings
or JSON arrays and accepts an optional target-path predicate.

For the standard restore action, derive from `RestorePostAction`. It supplies
the action ID, selects `files` or falls back to primary outputs, and attempts
each target. Implement `CanProcess` for the supported template tags and
`RestoreAsync` for one target; return false for expected restore failures.

For the standard add-reference action (`B17581D1-C5C9-4489-8F0A-004BE667B814`),
derive from `AddReferencePostAction<TReference>`, where `TReference` is the
workload's `PostActionReference` subtype. It validates the required `reference` and
`referenceType` arguments and the optional `version`, then selects targets from
`targetFiles`. When nothing matches, it calls the virtual `TryFindTargets`,
which finds nothing by default. Targets are processed in order, stopping at the
first failure. In the workload processor's constructor, declare supported
reference types and their methods:

```csharp
RegisterReferenceHandlers(
    ("package", AddPackageReferenceAsync),
    ("project", AddProjectReferenceAsync));
```

Each method takes `(PostActionContext context, string targetPath,
PostActionReference reference, CancellationToken cancellationToken)` and returns
`Task<bool>`. Type names are matched case-insensitively; blank names, null
handlers, and duplicate names are rejected. The base class defines no reference
types. Unregistered types report an unsupported-reference error. Handlers own
type-specific reporting and interpretation of reference properties, including
any path resolution or template rename handling. The default `TryGetReference`
implementation populates the subtype's properties from matching post-action
arguments; override it when a workload needs custom creation or conversion.
Subtypes can add properties for ecosystem-specific values such as registries
or classifiers.
`context.GetConfiguredPaths(name)` resolves an argument's paths against the
output directory without source matching.

Use `context.Reporter.ReportStatus(message)` for status and
`context.Reporter.WriteLog(line, FunctionsReportSeverity.Info)` for subprocess
output. `Warning` and `Error` severities are also available. This uses the same
`IFunctionsReporter` contract and `FunctionsReportSeverity` enum as project
host-run reporting. The CLI currently supplies a no-op reporter to post-action
processors, so their status and output are discarded. When a processor returns
`false`, the dispatcher still displays the template's manual instructions.

The generic registration activates the processor through DI and retains it as a
singleton. An already-registered implementation is reused and remains owned by
DI. Otherwise the CLI creates and disposes it. The instance overload,
`builder.AddTemplateComponent(processor)`, leaves disposal to its caller.
The CLI currently bridges post-action processors to the engine. Other
`ITemplateComponent` contracts can be registered but are ignored until the CLI
supports them; arbitrary template-engine types are not exposed.

The .NET stack workload registers the standard NuGet restore action
(`210D431B-A78B-4D2F-B762-4ED3E3EA9025`) for `dotnet` templates tagged with
`C#`, `csharp`, `F#`, or `fsharp` (case-insensitive). Its optional `files`
argument accepts the same source-relative patterns as `dotnet new`, either as a
semicolon-delimited string or a JSON array. If no files match, it restores the
primary outputs instead; no matching files and no primary outputs is a success.

It also registers the add-reference action (`B17581D1-C5C9-4489-8F0A-004BE667B814`)
for the same templates, running `dotnet add <target> package <id> [--version <v>]`
or `dotnet add <target> reference <project>`. When `targetFiles` matches no
generated files, it follows `dotnet new`: it uses the single project file in the
nearest directory at or above the output directory, filtered by the literal
`projectFileExtensions` (compared case-insensitively). Multiple projects in that
directory are an error. If none are found, existing `targetFiles` paths are used.
Its project-reference handler resolves references to absolute paths, following
template renames when the reference matches exactly one generated file.

### Selecting a Processor

Implement `bool CanProcess(PostActionSelectionContext context)` to declare
applicability. The immutable context contains the selected template's `Stack` and
`Language` tags, trimmed but otherwise unchanged. Missing or blank tags are
represented by empty strings, so workloads can reject them in `CanProcess`.
A runtime workload can contribute processors for any stack without registering
an `IProjectStack`:

```csharp
public bool CanProcess(PostActionSelectionContext context)
{
    ArgumentNullException.ThrowIfNull(context);
    return string.Equals(context.Stack, "node", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(context.Language, "TypeScript", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Language, "ts", StringComparison.OrdinalIgnoreCase));
}
```

Processors choose accepted values and aliases explicitly; the CLI does not
resolve them through installed stacks. A workload that owns an `IProjectStack`
can instead return `context.IsCompatibleWith(stack)`, which matches the stack ID
and its supported languages or aliases case-insensitively. `CanProcess` must be synchronous and
side-effect-free. For example, the predicate above accepts this template:

```json
{
  "tags": {
    "stack": "node",
    "language": "TypeScript"
  }
}
```

Core CLI processors always win for their action IDs, without evaluating workload
predicates. Otherwise the CLI evaluates every registered candidate's `CanProcess`
for that action ID before executing any processor. Exactly one must accept the
context. No eligible processor or multiple eligible
registrations produce a diagnostic without executing a workload processor.
Overlaps are detected for the selected template at dispatch, not at registration.
Conflict diagnostics identify the contributing workloads and processor types.
Repeated registration of the same eligible instance is also a conflict.

There is no registration-order or first-success fallback, even when only one
processor is installed. A `false` execution result is a failure, not permission
to try another processor after possible side effects. Manual instructions and
`continueOnError` apply as for other post-action failures. Predicate exceptions
propagate just like unexpected execution exceptions.

The dispatcher is not yet wired into `func init` or `func new`; registering a
processor alone does not activate template-based generation. Dry runs never
evaluate predicates or execute processors. Script permission policy remains deferred.

## Reading Workload-Specific Options

Built-in commands collect options from every registered provider during command construction, so they appear in `--help` automatically. Inside `InitializeAsync` (or any other provider method), read your option values from the supplied `ParseResult`:

```csharp
var packageManager = parseResult.GetValue(PackageManagerOption);
```

This is how workloads read their own options without the CLI needing to know about them.

`func init` and `func run` / `func start` group contributed options under `Stack.Descriptor.DisplayName`. Stack headings follow the general `Options` section in ordinal, case-insensitive alphabetical order; options within each group retain their registration order. Return the canonical options from the registry so shared options remain in the general `Options` section without duplicates. Built-in and bundle options also stay in that general section.

## Testing

The contract is plain DI, so tests don't need the host:

```csharp
[Fact]
public void Configure_RegistersInitializer()
{
    var services = new ServiceCollection();
    var builder = Substitute.For<FunctionsCliBuilder>();
    builder.Services.Returns(services);
    var descriptor = new StackDescriptor("node", "node", "Node.js");

    new NodeWorkload(descriptor).Configure(builder);

    using var sp = services.BuildServiceProvider();
    var initializer = Assert.Single(sp.GetServices<IProjectInitializer>());
    Assert.IsType<NodeProjectInitializer>(initializer);
    Assert.Same(descriptor, initializer.Stack.Descriptor);
}
```

Use NSubstitute to verify `AddTemplateComponent<T>()` or instance registrations
without starting the host. Test processors directly with a `PostActionContext`
and substitute their I/O dependencies. The CLI's integration tests cover
predicate selection, overlap rejection, core precedence, and isolated assembly loading.

Initializers are themselves easy to unit-test: drive them with a temp directory and a synthetic `ParseResult`.

## Debugging

Workloads load into the CLI process via `LoadFromAssemblyPath`, so the
debugger steps into them as long as the workload's PDB sits next to its DLL
in the install directory.

The `DeployForDebug` MSBuild target (in `eng/build/WorkloadDebug.targets`,
imported by every project) handles the build + copy. It's safe to invoke on
any project; it skips projects that do not set `PackAsWorkload=true`.

```bash
dotnet build src/Workloads/Stacks/Node/Workloads.Node.csproj -t:DeployForDebug \
    -p:FuncCliWorkloadsHome=$PWD/.debug-workloads
```

The home is resolved as `$(FuncCliWorkloadsHome)` property, then
`FUNC_CLI_WORKLOADS_HOME` env var, then `$(RepoRoot).debug-workloads`.
For a multi-RID workload such as the Functions host, pass `-r <rid>` (for
example, `-r win-x64`). Only the selected implementation is installed, not
the outer pointer package.

### Two modes

By default `DeployForDebug` is **deploy-only**: it errors if no install of
`<id>@<version>` exists at the resolved home. This is intentional. The
install state of the home is part of what you're debugging (e.g. "what does
`func run` do when only the stack is installed and the worker is missing?"),
so the target won't silently mutate it.

Pass `-p:FuncCliEnsureWorkloadInstalled=true` to opt into a one-time pack +
`func workload install` when the install is missing. Subsequent runs
detect the install and skip straight to the copy.

Publisher verification also applies to that install step. For unsigned local
builds, explicitly preinstall the package with
`func workload install <path-to-nupkg> --allow-untrusted` in the same
`FUNC_CLI_WORKLOADS_HOME` first. The debug target does not bypass publisher trust.

If the installed `workload.json` is missing or differs from the build, both
modes stop without changing the installation. After changing a stack ID,
worker runtime, title or description, use a new package version or point
`FuncCliWorkloadsHome` at a fresh debug home.

### VS Code launch configs

Two profiles wrap the two modes:

- **Run func cli (debug workload)** uses `-t:DeployForDebug` only. Expects
  you to have set up the home you want to test against.
- **Run func cli (debug workload, auto-install)** adds
  `-p:FuncCliEnsureWorkloadInstalled=true`. Good for the first iteration on
  a fresh `.debug-workloads/` when the package satisfies publisher trust.
  For unsigned local builds, use the explicit preinstall step above.

Both pick a stack csproj (Node / Python / DotNet / Go) and point
`FUNC_CLI_WORKLOADS_HOME` at `${workspaceFolder}/.debug-workloads`.
Breakpoints in both Func and the workload source bind once the workload
loads.

> The picker only lists stack workloads, since the content-only packages
> (host, workers, extension bundles) have no managed entry assembly to step
> into.

### Bootstrapping richer scenarios

The auto-install mode only installs the workload you picked. For end-to-end
runs (`func run`) you also need a host and the matching language worker
installed, since workload installs are not transitive today. Three recipes:

- **Rebuild preinstalled workloads**: `eng/scripts/debug-workloads.ps1`
  drives `DeployForDebug` for workloads in the repo, using
  `.debug-workloads/` so VS Code F5 finds them. For unsigned local builds,
  first pack and explicitly install every workload you intend to deploy with
  `func workload install <path-to-nupkg> --allow-untrusted`, using
  `FUNC_CLI_WORKLOADS_HOME` set to that same directory. Use the file-based
  recipe below as a starting point.
  ```bash
  pwsh ./eng/scripts/debug-workloads.ps1                # rebuild all preinstalled workloads
  pwsh ./eng/scripts/debug-workloads.ps1 -Project Node  # iterate on one preinstalled workload
  ```
  An existing install of each workload's current package ID/version is
  required for the copy-only path. Missing installs trigger strict installation;
  the script does not bypass publisher trust. `-Clean` removes the workloads
  home before attempting installation, so it cannot bootstrap unsigned local
  builds. After cleaning the home, explicitly preinstall those packages again
  before rerunning without `-Clean`.
- **Single language, file-based**: pack and install just what you need.
  ```bash
  dotnet pack src/Workloads/Host/Workloads.Host.csproj                -c Debug -o /tmp/funcpkgs
  dotnet pack src/Workloads/Workers/Node/Workloads.Workers.Node.csproj -c Debug -o /tmp/funcpkgs
  export FUNC_CLI_WORKLOADS_HOME="$PWD/.debug-workloads"
  ./out/bin/Func/debug/func workload install /tmp/funcpkgs/Azure.Functions.Cli.Workloads.Host.*.nupkg --allow-untrusted
  ./out/bin/Func/debug/func workload install /tmp/funcpkgs/Azure.Functions.Cli.Workloads.Workers.Node.*.nupkg --allow-untrusted
  ```
- **All workloads via a local feed**: see the `build-workloads` skill in
  `.github/skills/build-workloads/`. Use this when what you're testing is
  `func workload install` itself (feed resolution, manifest behaviour, etc.).
  Otherwise the script above is faster.

Once bootstrapped, every F5 redeploys only the workload you're iterating on
and leaves the rest of the home untouched.

## Solution + CI

Add the project (and its test project) to the solution:

```bash
dotnet sln add src/Workloads/<kind>/Node/Workloads.Node.csproj
dotnet sln add test/Workloads/<kind>/Node.Tests/Workloads.Node.Tests.csproj
```

For ungrouped workloads, omit the `<kind>/` segment from the solution paths.

After adding the projects, ensure `Azure.Functions.Cli.slnx` places them in
solution folders that mirror the filesystem hierarchy:

```xml
<Folder Name="/src/Workloads/<kind>/">
  <Project Path="src/Workloads/<kind>/Node/Workloads.Node.csproj" />
</Folder>
<Folder Name="/test/Workloads/<kind>/">
  <Project Path="test/Workloads/<kind>/Node.Tests/Workloads.Node.Tests.csproj" />
</Folder>
```

For ungrouped workloads, use `/src/Workloads/` and `/test/Workloads/` as the
solution folders. Do not leave grouped workload projects under only `/src/` or
`/test/`.

Workloads share a single CI job template, `eng/ci/templates/jobs/build-workload.yml`, parameterised by `WorkloadProjectName`. Each workload owns two thin pipeline files that extend the 1ES template and pass that parameter:

- `eng/ci/workloads/<name>/public-build.yml` — 1ES Unofficial template (PR + branch builds).
- `eng/ci/workloads/<name>/official-build.yml` — 1ES Official template (push + release branches, with the pack stage).

Both should set path filters to scope triggers to:

- `src/Abstractions/**`
- `src/Workloads/<kind>/<Name>/**`
- `test/Workloads/<kind>/<Name>.Tests/**`
- `eng/ci/templates/jobs/build-workload.yml`
- The pipeline file itself

For ungrouped workloads, omit the `<kind>/` segment from the path filters.

Use the Python pipelines (`eng/ci/workloads/python/{public,official}-build.yml`) as the reference. New workloads should not introduce per-workload job/step templates: `build-workload.yml` already covers build, test, and pack.

## Checklist

- [ ] New project `src/Workloads/<kind>/<Name>/Workloads.<Name>.csproj`, packs as `PackageType=FuncCliWorkload`, references `Abstractions` with `PrivateAssets=all` / `ExcludeAssets=runtime`
- [ ] `Directory.Version.props` and `release_notes.md` next to the csproj
- [ ] `README.md` next to the csproj (auto-packed by `Release.props` as `<PackageReadmeFile>`)
- [ ] Generated `workload.json` in the package, and stack metadata declared in the project if it provides a stack
- [ ] Subclass of `Workload` with a `CliWorkload` assembly attribute, a `Configure` implementation, and either a public parameterless constructor or a public `StackDescriptor` constructor for stack workloads
- [ ] `Configure(FunctionsCliBuilder)` null-checks `builder` and registers an `IProjectInitializer` and/or top-level commands via `builder.RegisterCommand(...)`
- [ ] `IProjectInitializer.GetInitOptions(IInitOptionRegistry registry)` registers any extra options the initializer needs via `registry.GetOrAdd(...)` and returns the canonical instances (return `[]` for stubs; only throw from `InitializeAsync`)
- [ ] Test project `test/Workloads/<kind>/<Name>.Tests/Workloads.<Name>.Tests.csproj`, assembly name `Azure.Functions.Cli.Workloads.<Name>.Tests` (matches the workload's `InternalsVisibleTo`)
- [ ] Both projects added to `Azure.Functions.Cli.slnx` under solution folders that mirror their source and test paths
- [ ] `eng/ci/workloads/<name>/{public,official}-build.yml` extending `eng/ci/templates/jobs/build-workload.yml` with `WorkloadProjectName: <Name>`
- [ ] `dotnet build` and `dotnet test` pass
