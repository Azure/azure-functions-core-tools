# Building a New Workload

This guide walks through building a new language workload for the Azure Functions Core Tools v5 CLI. A workload provides `func init` and `func new` support for a specific worker runtime (e.g., Node.js, Python, Java).

## AI-Assisted Scaffolding

You can use GitHub Copilot or another AI coding agent to scaffold a new workload.
The repo includes agent instructions (`AGENTS.md`) with the
full project setup checklist. Try a prompt like:

> Scaffold a new Node.js workload for the Azure Functions CLI. Follow the workload
> checklist in the agent instructions and use the dotnet workload as a reference.
> The workload should support func init and func new for Node.js/TypeScript projects.

## Architecture

```
┌─────────────────────────────────┐
│  Func.Cli (the CLI executable)  │
│  Loads workloads at runtime     │
│  via AssemblyLoadContext         │
└──────────┬──────────────────────┘
           │ references (compile-time)
           ▼
┌─────────────────────────────────┐
│  Func.Cli.Abstractions          │
│  NuGet package with interfaces  │
│  IWorkload, ITemplateProvider,  │
│  IProjectInitializer            │
└──────────┬──────────────────────┘
           │ referenced by (compile-time)
           ▼
┌─────────────────────────────────┐
│  Your Workload                  │
│  e.g., Func.Cli.Workload.Node  │
│  NuGet package, loaded at       │
│  runtime via reflection          │
└─────────────────────────────────┘
```

**Key principle**: Workloads reference only `Func.Cli.Abstractions`, never `Func.Cli`. The CLI loads workloads dynamically — there is no compile-time dependency from the CLI to any workload.

## Quick Start

1. Create a new class library project
2. Reference `Func.Cli.Abstractions`
3. Implement `IWorkload`, `IProjectInitializer`, `ITemplateProvider`, and optionally `IPackProvider`
4. Build, package, and install

## Step 1: Create the Project

```bash
mkdir src/Func.Cli.Workload.Node
cd src/Func.Cli.Workload.Node
```

Create the `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Azure.Functions.Cli.Workload.Node</AssemblyName>
    <RootNamespace>Azure.Functions.Cli.Workload.Node</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Func.Cli.Abstractions\Func.Cli.Abstractions.csproj" />
  </ItemGroup>

  <!-- For test access to internals -->
  <ItemGroup>
    <InternalsVisibleTo Include="Func.Cli.Workload.Node.Tests" />
  </ItemGroup>

</Project>
```

Add the standard build files (copy from an existing workload project):
- `Directory.Build.props`
- `Directory.Build.targets`
- `Directory.Version.props`

## Step 2: Implement IWorkload

This is the main entry point the CLI discovers via reflection. **It must have a parameterless constructor.**

```csharp
// NodeWorkload.cs
using System.CommandLine;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Node;

public class NodeWorkload : IWorkload
{
    public string Id => "node";
    public string Name => "Node.js";
    public string Description => "Azure Functions Node.js worker support";

    private readonly NodeTemplateProvider _templateProvider = new();
    private readonly NodeProjectInitializer _initializer = new();

    public void RegisterCommands(Command rootCommand)
    {
        // Optional: add workload-specific commands to the CLI tree
        // e.g., rootCommand.Subcommands.Add(new NodePackCommand());
    }

    public IReadOnlyList<ITemplateProvider> GetTemplateProviders() => [_templateProvider];

    public IProjectInitializer? GetProjectInitializer() => _initializer;

    // Optional: provide a pack provider for `func pack`
    // public IPackProvider? GetPackProvider() => new NodePackProvider();
}
```

## Step 3: Implement IProjectInitializer

Handles `func init --worker-runtime node`. Creates the project structure.

```csharp
// NodeProjectInitializer.cs
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Node;

public class NodeProjectInitializer : IProjectInitializer
{
    public string WorkerRuntime => "node";

    public IReadOnlyList<string> SupportedLanguages => ["JavaScript", "TypeScript"];

    // Optional: add workload-specific options to func init
    public static readonly Option<string> PackageManagerOption = new("--package-manager")
    {
        Description = "The package manager to use (npm, yarn, pnpm)",
        DefaultValueFactory = _ => "npm"
    };

    public IReadOnlyList<Option> GetInitOptions() => [PackageManagerOption];

    public bool CanHandle(string workerRuntime) =>
        workerRuntime.Equals("node", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("typescript", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync(
        ProjectInitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        var packageManager = parseResult.GetValue(PackageManagerOption) ?? "npm";
        var language = context.Language ?? "JavaScript";
        var projectPath = context.ProjectPath;

        Directory.CreateDirectory(projectPath);

        // Create package.json
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "package.json"),
            GeneratePackageJson(context.ProjectName ?? "my-functions-app"),
            cancellationToken);

        // Create host.json
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "host.json"),
            """{ "version": "2.0" }""",
            cancellationToken);

        // Create local.settings.json
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "local.settings.json"),
            GenerateLocalSettings(),
            cancellationToken);

        // If TypeScript, add tsconfig.json
        if (language.Equals("TypeScript", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "tsconfig.json"),
                GenerateTsConfig(),
                cancellationToken);
        }
    }

    // ... helper methods to generate file contents ...
}
```

### Init Options

Options returned by `GetInitOptions()` are automatically added to `func init` when your workload is loaded. They appear in help output with a `[node]` prefix:

```
  --package-manager    [node] The package manager to use (npm, yarn, pnpm)
```

### ProjectInitContext

The `ProjectInitContext` record provides:
- `ProjectPath` — target directory
- `ProjectName` — from `--name` or directory name
- `Language` — from `--language` or user prompt
- `Force` — `--force` flag

### ParseResult

The `ParseResult` gives access to your custom options via `parseResult.GetValue(MyOption)`. This is how workloads read their own option values without the CLI needing to know about them.

## Step 4: Implement ITemplateProvider

Handles `func new` — provides function templates and scaffolds them.

```csharp
// NodeTemplateProvider.cs
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Node;

public class NodeTemplateProvider : ITemplateProvider
{
    public string WorkerRuntime => "node";

    private static readonly FunctionTemplate[] _templates =
    [
        new("HttpTrigger", "A function triggered by an HTTP request", "node"),
        new("TimerTrigger", "A function triggered on a schedule", "node"),
        new("QueueTrigger", "A function triggered by a queue message", "node"),
        // ... more templates
    ];

    public Task<IReadOnlyList<FunctionTemplate>> GetTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<FunctionTemplate>>(_templates);
    }

    public async Task ScaffoldAsync(
        FunctionScaffoldContext context,
        CancellationToken cancellationToken = default)
    {
        var templateContent = GetTemplateContent(context.TemplateName, context.Language);
        var extension = context.Language?.Equals("TypeScript", StringComparison.OrdinalIgnoreCase)
            == true ? ".ts" : ".js";

        var functionDir = Path.Combine(context.OutputPath, "src", "functions");
        Directory.CreateDirectory(functionDir);

        var filePath = Path.Combine(functionDir, $"{context.FunctionName}{extension}");
        await File.WriteAllTextAsync(filePath, templateContent, cancellationToken);
    }

    // ... template content generation ...
}
```

### FunctionScaffoldContext

The `FunctionScaffoldContext` record provides:
- `TemplateName` — selected template (e.g., "HttpTrigger")
- `FunctionName` — user-chosen function name
- `OutputPath` — project directory
- `Language` — programming language (may be null)
- `AuthLevel` — authorization level (may be null, for workloads that support it)

## Step 5: (Optional) Implement IPackProvider

If your workload needs custom build/publish logic for `func pack`, implement `IPackProvider`:

```csharp
public class NodePackProvider : IPackProvider
{
    public Task<string?> ValidateAsync(PackContext context, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(context.ProjectPath, "package.json")))
            return Task.FromResult<string?>("No package.json found.");
        return Task.FromResult<string?>(null);
    }

    public async Task<string> PrepareAsync(PackContext context, CancellationToken ct)
    {
        if (context.NoBuild)
            return context.ProjectPath;

        // Run npm install + npm run build
        var outputDir = Path.Combine(context.ProjectPath, "dist");
        // ... build logic ...
        return outputDir;  // directory to zip
    }

    public Task CleanupAsync(PackContext context, CancellationToken ct)
    {
        // Optional: clean up build artifacts
        return Task.CompletedTask;
    }

    public IReadOnlyList<Option> GetPackOptions() => [];
}
```

Then wire it in your `IWorkload`:

```csharp
public IPackProvider? GetPackProvider() => new NodePackProvider();
```

### PackContext

The `PackContext` record provides:
- `ProjectPath` — project directory
- `OutputPath` — output directory for the zip (may be null for default)
- `NoBuild` — if true, skip build and package the project directory as-is
- `AdditionalArgs` — extra arguments from the command line

The zip step is handled by the CLI (not the provider) using `System.IO.Compression.ZipFile`, ensuring consistent behavior across all workloads.

## Feature Workloads

Not all workloads provide language support. **Feature workloads** (like Durable Functions or Kubernetes) add new commands rather than init/new/pack providers:

```csharp
public class DurableWorkload : IWorkload
{
    public string Id => "durable";
    public string Name => "Durable Functions";
    public string Description => "Durable Functions management commands";

    public void RegisterCommands(Command rootCommand)
    {
        var durableCommand = new Command("durable", "Manage Durable Functions");
        durableCommand.Subcommands.Add(new Command("get-instances", "List orchestration instances"));
        durableCommand.Subcommands.Add(new Command("start-new", "Start a new orchestration"));
        // ... more subcommands
        rootCommand.Subcommands.Add(durableCommand);
    }

    // All other IWorkload methods have default implementations that return
    // empty/null — feature workloads don't need to override them.
}
```

This produces: `func durable get-instances`, `func durable start-new`, etc.

## Step 6: Add to the Solution and CI

### Solution

```bash
dotnet sln add src/Func.Cli.Workload.Node/Func.Cli.Workload.Node.csproj --solution-folder src
```

### CI Pipeline

Create `eng/ci/workload-node-build.yml` with path-scoped triggers. See `eng/ci/workload-dotnet-build.yml` as a reference.

### Workload Alias

To enable `func workload install node` (instead of the full package ID), add an entry to `WorkloadManager._wellKnownAliases` in `src/Func.Cli/Workloads/WorkloadManager.cs`:

```csharp
private static readonly Dictionary<string, string> _wellKnownAliases = new(StringComparer.OrdinalIgnoreCase)
{
    ["dotnet"] = "Azure.Functions.Cli.Workload.Dotnet",
    ["node"] = "Azure.Functions.Cli.Workload.Node",       // ← already there
    // ...
};
```

## Step 7: Test

Create a test project at `test/Func.Cli.Workload.Node.Tests/`:

```bash
dotnet new xunit -o test/Func.Cli.Workload.Node.Tests
dotnet sln add test/Func.Cli.Workload.Node.Tests/ --solution-folder test
```

### What to Test

| Area | Test |
|------|------|
| Workload | `Id`, `Name`, `Description` are correct |
| Workload | `GetTemplateProviders()` returns the provider |
| Workload | `GetProjectInitializer()` returns the initializer |
| Initializer | `WorkerRuntime` is correct |
| Initializer | `CanHandle()` accepts expected runtime strings |
| Initializer | `SupportedLanguages` is correct |
| Initializer | `InitializeAsync()` creates expected files |
| Templates | `GetTemplatesAsync()` returns all templates |
| Templates | `ScaffoldAsync()` creates function files with correct content |

### Test Pattern

Use a fake/mock for external tools (see `FakeDotnetCliRunner` in the dotnet workload for reference):

```csharp
public class NodeProjectInitializerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NodeProjectInitializer _initializer;

    public NodeProjectInitializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-node-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _initializer = new NodeProjectInitializer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task InitializeAsync_CreatesPackageJson()
    {
        var context = new ProjectInitContext(_tempDir, "my-app", "JavaScript", false);
        // Create a ParseResult with default options
        var root = new System.CommandLine.RootCommand();
        root.Options.Add(NodeProjectInitializer.PackageManagerOption);
        var parseResult = root.Parse("");

        await _initializer.InitializeAsync(context, parseResult);

        Assert.True(File.Exists(Path.Combine(_tempDir, "package.json")));
    }
}
```

## Step 8: Build, Package, and Install

```bash
# Build
dotnet build src/Func.Cli.Workload.Node/

# Package as NuGet
dotnet pack src/Func.Cli.Workload.Node/

# Test locally
dotnet test test/Func.Cli.Workload.Node.Tests/
```

For local end-to-end testing, see [local-workload-testing.md](local-workload-testing.md).

## Reference: The Dotnet Workload

The `feature/dotnet-workload` branch contains a complete reference implementation. Key files:

| File | Purpose |
|------|---------|
| `DotnetWorkload.cs` | `IWorkload` implementation |
| `DotnetProjectInitializer.cs` | `func init` handler with `--target-framework` option |
| `DotnetTemplateProvider.cs` | 10 function templates, delegates to `dotnet new` |
| `DotnetPackProvider.cs` | `func pack` handler — runs `dotnet publish` |
| `IDotnetCliRunner.cs` | Abstraction over `dotnet` CLI for testability |
| `DotnetCliRunner.cs` | Real implementation that shells out to `dotnet` |
| `NuGetTemplateResolver.cs` | Resolves latest template versions from NuGet |
| `Templates.props` | MSBuild file that bundles template nupkgs for offline use |
| `BundledTemplateVersions.cs` | Reads bundled versions from assembly metadata |

## Checklist

- [ ] New project in `src/Func.Cli.Workload.<Name>/`
- [ ] References `Func.Cli.Abstractions` only (no reference to `Func.Cli`)
- [ ] Implements `IWorkload` with parameterless constructor
- [ ] Implements `IProjectInitializer` (for `func init`)
- [ ] Implements `ITemplateProvider` (for `func new`)
- [ ] (Optional) Implements `IPackProvider` (for `func pack`)
- [ ] Added to solution file
- [ ] CI pipeline in `eng/ci/workload-<name>-build.yml`
- [ ] Test project in `test/Func.Cli.Workload.<Name>.Tests/`
- [ ] Alias registered in `WorkloadManager._wellKnownAliases`
- [ ] Catalog entry added to `WorkloadManager._workloadCatalog`
- [ ] `ProjectDetector` updated if workload has unique project files
- [ ] `docs/repo-structure.md` updated with new projects and CI pipelines
- [ ] `Directory.Build.props`, `Directory.Build.targets`, `Directory.Version.props` created
- [ ] `release_notes.md` created in the workload project directory
- [ ] Public and official CI pipelines created in `eng/ci/`
- [ ] `dotnet build` and `dotnet test` pass
