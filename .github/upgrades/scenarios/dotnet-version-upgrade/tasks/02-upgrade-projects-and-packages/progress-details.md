# 02-upgrade-projects-and-packages — progress details

## Entry 1 — TaskExecutor (initial execution)

### Decomposition
- Loaded: scenario `SKILL.md`, `execution.md`, `breakdown-hints/common.md`, `breakdown-hints/test.md`, `strategies/all-at-once.md`.
- Escalated to TaskBreaker (whole-solution scope; `common.md` multi-project-dependency-ordering vs All-at-Once). Verdict: **atomic** (recorded in `breakdown-context.md`).
- Execution-scope scenario extensions: none apply.

### Changes made
1. **TFM** `net10.0` → `net11.0` (single-line in-place replace, verified 28 csprojs × 1/1 line diff):
   all 27 net10.0 slnx projects + `test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj`. `Workloads.Sdk` stays `netstandard2.0`.
2. **LangVersion** 14 → 15 in `eng/build/Engineering.props`.
3. **`eng/build/Packages.props`**
   - Microsoft.Extensions.{DependencyInjection, DependencyInjection.Abstractions, FileSystemGlobbing, Hosting, Http, Options.DataAnnotations, Logging, Logging.Console} → 11.0.0
   - System.Diagnostics.EventLog → 11.0.0
   - Microsoft.TemplateEngine.{Abstractions, Edge, Orchestrator.RunnableProjects} 10.0.301 → 11.0.100
   - **Deviation (forced):** NuGet.Packaging / NuGet.Protocol / NuGet.Versioning 6.13.2 → 8.0.0, and new central pin NuGet.Credentials 8.0.0 (see Issues #2).
   - Unchanged: System.Text.Json 10.0.9, System.Reflection.MetadataLoadContext 10.0.9, System.CommandLine 2.0.6, all others.
4. **`src/Abstractions/Abstractions.csproj`**: removed `<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />` (see Issues #1). Central pin kept at 11.0.0; `Workload.UnifiedAssemblies.targets` exclusion entry left as-is (still correct: assembly must not ship in workload packages).
5. **C# 15 IDE0028** (collection expression arguments): `dotnet format style Azure.Functions.Cli.slnx --diagnostics IDE0028 --severity info --no-restore` (analyzer's own code fix). 58 .cs files, 89/89 one-line changes, all of the form `new(StringComparer.X)` / `new T(StringComparer.X) { a, b }` → `[with(StringComparer.X)]` / `[with(StringComparer.X), a, b]`. Semantically identical (same constructor + Add). Includes `src/Workloads/Sdk/Tasks/WriteWorkloadJson.cs` (netstandard2.0, compiles under C# 15) and `DotNetProjectInitializer.cs` (only a comparer-initializer line; the out-of-scope net10 literals untouched).
6. **NuGet 8 nullability annotations** (`ISourceRepository.GetResourceAsync<T>` now `T?`, `GetPrimarySignatureAsync` now `PrimarySignature?`):
   - `src/Func/Workloads/Catalog/NuGetProtocolSourceClient.cs`: added private `GetRequiredResourceAsync<T>` that throws `InvalidOperationException("Source '…' does not provide the NuGet resource '…'")` when null; used for `FindPackageByIdResource` (×2) and `HttpSourceResource`. Previously a null resource would have been an NRE; behavior for present resources unchanged.
   - `test/Func.Tests/Workloads/Install/WorkloadPackageTrustTests.cs`: assert signature not null before reading `.Type`.

### Issues hit (in order)
1. Restore `NU1510` (warning-as-error) on Abstractions: `Microsoft.Extensions.DependencyInjection.Abstractions` is now in `Microsoft.NETCore.App` 11 (verified: `packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/Microsoft.Extensions.DependencyInjection.Abstractions.dll` and `data/PackageOverrides.txt` line `…DependencyInjection.Abstractions|11.0.0`). Fix: drop the redundant PackageReference.
2. Restore `NU1109` (downgrade NuGet.Packaging/Protocol/Versioning 8.0.0-rc.53118 → central 6.13.2) and `NU1603` (NuGet.Common/Configuration/Credentials/Frameworks 8.0.0-rc.53118 not found): `Microsoft.TemplateEngine.Edge 11.0.100` requires NuGet.* `>= 8.0.0-rc.53118`. Feed (`dotnet package search --exact-match --prerelease --configfile NuGet.Config`) has 8.0.0 GA for all, no rc. Keeping 6.13.2 is impossible with TemplateEngine 11.0.1xx (which task.md requires). Fix: bump the three NuGet.* pins to 8.0.0 (clears NU1109 and the Common/Configuration/Frameworks NU1603 via nearest-wins); NuGet.Credentials only reachable through TemplateEngine.Edge still raised NU1603 → added transitive central pin `NuGet.Credentials 8.0.0`. Restore then clean. **This contradicts task.md's "leave NuGet.* pins" line; flagged for orchestrator/user.**
3. Build: IDE0028 ×2 in Abstractions, then ~52 across Func/Workloads.Sdk/tests (C# 15). Fixed via dotnet format (above).
4. Build: CS8600/CS8602 in NuGetProtocolSourceClient.cs and WorkloadPackageTrustTests.cs (NuGet 8 annotations). Fixed (above). First attempt placed the helper between another method's XML doc and signature; moved before the doc block.
5. Overload-rebind (Api.0002, 22 notices): no compiler errors/ambiguities surfaced; no change needed.
6. Probe project needs `-p:SpectreAssemblyPath=…` (as ConPtySmokeTests passes it); without it it fails with CS0246 — pre-existing build contract, not an upgrade regression.

### Validation (all from repo root, PowerShell, SDK 11.0.100 via global.json, `$env:CI='true'` in same command)
- `dotnet restore Azure.Functions.Cli.slnx` → exit 0, no warnings.
- `dotnet build Azure.Functions.Cli.slnx -c Release --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**, exit 0 (5m33s). TreatWarningsAsErrors=$(CI)=true.
- `dotnet build test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj -c Release -p:SpectreAssemblyPath=out/bin/Func.Tests/release/Spectre.Console.dll` → 0 warnings, 0 errors.
- `dotnet test Azure.Functions.Cli.slnx -c Release --no-build` → exit 0. All net11.0:
  Func.Tests 3127 passed / 3 skipped / 0 failed; DotNet 79; Node 66; Python 56; Host 50; Java 39; Go 38; PowerShell 36; Sdk 35 — all passed.
  (Tests ran after the last source edit; the subsequent `--no-incremental` rebuild had no source changes in between.)
- Workloads.Sdk effective versions (`out/obj/Workloads.Sdk/project.assets.json`): System.Text.Json 10.0.9, System.Reflection.MetadataLoadContext 10.0.9, Microsoft.Build.Utilities.Core 17.11.48 — unchanged.
- No suppressions added: `git diff` has no added `NoWarn`/`#pragma`/`SuppressMessage`.

### Behavioral API review
- .NET 11 behavioral notices (475, Uri/JsonDocument/HttpContent/ProcessStartInfo.Environment/MemoryStream etc.): **No applicable impact observed** — full unit suites pass on net11.0; no code change made. Remains advisory.
- NuGet 6.13 → 8.0 (forced dependency bump): workload catalog/install/signature-trust tests (Func.Tests Workloads/*) pass; only nullability annotation changes required.
- `GetRequiredResourceAsync` changes a would-be NRE into a descriptive `InvalidOperationException` (same exception propagation path for callers that catch `Exception`).

### Review
- IntegrityReviewer verdict: **PASS** (initial pass; NuGet 8.0.0 pins judged dependency-forced, DI.Abstractions removal judged framework-provided). No review.md created (clean pass, no record required).
