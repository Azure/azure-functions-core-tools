# 02-upgrade-projects-and-packages: Retarget all projects to net11.0 and bump central packages

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

## Research findings (TaskExecutor, 2026-11-17, before any source edit)

### Confirmed scope (actual repo)
- `rg '<TargetFramework'` finds 29 project files: 27 slnx projects on `net10.0`, `Workloads.Sdk` on `netstandard2.0`, and `test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj` on `net10.0` (not in slnx). Matches the task description. No `TargetFrameworks` (plural) usage; no TFM in any `Directory.Build.props`/`.targets`/`Engineering.props`, so every TFM is a per-project literal and must be edited in each csproj (in-place replace, singular kept).
- `LangVersion` is defined only in `eng/build/Engineering.props` (`14`); no per-project overrides.
- CPM is on (`Directory.Packages.props` → `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`, imports `eng/build/Packages.props`). No project uses `Version=`/`VersionOverride=` for the packages in scope.
- SDK: `global.json` pins `11.0.100` (task 01); `dotnet --list-sdks` shows 11.0.100 installed.
- No `// STUB:` markers in src/ or test/ (stub discovery: none).

### Package actions (observed current → target; target versions confirmed present on the `upstream-public` feed via `dotnet package search --configfile NuGet.Config`)
| Package | Current | Target | Consumers |
|---|---|---|---|
| Microsoft.Extensions.DependencyInjection | 10.0.0 | 11.0.0 | 6 stack test projects |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.7 | 11.0.0 | Abstractions (also unified in `eng/build/Workloads/Workload.UnifiedAssemblies.targets`) |
| Microsoft.Extensions.FileSystemGlobbing / Http / Options.DataAnnotations | 10.0.0 | 11.0.0 | Func |
| Microsoft.Extensions.Hosting | 10.0.0 | 11.0.0 | Func, Workloads.Host |
| Microsoft.Extensions.Logging | 10.0.0 | 11.0.0 | Workloads.Host.Tests |
| Microsoft.Extensions.Logging.Console | 10.0.0 | 11.0.0 | (pin only; transitive) |
| System.Diagnostics.EventLog | 10.0.0 | 11.0.0 | Workloads.Host |
| Microsoft.TemplateEngine.{Abstractions,Edge,Orchestrator.RunnableProjects} | 10.0.301 | 11.0.100 (only GA 11.0.1xx on feed) | Func |
| System.Text.Json / System.Reflection.MetadataLoadContext | 10.0.9 | **unchanged** | Directly referenced only by Workloads.Sdk (netstandard2.0). Pins left as-is so Workloads.Sdk's effective versions don't move. Under transitive pinning these pins also apply to net11 projects' transitive graphs; if restore reports NU1109 (downgrade) for a net11 project, scope the pin with a TargetFramework condition rather than bumping it for Workloads.Sdk. |
| System.CommandLine | 2.0.6 | unchanged (decision) | |
| All others | — | unchanged (assessment: compatible, no vulnerabilities) | |

### Assessment findings and dispositions
- 28 projects, all Low difficulty; 0 binary-incompatible; 0 incompatible packages.
- Api.0002 source-incompatible (22): Func (4 in 4 files), Func.Tests (11 in 7 files), Workloads.DotNet.Tests (4 in 1), Workloads.Java (1), Workloads.Java.Tests (2). APIs: `Path.Combine(ReadOnlySpan<string>)` 16, `Task.WhenAll(ReadOnlySpan<Task>)` 4, `string.Join(string, ReadOnlySpan<string>)` 1, `string.Split(ReadOnlySpan<char>)` 1. Disposition: these are overload re-binds; they only need code changes if the compiler reports ambiguity/error. The CI=true build is the arbiter.
- Api.0003 behavioral (475): advisory; act only if a build/test failure points to one (see Behavioral API review below).
- Workloads.Sdk: `NETStandard.Library` "mandatory" item is an artefact of netstandard2.0 implicit reference — no action (stays netstandard2.0 by decision).

### Out-of-scope `net10.0` literals left intentionally
- `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs` (DefaultFramework/SupportedFrameworks) + `DotNetProjectInitializerTests.cs` — product behavior for user projects (decision).
- `test/Workloads/Stacks/DotNet.Tests/DotNetSourceProjectTests.cs` net10.0 InlineData / `bin/Debug/net10.0/MyApp.dll` — describes a generated *user* project, same category.
- `test/Func.Tests/Workloads/Storage/WorkloadStoreTests.cs` `lib/net10.0/Foo.dll` — opaque test data.

### Plan of edits
1. Replace `net10.0` → `net11.0` in the 28 csprojs (27 slnx + Terminal.Native.Probe).
2. `eng/build/Engineering.props`: LangVersion 14 → 15.
3. `eng/build/Packages.props`: bump the pins listed above.
4. `dotnet restore Azure.Functions.Cli.slnx` (CI=true), then `dotnet build Azure.Functions.Cli.slnx -c Release` with `CI=true`; fix errors/warnings at source.
5. `dotnet test` the slnx (Release, CI=true) unit tests.

## Behavioral API review
- Underlying change: .NET 11 runtime behavioral notices (Uri, JsonDocument, HttpContent, ProcessStartInfo.Environment, MemoryStream, etc.), 475 instances concentrated in Func/Func.Tests. Docs: https://learn.microsoft.com/dotnet/core/compatibility/11 . Disposition at research time: **Uncertain / advisory** — no evidence yet of applicable impact; the existing unit test suites are the check. Updated in progress-details.md after tests run.
