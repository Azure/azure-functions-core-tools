# Agent Instructions

## Repository Overview

Azure Functions Core Tools v5 CLI — rebuilt with System.CommandLine, Spectre.Console,
and .NET 10 using a workload-based extensibility model (similar to `dotnet` CLI workloads).

## Adding a New Command

See `docs/adding-a-command.md` for the full guide. Summary:

1. Create a class in `src/Func.Cli/Commands/` extending `BaseCommand`
2. Register it in `Parser.cs`
3. Add tests in `test/Func.Cli.Tests/Commands/`
4. Update `docs/cli-architecture.md` command tree

## Creating a New Workload

See `docs/building-a-workload.md` for the interface contracts. Below is the full
checklist for scaffolding a new workload project. Use the dotnet workload as the
reference implementation.

### Workload Project Checklist

Replace `<Name>` with the workload name (e.g., `Node`, `Python`, `Java`).
Replace `<name>` with the lowercase form (e.g., `node`, `python`, `java`).

#### 1. Source project — `src/Func.Cli.Workload.<Name>/`

Create the following files:

- [ ] `Func.Cli.Workload.<Name>.csproj`
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <RootNamespace>Azure.Functions.Cli.Workload.<Name></RootNamespace>
      <AssemblyName>Azure.Functions.Cli.Workload.<Name></AssemblyName>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <IsPackable>true</IsPackable>
      <PackageId>Azure.Functions.Cli.Workload.<Name></PackageId>
      <Description>Azure Functions CLI <name> workload — ...</Description>
    </PropertyGroup>
    <ItemGroup>
      <InternalsVisibleTo Include="Func.Cli.Workload.<Name>.Tests" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\Func.Cli.Abstractions\Func.Cli.Abstractions.csproj" />
    </ItemGroup>
  </Project>
  ```
- [ ] `Directory.Build.props` — imports parent props:
  ```xml
  <Project>
    <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  </Project>
  ```
- [ ] `Directory.Build.targets` — imports parent targets:
  ```xml
  <Project>
    <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" />
  </Project>
  ```
- [ ] `Directory.Version.props` — workload version:
  ```xml
  <Project>
    <PropertyGroup>
      <VersionPrefix>1.0.0</VersionPrefix>
      <VersionSuffix>preview.1</VersionSuffix>
      <UpdateBuildNumber>true</UpdateBuildNumber>
    </PropertyGroup>
  </Project>
  ```
- [ ] `release_notes.md` — initial release notes
- [ ] `<Name>Workload.cs` — class implementing `IWorkload`
- [ ] At least one of: `IProjectInitializer`, `ITemplateProvider`, `IPackProvider` implementations

#### 2. Test project — `test/Func.Cli.Workload.<Name>.Tests/`

- [ ] `Func.Cli.Workload.<Name>.Tests.csproj`
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <RootNamespace>Azure.Functions.Cli.Workload.<Name>.Tests</RootNamespace>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <IsPackable>false</IsPackable>
      <IsTestProject>true</IsTestProject>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" />
      <PackageReference Include="xunit" />
      <PackageReference Include="xunit.runner.visualstudio">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        <PrivateAssets>all</PrivateAssets>
      </PackageReference>
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\src\Func.Cli.Workload.<Name>\Func.Cli.Workload.<Name>.csproj" />
    </ItemGroup>
  </Project>
  ```
- [ ] `Directory.Build.props` and `Directory.Build.targets` (same parent-import pattern)
- [ ] Contract tests for the `IWorkload` implementation
- [ ] Unit tests for each provider (initializer, template, pack)

#### 3. Solution file — `Azure.Functions.Cli.sln`

- [ ] Add both projects to the solution:
  ```
  dotnet sln add src/Func.Cli.Workload.<Name>/Func.Cli.Workload.<Name>.csproj
  dotnet sln add test/Func.Cli.Workload.<Name>.Tests/Func.Cli.Workload.<Name>.Tests.csproj
  ```

#### 4. CI pipelines

Create two pipeline files in `eng/ci/`:

- [ ] `workload-<name>-public-build.yml` — 1ES Unofficial template
  - Path filters: `src/Func.Cli.Abstractions/**`, `src/Func.Cli.Workload.<Name>/**`,
    `test/Func.Cli.Workload.<Name>.Tests/**`, plus CI template paths
  - Build & test stage on Windows + Linux

- [ ] `workload-<name>-official-build.yml` — 1ES Official template
  - Same path filters plus `release/*` branch and pack template path
  - Build & test stage + NuGet pack stage
  - `pr: none` (official builds only trigger on push)

Create CI templates:

- [ ] `eng/ci/templates/jobs/test-workload-<name>.yml` — job template
- [ ] `eng/ci/templates/steps/run-workload-<name>-tests.yml` — test step
- [ ] `eng/ci/templates/official/jobs/pack-workload-<name>.yml` — NuGet pack job

Use the dotnet workload CI files as templates.

#### 5. Base CLI updates — `src/Func.Cli/`

In `src/Func.Cli/Workloads/WorkloadManager.cs`:

- [ ] Add short alias to `_wellKnownAliases` dictionary:
  ```csharp
  ["<name>"] = "Azure.Functions.Cli.Workload.<Name>",
  ```
- [ ] Add entry to `_workloadCatalog` array:
  ```csharp
  new("<name>", "Azure.Functions.Cli.Workload.<Name>", "<Display Name>", "<Languages>"),
  ```

If the workload has unique project files (e.g., `go.mod`, `Cargo.toml`):
- [ ] Add detection in `src/Func.Cli/Commands/ProjectDetector.cs`

#### 6. Documentation

- [ ] `docs/repo-structure.md` — add new project directories, CI pipeline entries
- [ ] `docs/building-a-workload.md` — update if new patterns are introduced

#### 7. Verify

```bash
dotnet restore
dotnet build
dotnet test
```

## Build & Test Commands

```bash
dotnet restore                    # Restore NuGet packages
dotnet build                      # Build all projects
dotnet test                       # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName"  # Run specific tests
```

## Project Conventions

- **Folder names**: short form — `Func.Cli`, `Func.Cli.Abstractions`, `Func.Cli.Workload.<Name>`
- **Assembly/namespace names**: full form — `Azure.Functions.Cli`, `Azure.Functions.Cli.Workloads`
- **Package IDs**: full form — `Azure.Functions.Cli.Workload.<Name>`
- **CI pipelines**: `workload-<name>-public-build.yml` and `workload-<name>-official-build.yml`
- **Tests**: xUnit with NSubstitute for mocking, `FakeDotnetCliRunner` pattern for CLI wrappers
- **Error handling**: throw `GracefulException` for user-facing errors (caught in Program.cs)
- **Cancellation**: all async methods accept `CancellationToken`, pass through to child operations
