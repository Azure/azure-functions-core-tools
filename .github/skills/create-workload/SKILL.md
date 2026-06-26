---
name: create-workload
description: 'Use when adding a new func CLI workload (e.g. Node, Python, Java). Scaffolds the source project, test project, solution entry, CI release pipeline, and docs.'
---

# Create a New Workload

See `docs/building-a-workload.md` for the authoring guide and rationale, and
`docs/proposed/workload-package-layout.md` for the package-layout spec
(`workload.json` schema, `kind` discriminator, install pipeline). Use the
Python stack workload (`src/Workloads/Stacks/Python/`) as the canonical
reference for stack workloads.

## Workload Project Checklist

Replace `<Name>` with the workload name (e.g., `Node`, `Python`, `Java`).
Replace `<name>` with the lowercase form (e.g., `node`, `python`, `java`).
Replace `<kind>` with the workload grouping when one applies (e.g. `Stacks`);
omit the `<kind>/` path segment for ungrouped workloads.

### 1. Source project

Place grouped workloads under `src/Workloads/<kind>/<Name>/`. Stack workloads
use `Stacks` as the kind, for example `src/Workloads/Stacks/Python/`. Workloads
without a grouping can live directly under `src/Workloads/<Name>/`.

Create the following files:

- [ ] `Workloads.<Name>.csproj`
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <Title><Name> Stack</Title>
      <Description>Azure Functions CLI tooling for <Name> projects.</Description>
      <PackAsWorkload>true</PackAsWorkload>
      <WorkloadKind>workload</WorkloadKind>
      <WorkloadAlias><name></WorkloadAlias>
    </PropertyGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="Azure.Functions.Cli.Workloads.<Name>.Tests" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="$(SrcRoot)Abstractions/Abstractions.csproj" />
    </ItemGroup>

  </Project>
  ```
  - `PackAsWorkload=true` triggers the SDK build targets in `eng/build/Workloads/` that handle packaging, `workload.json` generation, dependency filtering, and pack layout. Do **not** manually set `PackageType`, `PackageTags`, `IncludeBuildOutput`, or `SuppressDependenciesWhenPacking` — the SDK targets set all of these from `WorkloadKind` and `WorkloadAlias`.
  - `WorkloadKind` must be one of: `workload`, `content`, `meta`, `rid-pointer`. Stack workloads use `workload`.
  - `WorkloadAlias` is the short name users pass to `func workload install <alias>`.
  - `Title` is the feed-UI display name; `Description` is the one-line summary.
  - The Abstractions reference does **not** need `PrivateAssets`/`ExcludeAssets` — the SDK targets (`Workload.Pack.targets`) automatically exclude unified CLI assemblies from the package via the `ResolveWorkloadCopyLocal` task.
  - If the workload embeds template files, add `<EmbeddedResource Include="Templates/**" />`.
  - Csproj/assembly name is `Azure.Functions.Cli.Workloads.<Name>` (derived from the project filename by `eng/build/Engineering.props`).
- [ ] `Directory.Version.props`, the workload's version:
  ```xml
  <Project>
    <PropertyGroup>
      <VersionPrefix>1.0.0</VersionPrefix>
      <VersionSuffix>preview.1</VersionSuffix>
    </PropertyGroup>
  </Project>
  ```
- [ ] `release_notes.md`:
  ```markdown
  # Azure.Functions.Cli.Workloads.<Name>

  ## 1.0.0-preview.1

  - Initial scaffold of the <Name> workload (entry point + stub project initializer).
  ```
- [ ] `README.md` next to the csproj. `Release.props` auto-includes it as `<PackageReadmeFile>` (and the Functions icon as `<PackageIcon>`), so both surface on NuGet feeds. Describe the workload's purpose, supported languages, and any prerequisites.
- [ ] `<Name>Workload.cs`, the workload entry-point class. The file **must** include the assembly-level `CliWorkload<T>` attribute that the SDK uses to discover the entry point during packaging:
  ```csharp
  // Copyright (c) .NET Foundation. All rights reserved.
  // Licensed under the MIT License. See LICENSE in the project root for license information.

  using Azure.Functions.Cli.Projects;
  using Azure.Functions.Cli.Workloads;
  using Microsoft.Extensions.DependencyInjection;

  [assembly: CliWorkload<Azure.Functions.Cli.Workloads.<Name>.<Name>Workload>()]

  namespace Azure.Functions.Cli.Workloads.<Name>;

  /// <summary>
  /// Entry-point for the <Name> workload.
  /// </summary>
  public sealed class <Name>Workload : Workload
  {
      public override string DisplayName => "<Name> Stack";

      public override string Description => "Azure Functions CLI tooling for <Name> projects.";

      public override void Configure(FunctionsCliBuilder builder)
      {
          ArgumentNullException.ThrowIfNull(builder);
          builder.Services.AddSingleton<IProjectInitializer, <Name>ProjectInitializer>();
      }
  }
  ```
  - Must be `public sealed` with a parameterless constructor (the loader activates it by reflection).
  - The `[assembly: CliWorkload<T>()]` attribute is **required** — the `ResolveWorkloadEntry` MSBuild task scans the compiled assembly for this attribute to auto-generate `workload.json`. Do **not** create a manual `workload.json` file.
  - Use expression-bodied `=>` overrides; do not redeclare package id or version on the class.
  - Register additional services in `Configure` as needed: `IQuickstartProvider` for post-init guidance, `IFunctionsProjectFactory` via `builder.AddProjectFactory(...)` for project detection, custom commands via `builder.RegisterCommand<T>()`.
- [ ] At least one provider, typically `<Name>ProjectInitializer.cs` implementing `IProjectInitializer`:
  ```csharp
  // Copyright (c) .NET Foundation. All rights reserved.
  // Licensed under the MIT License. See LICENSE in the project root for license information.

  using System.CommandLine;
  using Azure.Functions.Cli.Commands;
  using Azure.Functions.Cli.Projects;

  namespace Azure.Functions.Cli.Workloads.<Name>;

  /// <summary>
  /// Scaffolds a <Name> Functions project.
  /// </summary>
  internal sealed class <Name>ProjectInitializer : IProjectInitializer
  {
      public string Stack => "<name>";

      public string DisplayName => "<Name>";

      public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

      public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
          new Dictionary<string, IReadOnlyList<string>>()
          {
              { "<Language>", [] }
          };

      public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
      {
          ArgumentNullException.ThrowIfNull(registry);
          return [];
      }

      public Task InitializeAsync(
          InitContext context,
          ParseResult parseResult,
          CancellationToken cancellationToken = default)
      {
          throw new NotImplementedException(
              "<Name> project initialization is not implemented yet.");
      }
  }
  ```
  - Initializer must be `internal sealed` (per repo convention; tests reach it via `InternalsVisibleTo`).
  - For stubs, return `[]` from `GetInitOptions(IInitOptionRegistry)` and only throw from `InitializeAsync`. Throwing from `GetInitOptions` breaks any code path that enumerates options across workloads.
  - For shared options that other stacks also contribute (`--no-bundles`, `--bundles-channel`), use the factories in `Azure.Functions.Cli.Projects.CommonInitOptions` so wording stays consistent. Register every option via `registry.GetOrAdd(...)` and stash the returned canonical instance for reads inside `InitializeAsync`.

### 2. Test project

Mirror the source path under `test/Workloads/`: grouped workloads use
`test/Workloads/<kind>/<Name>.Tests/`, and ungrouped workloads use
`test/Workloads/<Name>.Tests/`.

- [ ] `Workloads.<Name>.Tests.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="$(SrcRoot)Workloads/<kind>/<Name>/Workloads.<Name>.csproj" />
      <ProjectReference Include="$(SrcRoot)Abstractions/Abstractions.csproj" />
      <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    </ItemGroup>

  </Project>
  ```
  - Omit the `<kind>/` segment in the `ProjectReference` for ungrouped workloads.
  - The `Abstractions` and `Microsoft.Extensions.DependencyInjection` references are needed for DI-based contract tests that verify `Configure(FunctionsCliBuilder)` registrations.
  - Common test dependencies (xUnit, NSubstitute, AwesomeAssertions) are pulled in by `test/Directory.Build.props` — do not redeclare them.
- [ ] `<Name>WorkloadTests.cs`, contract tests for the workload (initializer is registered via `Configure`, `DisplayName` / `Description` are set).
- [ ] `<Name>ProjectInitializerTests.cs`, unit tests for the initializer (`Stack`, `SupportedLanguages`, `InitializeAsync` throws `NotImplementedException` for stubs).

### 3. Solution file, `Azure.Functions.Cli.slnx`

- [ ] Add both projects to the solution under the matching `/src/Workloads/<kind>/` and `/test/Workloads/<kind>/` folders. Omit the `<kind>/` segment for ungrouped workloads.
  ```
  dotnet sln add src/Workloads/<kind>/<Name>/Workloads.<Name>.csproj
  dotnet sln add test/Workloads/<kind>/<Name>.Tests/Workloads.<Name>.Tests.csproj
  ```
  Verify `Azure.Functions.Cli.slnx` keeps the projects in solution folders that match the filesystem hierarchy; do not leave grouped workload projects under only `/src/` or `/test/`.
  ```xml
  <Folder Name="/src/Workloads/<kind>/">
    <Project Path="src/Workloads/<kind>/<Name>/Workloads.<Name>.csproj" />
  </Folder>
  <Folder Name="/test/Workloads/<kind>/">
    <Project Path="test/Workloads/<kind>/<Name>.Tests/Workloads.<Name>.Tests.csproj" />
  </Folder>
  ```
  For ungrouped workloads, use `/src/Workloads/` and `/test/Workloads/` as the solution folders.

### 4. CI release pipeline

Build and test are handled by the unified `eng/ci/public-build.yml` which builds the entire solution — no per-workload build pipelines are needed. Each workload only needs a **release pipeline** for publishing to NuGet.

- [ ] `eng/ci/release/official-release.workload.<name>.yml`:
  ```yaml
  parameters:
  - name: public
    displayName: Publish to nuget.org?
    type: boolean
    default: false

  # Release has no triggers outside of pipeline
  trigger: none
  pr: none

  extends:
    template: /eng/ci/templates/pipelines/release-packages.yml@self
    parameters:
      public: ${{ parameters.public }}
      folder: workloads/<name>
      projects: |
        $(Build.SourcesDirectory)/src/Workloads/<kind>/<Name>/Workloads.<Name>.csproj
        $(Build.SourcesDirectory)/test/Workloads/<kind>/<Name>.Tests/Workloads.<Name>.Tests.csproj
  ```
  - The `release-packages.yml` shared template handles: restore → build → test → sign → pack → release to NuGet staging feed (with optional nuget.org publish).
  - The `folder` parameter determines the partner drop path (`azure-functions/workloads/<name>/`).
  - Omit the `<kind>/` segment in project paths for ungrouped workloads.

Use `eng/ci/release/official-release.workload.python.yml` as the reference.

### 5. Documentation

- [ ] `docs/repo-structure.md`, add the new project directories and CI pipeline entries.
- [ ] `docs/building-a-workload.md`, update only if you introduce new patterns. The existing guide already covers the standard shape this skill scaffolds.

### 6. Verify

```bash
dotnet restore
dotnet build
dotnet test
```

A correctly built workload package should:

- Have package id `Azure.Functions.Cli.Workloads.<Name>` and `packageType=FuncCliWorkload`.
- Carry tags `func-workload;kind:workload;alias:<name>` (auto-set by `Workload.Pack.targets`).
- Contain an auto-generated `workload.json` at the package root (generated by the `WriteWorkloadJson` MSBuild task from the `[assembly: CliWorkload<T>()]` attribute).
- Contain the workload assembly (and its non-unified transitive deps) under `tools/any/`.
- Have an empty `<dependencies>` element in its `.nuspec`.
- Not contain Abstractions or any other unified CLI assemblies (excluded by the `ResolveWorkloadCopyLocal` task).
