# Supporting Files and Upgrade Notes (net10.0 → net11.0)

[← Back to the assessment index](assessment.md)

This page was added by the assessment worker. It covers the non-project files the user asked to upgrade, plus
some notes that the engine report does not include. The engine-generated content is in [assessment.md](assessment.md).

## Contents

- [Toolchain](#toolchain)
- [Project TFM declarations](#project-tfm-declarations)
- [Central package versions](#central-package-versions)
- [Build props/targets](#build-propstargets)
- [Pipelines and workflows](#pipelines-and-workflows)
- [Containers](#containers)
- [Docs, AGENTS.md, skills](#docs-agentsmd-skills)
- [Product-behavior references (decision needed)](#product-behavior-references-decision-needed)
- [Open items / ambiguities](#open-items--ambiguities)

## Toolchain

| Item | Current | Target / Status |
| :--- | :--- | :--- |
| `global.json` sdk.version | `10.0.403`, `rollForward: latestMinor`, `allowPrerelease: true` | Change to `11.0.100` (installed locally). Keep `latestMinor` once pinned to 11.x. |
| Installed SDKs | 6.0–10.0.404, **11.0.100** | `validate_dotnet_sdk_installation(net11.0)` → Compatible SDK found |
| `msbuild-sdks` | NoTargets 3.7.56, Traversal 4.1.0 | No change required |
| `LangVersion` (`eng/build/Engineering.props:22`) | `14` | Optional: bump to `15` (C# default for net11). If left at 14, net11 still builds, but the default language version is pinned. |

> Note: `validate_dotnet_sdk_in_globaljson` rewrites `global.json` (it sets `rollForward: latestMajor`). The assessment
> worker reverted that edit. Execution must set the SDK version explicitly instead.

## Project TFM declarations

Every project sets `<TargetFramework>` in its own csproj. No shared TFM property exists in Directory.Build.props.

- **27 projects in the slnx** use `net10.0`. Change them to `net11.0`. See the [project inventory](assessment/projects/index.md).
- `src/Workloads/Sdk/Workloads.Sdk.csproj` targets **`netstandard2.0`**. This is an MSBuild task library, so **keep it as is**.
- **Not in the slnx and not analyzed:** `test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj` (`net10.0`, line 9).
  It is a terminal probe exe that `Func.Tests` builds. Change it to `net11.0` together with `Func.Tests`.

## Central package versions

All versions are centralized in `eng/build/Packages.props`, which `Directory.Packages.props` imports.

| Package | Current | Suggested | Note |
| :--- | :---: | :---: | :--- |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | 11.0.0 | |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.7 | 11.0.0 | Abstractions is a public package, so this raises the floor for workload authors |
| Microsoft.Extensions.FileSystemGlobbing / Hosting / Http / Options.DataAnnotations / Logging | 10.0.0 | 11.0.0 | |
| Microsoft.Extensions.Logging.Console | 10.0.0 | 11.0.0 | Not in the engine report (no project references it) but it is pinned centrally. Keep it in step with the others. |
| System.Diagnostics.EventLog | 10.0.0 | 11.0.0 | Workloads.Host |
| System.Reflection.MetadataLoadContext / System.Text.Json | 10.0.9 | 11.0.0 | Used by **Workloads.Sdk (netstandard2.0)**, which MSBuild loads. Check that the 11.0.0 packages still support netstandard2.0 and the MSBuild host's binding before bumping. Otherwise keep 10.0.x. |
| Microsoft.TemplateEngine.* (3) | 10.0.301 | 11.0.100 | Matches the SDK band. The docs cite 10.0.301 (see below). |
| System.CommandLine | 2.0.6 | 3.0.0 | **Major bump**: expect API breaks in Abstractions/Func. Abstractions is public API. Decide: bump or stay on 2.0.x (2.0.x is net11 compatible). |
| NETStandard.Library | 2.0.3 | — | Implicit for netstandard2.0 (Workloads.Sdk). Leave as is. |
| All others (OpenTelemetry, NuGet.*, Spectre, WebJobs.Script.WebHost 4.1055.100, xunit 2.9.3, Test.Sdk 17.12.0, NSubstitute, AwesomeAssertions, MSBuild 17.11.48, workers) | — | — | ✅ Compatible. No change needed. |

No security vulnerabilities were reported.

## Build props/targets

- `Directory.Build.props/targets`, `src/`, `test/`, `test/Fixtures/Directory.Build.props`, and `eng/build/*.props|targets` have **no hardcoded `net10`/SDK version**.
- `eng/build/Engineering.props`: `LangVersion 14`. See [Toolchain](#toolchain).
- `_CliRuntimeIdentifiers` (win/linux/osx, x64/arm64): no change needed.

## Pipelines and workflows

| File | Reference | Action |
| :--- | :--- | :--- |
| `eng/ci/templates/steps/setup-dependencies.yml:7` | `UseDotNet@2 useGlobalJson: true` | Picks up the SDK from global.json automatically |
| `eng/ci/templates/steps/setup-dependencies.yml:14` | Comment says `# ESRP doesn't yet support net10 SDK`; installs `9.x` for the ESRP tool | Keep the 9.x install, because the ESRP tool needs that runtime. Update the comment to say "net11" (or "newer"). |
| `eng/ci/templates/jobs/stage-cli-artifacts.yml:57` | Same ESRP comment and `9.x` install | Same as above |
| `eng/ci/templates/jobs/build.yml`, `public-build.yml`, `official-build.yml`, `release/*.yml` | `DotNetCoreCLI@2` with no version pin | No change needed |
| `.github/workflows/terminal-macos.yml:61` | `actions/setup-dotnet@v4` with `global-json-file: global.json` | Picks up the SDK automatically |
| `.github/workflows/installer-tests.yml`, `dependency-review.yml`, `doc-sync.yml` | No .NET version reference | No change needed |
| `.github/dependabot.yml` | `dotnet-sdk` ecosystem | No change needed |

## Containers

| File | Reference | Action |
| :--- | :--- | :--- |
| `.devcontainer/devcontainer.json:3` | `mcr.microsoft.com/dotnet/sdk:10.0` | Change to `sdk:11.0` |
| `.github/skills/build-workloads/assets/docker-compose.yml` | `loicsharma/baget:latest` (NuGet feed) | No change needed |
| Dockerfiles | None in the repo | — |

## Docs, AGENTS.md, skills

These files describe the CLI's own target framework or SDK. Update all of them to .NET 11 / `net11.0`:

- `AGENTS.md:6`: "**.NET 10**"
- `README.md:107`: "**.NET 10** — target framework". `README.md:142`: "Prerequisites: .NET 10 SDK".
- `docs/repo-structure.md:7`: "targeting **.NET 10**". `:86`: "Pins .NET SDK 10.0.100" (already stale; it is 10.0.403).
- `docs/building-a-workload.md:58,85`: tells workload authors to target `net10.0`.
- `.github/skills/create-workload/SKILL.md:34,169`: the scaffold uses `<TargetFramework>net10.0</TargetFramework>`.
- `proposed/workload-package-layout.md:190,941-942`: illustrative `lib/net10.0`. This is a design proposal; update it or leave it (low priority).
- `docs/func-new-execution/design.md:9`, `docs/template-package-install/design.md:15`: cite "Microsoft.TemplateEngine 10.0.301". Update these if the package is bumped.
- `docs/cli-architecture.md:137-138`: a sample `--target-framework` option with default `net10.0`. This is an illustrative snippet; update it so it stays consistent.

## Product-behavior references (decision needed)

These references describe the **user-project TFM that `func init` generates**, not the CLI's own TFM. Changing them changes product behavior:

- `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs:21-23,50,82`: `DefaultFramework = "net10.0"`, `SupportedFrameworks = ["net10.0","net11.0"]`, F# guidance "target .NET 10".
- `test/Workloads/Stacks/DotNet.Tests/DotNetProjectInitializerTests.cs`, `DotNetSourceProjectTests.cs`: assert those values.
- `src/Workloads/Stacks/DotNet/README.md:5,13,22`, `release_notes.md:5`, `docs/func-init-execution/stack-inventory.md:14`.
- `test/Func.Tests/Workloads/Storage/WorkloadStoreTests.cs:123`: test data `lib/net10.0/Foo.dll`. This is opaque test data; leaving it is fine.

**Recommendation:** keep these out of scope for the TFM upgrade. They track Azure Functions isolated-worker support policy and are not the build TFM.

## Open items / ambiguities

1. Should System.CommandLine go to 3.0.0 (major, public Abstractions surface) or stay on 2.0.x?
2. Should System.Text.Json / MetadataLoadContext in netstandard2.0 Workloads.Sdk (an MSBuild task) be bumped?
3. Should LangVersion go from 14 to 15?
4. Should the `func init` default TFM change? (Recommended: no.)
5. Terminal.Native.Probe.csproj is outside the slnx. Include it in the TFM change.
6. Source-incompatible APIs (22): `Path.Combine`, `Task.WhenAll`, `string.Join`, and `string.Split` resolving to `ReadOnlySpan` params overloads. These only need a recompile; check for overload-ambiguity errors when building with `CI=true`.
