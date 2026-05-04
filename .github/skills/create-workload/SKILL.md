---
name: create-workload
description: 'Use when adding a new func CLI workload (e.g. Node, Python, Java). Scaffolds the source project, test project, solution entry, CI pipelines, base CLI registration, and docs.'
---

# Create a New Workload

See `docs/building-a-workload.md` for the interface contracts. Below is the full
checklist for scaffolding a new workload project. Use the dotnet workload as the
reference implementation.

## Workload Project Checklist

Replace `<Name>` with the workload name (e.g., `Node`, `Python`, `Java`).
Replace `<name>` with the lowercase form (e.g., `node`, `python`, `java`).

### 1. Source project — `src/Workload/<Name>/`

Create the following files:

- [ ] `Workload.<Name>.csproj`
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <Description>Azure Functions CLI <name> workload — ...</Description>
    </PropertyGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="Azure.Functions.Workload.<Name>.Tests" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="../../Abstractions/Abstractions.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] `Directory.Version.props` — workload version:
  ```xml
  <Project>
    <PropertyGroup>
      <VersionPrefix>1.0.0</VersionPrefix>
      <VersionSuffix>preview.1</VersionSuffix>
    </PropertyGroup>
  </Project>
  ```
- [ ] `release_notes.md` — initial release notes
- [ ] `<Name>Workload.cs` — class implementing `IWorkload`
- [ ] At least one of: `IProjectInitializer`, `ITemplateProvider`, `IPackProvider` implementations

### 2. Test project — `test/Workload/<Name>.Tests/`

- [ ] `Workload.<Name>.Tests.csproj`
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="$(SrcRoot)Workload/<Name>/$Workload.<Name>.csproj" />
    </ItemGroup>

  </Project>
  ```
- [ ] Contract tests for the `IWorkload` implementation
- [ ] Unit tests for each provider (initializer, template, pack)

### 3. Solution file — `Azure.Functions.Cli.slnx`

- [ ] Add both projects to the solution:
  ```
  dotnet sln add src/Workload/<Name>/Workload.<Name>.csproj
  dotnet sln add test/Workload/<Name>.Tests/Workload.<Name>.Tests.csproj
  ```

### 4. CI pipelines

Create two pipeline files in `eng/ci/`:

- [ ] `workload-<name>-public-build.yml` — 1ES Unofficial template
  - Path filters: `src/Abstractions/**`, `src/Workload/<Name>/**`,
    `test/Workload/<Name>.Tests/**`, plus CI template paths
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

### 5. Base CLI updates — `src/Func/`

In `src/Func/Workloads/WorkloadManager.cs`:

- [ ] Add short alias to `_wellKnownAliases` dictionary:
  ```csharp
  ["<name>"] = "Azure.Functions.Cli.Workload.<Name>",
  ```
- [ ] Add entry to `_workloadCatalog` array:
  ```csharp
  new("<name>", "Azure.Functions.Cli.Workload.<Name>", "<Display Name>", "<Languages>"),
  ```

If the workload has unique project files (e.g., `go.mod`, `Cargo.toml`):
- [ ] Add detection in `src/Func/Commands/ProjectDetector.cs`

### 6. Documentation

- [ ] `docs/repo-structure.md` — add new project directories, CI pipeline entries
- [ ] `docs/building-a-workload.md` — update if new patterns are introduced

### 7. Verify

```bash
dotnet restore
dotnet build
dotnet test
```
