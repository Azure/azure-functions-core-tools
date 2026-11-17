# .NET Version Upgrade Plan

## Overview

**Target**: Azure Functions Core Tools (vnext) — upgrade all projects from .NET 10 (`net10.0`) to .NET 11 (`net11.0`), along with the SDK toolchain, pipelines, container, and documentation.
**Scope**: Large solution with 28 SDK-style projects (~104k LOC). All projects are Low difficulty and package versions are pinned centrally. There are no incompatible packages and no binary-incompatible APIs. The assessment found 22 source-incompatible and 475 behavioral-change notices.

### Selected Strategy
**All-At-Once**: all projects are upgraded together in a single operation.
**Rationale**: 28 projects, all already on modern .NET (27 on `net10.0`, plus `Workloads.Sdk` on `netstandard2.0`, which stays as is). Every project is rated Low difficulty, and versions are pinned centrally in `eng/build/Packages.props`. That makes this a mechanical TFM and package bump that is best applied atomically.

**Projects (single group):**
- Libraries and apps: Abstractions, Func, Workloads.Host, Workloads.ExtensionBundles, Workloads.Sdk (stays `netstandard2.0`), Workloads.{DotNet,Go,Java,Node,PowerShell,Python}, Workloads.Workers.{Go,Java,Node,PowerShell,Python}
- Tests and fixtures: Func.Tests (plus `Terminal.Native.Probe.csproj`, which is not in the slnx), Workloads.Host.Tests, Workloads.Sdk.Tests, Workloads.{DotNet,Go,Java,Node,PowerShell,Python}.Tests, Workloads.Tests.Fixtures.{Default,Sdk,WithCommand}

## Upgrade Options

| Option | Selected | Why |
|--------|----------|-----|
| Upgrade Strategy | All-at-Once | All 28 projects are SDK-style, Low difficulty, and centrally pinned, with no incompatible packages, so an atomic bump avoids incremental overhead. Chosen on the user's behalf in place of the size-based Top-Down default. |
| Test Coverage | Skip | Opt-in by default. The existing test suites in 11 test projects cover the upgraded code, and the user's validation preference is restore, build, and relevant tests. |

## Tasks

### 01-sdk-toolchain: Point the repository at the .NET 11 SDK

Update `global.json` so the build uses the installed .NET 11 SDK. Set `sdk.version` to `11.0.100` and keep the existing `rollForward: latestMinor` and `allowPrerelease` settings. Do not run `validate_dotnet_sdk_in_globaljson`, because it rewrites `rollForward` to `latestMajor`. Leave `msbuild-sdks` (NoTargets 3.7.56, Traversal 4.1.0) unchanged. The CI templates (`UseDotNet@2 useGlobalJson: true`) and `.github/workflows/terminal-macos.yml` (`global-json-file`) pick up the SDK from this file.

This task must land before any `net11.0` build is attempted. The pinned 10.x SDK cannot target `net11.0`, and `latestMinor` will not roll forward to 11.

**Done when**: `global.json` pins `11.0.100` with its rollForward policy unchanged, and `dotnet --version` in the repo root resolves to an 11.0.x SDK.

---

### 02-upgrade-projects-and-packages: Retarget all projects to net11.0 and bump central packages

Change `<TargetFramework>` from `net10.0` to `net11.0` in all 27 `net10.0` projects in `Azure.Functions.Cli.slnx`. Also change `test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj`, which is not in the slnx but is built by Func.Tests. `src/Workloads/Sdk/Workloads.Sdk.csproj` stays on `netstandard2.0` because it is an MSBuild task library. Bump `LangVersion` from 14 to 15 in `eng/build/Engineering.props`.

In `eng/build/Packages.props`, move these packages to their .NET 11 versions:
- Microsoft.Extensions.* (DependencyInjection, DependencyInjection.Abstractions, FileSystemGlobbing, Hosting, Http, Options.DataAnnotations, Logging, Logging.Console): to 11.0.0
- System.Diagnostics.EventLog: to 11.0.0
- Microsoft.TemplateEngine.* (3 packages): to the 11.0.1xx band

Keep System.CommandLine on 2.0.x. Do not bump System.Text.Json or System.Reflection.MetadataLoadContext for `Workloads.Sdk`. If those pins are shared with net11 projects, keep `Workloads.Sdk`'s effective versions unchanged, for example by using a conditional or project-scoped override. Leave all other pins as they are (OpenTelemetry, NuGet.*, Spectre, WebJobs.Script.WebHost, xunit, MSBuild, workers). The assessment rates them compatible and reports no vulnerabilities.

Then restore, build, and fix all compilation errors and warnings in one bounded pass. The build runs with `CI=true`, so warnings are errors. Fix them at the source and never suppress them. Expected friction:
- 22 source-incompatible notices, where `Path.Combine`, `Task.WhenAll`, `string.Join`, and `string.Split` now bind to `ReadOnlySpan` params overloads. Watch for overload ambiguity.
- New analyzer or C# 15 warnings.

The 475 behavioral notices are advisory review items, not required changes. They are mostly `System.Uri`, `JsonDocument`, `HttpContent`, and `ProcessStartInfo.Environment`, concentrated in Func and Func.Tests. Act on one only if a build or test failure points to it. See `assessment.md` and `supporting-files-inventory.md` for detail, and use `query_dotnet_assessment` for per-instance locations.

Out of scope: product behavior for user projects that `func init` generates (`DotNetProjectInitializer` DefaultFramework/SupportedFrameworks and its tests). Opaque test data such as `WorkloadStoreTests` `lib/net10.0/Foo.dll` also stays unchanged.

**Done when**: every in-scope project (including Terminal.Native.Probe) targets `net11.0`, and Workloads.Sdk still targets `netstandard2.0` with unchanged effective System.Text.Json/MetadataLoadContext versions. Central pins are updated as described and LangVersion is 15. `dotnet restore` succeeds. A Release build of `Azure.Functions.Cli.slnx` with `CI=true` produces 0 errors and 0 warnings, with no new suppressions. The existing unit test projects pass.

---

### 03-pipelines-and-supporting-files: Update pipelines, container, docs, and agent guidance for .NET 11

Bring the non-project files in line with .NET 11, following `supporting-files-inventory.md`:
- **Pipeline comments:** update the outdated "ESRP doesn't yet support net10 SDK" comments in `eng/ci/templates/steps/setup-dependencies.yml` and `eng/ci/templates/jobs/stage-cli-artifacts.yml`. Keep the `9.x` install that ESRP signing needs.
- **Toolchain wiring:** confirm that `global.json` (from task 01) is the single SDK source for the `.github/workflows` and CI templates. Change any remaining hardcoded 10.x SDK or `net10.0` references.
- **Container:** in `.devcontainer/devcontainer.json`, change the `mcr.microsoft.com/dotnet/sdk:10.0` image to `sdk:11.0`.

Then update documentation and agent guidance that describe the CLI's own target framework or SDK:
- `AGENTS.md`
- `README.md` (target framework and prerequisites)
- `docs/repo-structure.md` (target, and the stale SDK pin text)
- `docs/building-a-workload.md` (workload authors' `net10.0`)
- `.github/skills/create-workload/SKILL.md` (scaffold TFM)
- `docs/cli-architecture.md` (sample snippet)
- `proposed/workload-package-layout.md` (illustrative `lib/net10.0`)
- `docs/func-new-execution/design.md` and `docs/template-package-install/design.md` (TemplateEngine version, if it was bumped in task 02)

Also check the other `.github/skills`, `.github/instructions`, and scripts for .NET 10 references that describe the repo's own build.

Leave unchanged any references that describe the user-project framework `func init` generates. That includes `src/Workloads/Stacks/DotNet/README.md`, `release_notes.md`, and `docs/func-init-execution/stack-inventory.md`.

**Done when**: the in-scope references to `net10.0` and the .NET 10 SDK in pipelines, the devcontainer, docs, AGENTS.md, and skills now say .NET 11 / `net11.0`, and the ESRP `9.x` install is still there. A repo-wide search for `net10`, `.NET 10`, and `10.0.` turns up only intentional leftovers, which are product-behavior references, opaque test data, and unrelated version numbers.

---

### 04-final-validation: Full clean Release validation of the .NET 11 upgrade

Run the end-to-end validation the user requested on the final tree, after the code, pipeline, and doc changes:
1. Start from a clean state (`dotnet clean` and remove stale `bin/obj` or `out/` outputs as needed).
2. Run `dotnet restore` on `Azure.Functions.Cli.slnx`.
3. Run a Release build with `CI=true`.
4. Run the relevant test projects: Func.Tests, Workloads.Host.Tests, Workloads.Sdk.Tests, and the Workloads stack tests.

Confirm that `Terminal.Native.Probe` builds as part of the Func.Tests run. Any failure found here is fixed at the source and does not get suppressed.

Also confirm there are no `NU1605`/`NU1608` downgrade or conflict warnings and no new vulnerability warnings. `git status` should show only intended changes, with no stray artifacts from the upgrade tooling.

**Done when**: a clean `dotnet restore` succeeds. The Release build with `CI=true` finishes with 0 errors and 0 warnings. All relevant test projects pass, or any failures are shown to be pre-existing or environment-dependent and documented. The working tree contains only intended upgrade changes, all committed.
