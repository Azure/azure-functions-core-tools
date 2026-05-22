---
name: create-workload
description: 'Use when adding a new func CLI workload (e.g. Node, Python, Java). Scaffolds the source project, test project, solution entry, CI pipelines, and docs.'
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
      <Title><Name></Title>
      <Description>Azure Functions CLI tooling for <Name> projects.</Description>
      <PackageType>FuncCliWorkload</PackageType>
      <PackageTags>kind:workload alias:<name> func-workload</PackageTags>
      <IncludeBuildOutput>false</IncludeBuildOutput>
      <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
      <NoWarn>$(NoWarn);NU5128;NU5100</NoWarn>
    </PropertyGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="Azure.Functions.Cli.Workloads.<Name>.Tests" />
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
  - `PackageType=FuncCliWorkload` is required for catalog discovery.
  - `Title` is the feed-UI display name; `Description` is the one-line summary. The `Workload` class's `DisplayName` / `Description` overrides serve the same purpose for the running CLI.
  - `PackageTags` should include exactly one `kind:<workload|content|meta>` tag matching `workload.json`'s `kind` field, plus one or more `alias:<name>` tags so users can install by short name. `func-workload` is recommended for generic feed UI discoverability.
  - `IncludeBuildOutput=false` plus the explicit `tools/any/` pack item is what makes the loader find the workload assembly.
  - `SuppressDependenciesWhenPacking=true` plus `PrivateAssets=all` / `ExcludeAssets=runtime` on Abstractions keep the workload self-contained: the CLI provides Abstractions (and the other host-shared contract assemblies) at runtime. Apply the same `PrivateAssets=all` rule to **every** future `<PackageReference>`.
  - The csproj above only packs the workload's own assembly. As soon as the workload pulls in transitive managed dependencies, you'll need to pack the `dotnet publish` output (workload `.dll`, `.deps.json`, transitive deps, optional `runtimes/`). The upcoming `Workload.Sdk` package will provide that pack target. See `docs/proposed/workload-package-layout.md` §5 and §9.
  - Csproj/assembly name must be `Azure.Functions.Cli.Workloads.<Name>` (set via the project filename and matched in the package id).
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
- [ ] `workload.json`, the entry-point manifest packed at the package root. `assemblyPath` is relative to `tools/any/` (bare filename; no leading `/`, no `..`); `type` is the FQN of the `Workload` subclass:
  ```json
  {
    "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
    "kind": "workload",
    "entryPoint": {
      "assemblyPath": "Azure.Functions.Cli.Workloads.<Name>.dll",
      "type": "Azure.Functions.Cli.Workloads.<Name>.<Name>Workload"
    }
  }
  ```
- [ ] `<Name>Workload.cs`, subclass of `Workloads.Workload`:
  ```csharp
  using Azure.Functions.Cli.Workloads;
  using Microsoft.Extensions.DependencyInjection;

  namespace Azure.Functions.Cli.Workloads.<Name>;

  /// <summary>
  /// Entry-point for the <Name> workload.
  /// </summary>
  public sealed class <Name>Workload : Workloads.Workload
  {
      public override string DisplayName => "<Name>";

      public override string Description => "Azure Functions CLI tooling for <Name> projects.";

      public override void Configure(FunctionsCliBuilder builder)
      {
          ArgumentNullException.ThrowIfNull(builder);
          builder.Services.AddSingleton<IProjectInitializer, <Name>ProjectInitializer>();
      }
  }
  ```
  - Must be `public sealed` and have a parameterless constructor (the loader activates it by reflection).
  - Use expression-bodied `=>` overrides; do not redeclare package id or version on the class — the csproj and `Directory.Version.props` are the single source of truth.
- [ ] At least one provider, typically `<Name>ProjectInitializer.cs` implementing `IProjectInitializer`:
  ```csharp
  internal sealed class <Name>ProjectInitializer : IProjectInitializer
  {
      public string Stack => "<name>";

      public IReadOnlyList<string> SupportedLanguages { get; } = ["<Language>"];

      public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry) => [];

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

- [ ] `Workloads.<Name>.Tests.csproj`, assembly name `Azure.Functions.Cli.Workloads.<Name>.Tests`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <AssemblyName>Azure.Functions.Cli.Workloads.<Name>.Tests</AssemblyName>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="$(SrcRoot)Workloads/<kind>/<Name>/Workloads.<Name>.csproj" />
    </ItemGroup>
  </Project>
  ```
  - Omit the `<kind>/` segment in the `ProjectReference` for ungrouped workloads.
  - Don't add a redundant `ProjectReference` to `Abstractions`; it flows through the workload reference.
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

### 4. CI pipelines

All workloads share a single job template, `eng/ci/templates/jobs/build-workload.yml`, parameterised by `WorkloadProjectName`. Each workload only needs two thin pipeline files; do not introduce per-workload job/step templates.

- [ ] `eng/ci/workloads/<name>/public-build.yml`, 1ES Unofficial template:
  - Extends the 1ES unofficial template and references the shared job template:
    ```yaml
    extends:
      template: ...1ESPipelineTemplates/1ES.Unofficial.PipelineTemplate.yml@1esPipelines
      parameters:
        ...
        stages:
          - stage: BuildAndTest
            jobs:
              - template: /eng/ci/templates/jobs/build-workload.yml@self
                parameters:
                  WorkloadProjectName: <Name>
    ```
  - Path filters scope triggers to `src/Abstractions/**`, `src/Workloads/<kind>/<Name>/**`, `test/Workloads/<kind>/<Name>.Tests/**`, `eng/ci/templates/jobs/build-workload.yml`, and the pipeline file itself. Omit the `<kind>/` segment for ungrouped workloads.
- [ ] `eng/ci/workloads/<name>/official-build.yml`, 1ES Official template:
  - Same shape, but extends the Official template, sets `pr: none`, and adds `release/*` to its branch triggers (and to path filters as needed). The shared template handles build + test + pack.

Use `eng/ci/workloads/python/{public,official}-build.yml` as the reference.

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
- Carry tags `kind:workload alias:<name>` (plus optional extra `alias:` entries and `func-workload`).
- Contain `workload.json` at the package root.
- Contain the workload assembly at `tools/any/Azure.Functions.Cli.Workloads.<Name>.dll`.
- Have an empty `<dependencies>` element in its `.nuspec`.
- Not contain Abstractions or any of the other host-shared contract assemblies.
