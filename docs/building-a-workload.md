# Building a New Workload

This guide walks through building a workload for the Azure Functions Core Tools v5 CLI. A workload is a NuGet package that the CLI loads at runtime to extend its behavior, most commonly to provide `func init` / `func new` support for a specific language stack (e.g. Node.js, Python, Java), but a workload can also contribute brand-new subcommands.

> **Status**: the abstractions, DI host, and `func workload install` / `uninstall` commands described below are in the tree as of this PR. Workloads are installed from a local `.nupkg` on disk; NuGet feed acquisition lands in a follow-up.
>
> **Spec**: this guide is the authoring view. The on-disk and on-feed layout, the `workload.json` schema, the `kind` discriminator (`workload` / `content` / `meta`), and the install pipeline are specified in [`docs/proposed/workload-package-layout.md`](./proposed/workload-package-layout.md). Consult that doc for the contract; this guide stays focused on the happy-path authoring experience for `kind: workload`.

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

1. Create a class library project under `src/Workloads/<kind>/<Name>/` that targets `net10.0` and packs as `PackageType=FuncCliWorkload`; omit `<kind>/` for workloads that do not belong to a grouping
2. Reference `Azure.Functions.Cli.Abstractions` (with `PrivateAssets=all` and `ExcludeAssets=runtime` so it isn't shipped inside your package)
3. Subclass `Workload` and override `DisplayName`, `Description`, and `Configure`
4. Inside `Configure`, register an `IProjectInitializer`, an `IProjectDetector` (so the resolver can claim directories owned by your workload), and/or top-level commands via `builder.RegisterCommand(...)` plus any supporting services
5. Add a `workload.json` next to your csproj describing the entry-point assembly and type
6. Add a `README.md` next to the csproj — `Release.props` auto-includes it as `<PackageReadmeFile>` so it surfaces on NuGet feeds and in `func workload list`
7. Build, package, and install with `func workload install <path-to-nupkg>`

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
    <TargetFramework>net10.0</TargetFramework>
    <Title>Node.js</Title>
    <Description>Azure Functions CLI tooling for Node.js projects.</Description>
    <PackageType>FuncCliWorkload</PackageType>
    <PackageTags>kind:workload alias:node alias:javascript alias:typescript func-workload</PackageTags>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <NoWarn>$(NoWarn);NU5128;NU5100</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Azure.Functions.Cli.Workloads.Node.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="$(SrcRoot)Abstractions/Abstractions.csproj">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="workload.json" Pack="true" PackagePath="/" />
    <None Include="$(OutputPath)$(AssemblyName).dll" Pack="true" PackagePath="tools/any/" Visible="false" />
  </ItemGroup>

</Project>
```

What each piece does:

- `PackageType=FuncCliWorkload` is how the CLI's catalog discovers workload packages (see `docs/proposed/workload-package-layout.md` §5, §7).
- `Title` is the display name surfaced by NuGet feed UIs; `Description` is the one-line summary. The `Workload` class's `DisplayName` / `Description` overrides serve the same purpose for `func workload list` (no duplication: feed metadata vs. running CLI).
- `PackageTags` should include exactly one `kind:<workload|content|meta>` tag matching `workload.json`'s `kind`, plus one or more `alias:<name>` tags so `func workload install <alias>` resolves. `func-workload` is recommended for generic feed UI discoverability.
- `IncludeBuildOutput=false` + the explicit `<None Include="$(OutputPath)$(AssemblyName).dll" ... PackagePath="tools/any/" />` puts the workload assembly under `tools/any/` instead of `lib/`, which is where the loader looks.
- `<None Include="workload.json" ... PackagePath="/" />` ships the manifest at the package root.
- `SuppressDependenciesWhenPacking=true` plus `PrivateAssets=all` / `ExcludeAssets=runtime` on the `Abstractions` reference keep the workload self-contained: the CLI provides Abstractions (and the other host-shared contract assemblies, see §9.2 of the layout spec) at runtime. The same `PrivateAssets=all` rule applies to **every** `<PackageReference>` you add later, not just `Abstractions`.
- `NU5128`/`NU5100` are suppressed because we deliberately ship without `lib/` and place files under `tools/any/`.

> **Pack scope today.** The csproj above packs only the workload assembly itself. That works for stubs and for workloads whose runtime closure is just the host-shared contracts. As soon as a workload pulls in a transitive managed dependency (e.g., `Newtonsoft.Json`), the long-term shape from the package-layout spec applies: the package must contain the publish output (workload `.dll`, `.deps.json`, optional `.pdb`, every transitive managed dep, and any `runtimes/<rid>/` assets the deps ship). The upcoming `Workload.Sdk` package will provide the publish-into-`tools/any/` target; until then, workloads with transitive deps need to wire that step manually. See `docs/proposed/workload-package-layout.md` §5 and §9.

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

Add a `workload.json` that points at your entry-point type. `assemblyPath` is **relative to `tools/any/`**, conventionally a bare filename (no leading `/`, no `..`); `type` is the FQN of your `Workload` subclass.

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "workload",
  "entryPoint": {
    "assemblyPath": "Azure.Functions.Cli.Workloads.Node.dll",
    "type": "Azure.Functions.Cli.Workloads.Node.NodeWorkload"
  }
}
```

Add a `README.md` next to the csproj. `Release.props` auto-wires it as `<PackageReadmeFile>` (and the Functions icon as `<PackageIcon>`), so both surface on NuGet feeds without extra packing config. Use it to describe the workload's purpose, the language(s) it supports, and any prerequisites.

## Step 2: Subclass `Workload`

`Workload` is an abstract class in `Azure.Functions.Cli.Abstractions` (under `Azure.Functions.Cli.Workloads`). The CLI loader instantiates the type named in `workload.json` and calls `Configure`. **It must have a parameterless constructor.**

```csharp
// NodeWorkload.cs
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Entry-point for the Node.js workload. Registers the project initializer
/// and any Node-specific services.
/// </summary>
public sealed class NodeWorkload : Workloads.Workload
{
    public override string DisplayName => "Node.js";

    public override string Description => "Azure Functions CLI tooling for Node.js projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, NodeProjectInitializer>();
        // builder.RegisterCommand<NodeTopLevelCommand>();
        // …any other services your providers depend on
    }
}
```

`DisplayName` and `Description` surface in `func workload list`. Package id and version aren't on the class: they come from the csproj/`Directory.Version.props` (single source of truth) and are persisted in the global registry at install time.

## Step 3: Implement IProjectInitializer

`IProjectInitializer` extends `func init`. Each registered initializer owns a stack, contributes any extra options it needs, and scaffolds the project.

```csharp
// NodeProjectInitializer.cs
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workloads.Node;

internal sealed class NodeProjectInitializer : IProjectInitializer
{
    public string Stack => "node";

    public IReadOnlyList<string> SupportedLanguages => ["JavaScript", "TypeScript"];

    public bool CanHandle(string stack) =>
        stack.Equals("node",       StringComparison.OrdinalIgnoreCase) ||
        stack.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
        stack.Equals("typescript", StringComparison.OrdinalIgnoreCase);

    // Workload-specific options. Registered through the supplied IInitOptionRegistry,
    // which is what lets options like `--no-bundle` that multiple stacks contribute
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
        var language       = context.Language ?? "JavaScript";
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

`func init` (and other built-in commands) pass the workload a context object. Common inputs live on the `WorkloadContext` base record; command-specific inputs live on the derived record.

| Type | Provides |
|------|----------|
| `WorkloadContext` (base) | `WorkingDirectory` — the directory (`DirectoryInfo` + `WasExplicit` flag) the command is operating from. The CLI guarantees `WorkingDirectory.Info` exists before invoking your initializer, so you don't need to call `Directory.CreateDirectory`. Stack-specific concepts like dotnet's `.csproj` are not modelled here; workloads add their own options for those. |
| `InitContext` | `ProjectName`, `Language`, `Force` |

Workload-specific option values flow through the `ParseResult`, so the CLI never has to know about your options.

## Step 4 (optional): Contribute New Commands

For "feature" workloads that aren't tied to a stack — say a Durable Functions workload — register top-level commands through `builder.RegisterCommand(...)`. Workload commands derive from `FuncCommand` (in `Azure.Functions.Cli.Workloads`), which is parser-independent: you describe the command's name, description, options, arguments, and subcommands using lightweight descriptors, and read parsed values through a `FuncCommandInvocationContext` at execution time. The CLI tracks which workload each command came from for diagnostics and collision reporting.

```csharp
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

public sealed class DurableWorkload : Workloads.Workload
{
    public override string DisplayName => "Durable Functions";

    public override string Description => "Durable Functions management commands.";

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

## Reading Workload-Specific Options

Built-in commands collect options from every registered provider during command construction, so they appear in `--help` automatically. Inside `InitializeAsync` (or any other provider method), read your option values from the supplied `ParseResult`:

```csharp
var packageManager = parseResult.GetValue(PackageManagerOption);
```

This is how workloads read their own options without the CLI needing to know about them.

## Testing

The contract is plain DI, so tests don't need the host:

```csharp
[Fact]
public void Configure_RegistersInitializer()
{
    var services = new ServiceCollection();
    var builder  = new TestBuilder(services);

    new NodeWorkload().Configure(builder);

    using var sp = services.BuildServiceProvider();
    var initializers = sp.GetServices<IProjectInitializer>().ToList();
    Assert.Single(initializers);
    Assert.IsType<NodeProjectInitializer>(initializers[0]);
}

private sealed class TestBuilder(IServiceCollection services) : FunctionsCliBuilder
{
    public override IServiceCollection Services { get; } = services;
}
```

Initializers are themselves easy to unit-test: drive them with a temp directory and a synthetic `ParseResult`.

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
- [ ] `workload.json` next to the csproj, listing the entry-point `assemblyPath` and `type`
- [ ] Subclass of `Workload` with a parameterless constructor, overriding `DisplayName`, `Description`, and `Configure`
- [ ] `Configure(FunctionsCliBuilder)` null-checks `builder` and registers an `IProjectInitializer` and/or top-level commands via `builder.RegisterCommand(...)`
- [ ] `IProjectInitializer.GetInitOptions(IInitOptionRegistry registry)` registers any extra options the initializer needs via `registry.GetOrAdd(...)` and returns the canonical instances (return `[]` for stubs; only throw from `InitializeAsync`)
- [ ] Test project `test/Workloads/<kind>/<Name>.Tests/Workloads.<Name>.Tests.csproj`, assembly name `Azure.Functions.Cli.Workloads.<Name>.Tests` (matches the workload's `InternalsVisibleTo`)
- [ ] Both projects added to `Azure.Functions.Cli.slnx` under solution folders that mirror their source and test paths
- [ ] `eng/ci/workloads/<name>/{public,official}-build.yml` extending `eng/ci/templates/jobs/build-workload.yml` with `WorkloadProjectName: <Name>`
- [ ] `dotnet build` and `dotnet test` pass
