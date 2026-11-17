# 04-final-validation — progress details

## Entry 1 — TaskExecutor (initial execution)

### Decomposition
- Loaded: scenario `SKILL.md`, `execution.md`, `breakdown-hints/common.md`, `breakdown-hints/test.md`. Execution-scope scenario extensions: none apply.
- Verdict: **atomic** (validation-only; no hint conditions match — no TFM/package-migration work, no project moves). No `// STUB:` markers in the repo.

### Environment
- Repo root `Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot`, HEAD `8675b35c`, Windows, PowerShell; SDK 11.0.100 (via `global.json`); every command run with `$env:CI='true'` in the same process (TreatWarningsAsErrors = `$(CI)`).

### Steps and results (in order)
1. **Clean**: `dotnet clean Azure.Functions.Cli.slnx -c Release -v q` → exit 0; then `Remove-Item -Recurse -Force out` (all bin/obj live under git-ignored `out/`; verified no `bin`/`obj` under `src`/`test`).
2. **Restore**: `dotnet restore Azure.Functions.Cli.slnx` → exit 0, 0 warning/NU lines (no NU1605/NU1608/NU190x).
3. **Vulnerability check**: `dotnet list Azure.Functions.Cli.slnx package --vulnerable --include-transitive` → **found** in Workloads.Host and Workloads.Host.Tests:
   - `System.Security.Cryptography.Pkcs 8.0.1` — High GHSA-846f-f764-76mx, Moderate GHSA-2fmv-rv2c-59x7 (patched 8.0.2 / 9.0.21 / 10.0.13 / 11.0.0-rc.2).
   - `Microsoft.AspNetCore.WebUtilities 2.3.0` — Moderate GHSA-6527-9fp8-r4x6 (published 2026-11-10, patched 2.3.14).
   - Note: restore did not emit NU190x for these (`project.assets.json` `logs` empty although `NuGetAuditMode=all`, `auditLevel=moderate`); `dotnet list --vulnerable` is the check that surfaced them.
4. **Attribution** (new vs pre-existing): exported base commit `6f818499` via `git archive` to `%TEMP%\func-base-6f818499` (outside the repo, deleted afterwards), restored Host/Func/Func.Tests with its SDK 10.0.404 and ran the same `--vulnerable` listing:
   - Base Host: NuGet.Packaging 6.13.2 → Pkcs **6.0.4** (not flagged); WebUtilities 2.3.0 **flagged at base too**.
   - Post-upgrade dependency chain (from `out/obj/Workloads.Host/project.assets.json`): WebHost → NuGet.Protocol → NuGet.Packaging (transitively pinned to central 8.0.0 in task 02) → Pkcs ≥ 8.0.1. → **Pkcs vuln is introduced by the upgrade**; WebUtilities is pre-existing (unchanged `Microsoft.Azure.WebJobs.Script.WebHost 4.1055.100`).
5. **Fix (source, no suppression)**: `eng/build/Packages.props` — added central pin `System.Security.Cryptography.Pkcs` **11.0.0** (patched; on feed per `dotnet package search … --configfile NuGet.Config`; `CentralPackageTransitivePinningEnabled=true` applies it transitively). Func/Func.Tests already resolved 11.0.0, so only Host/Host.Tests change (8.0.1 → 11.0.0). Workloads.Sdk graph unchanged (System.Text.Json 10.0.9, System.Reflection.MetadataLoadContext 10.0.9, no Pkcs).
6. **Re-restore**: `dotnet restore Azure.Functions.Cli.slnx` → exit 0, 0 warnings. Re-ran `--vulnerable` listing → only the pre-existing WebUtilities 2.3.0 remains (Host, Host.Tests).
7. **Release build**: `dotnet build Azure.Functions.Cli.slnx -c Release --no-restore` (fresh outputs, `out/` deleted in step 1) → **Build succeeded, 0 Warning(s), 0 Error(s)**, exit 0, 7m15s. Log: `%TEMP%\func04-build.log`.
8. **Tests**: `dotnet test Azure.Functions.Cli.slnx -c Release --no-build` → exit 0, all net11.0:
   - Func.Tests 3127 passed / 3 skipped / 0 failed (3130)
   - Workloads.Host.Tests 50/50; Workloads.Sdk.Tests 35/35
   - Workloads.DotNet.Tests 79/79; Node 66/66; Python 56/56; Java 39/39; Go 38/38; PowerShell 36/36
   - Identical counts to the task-02 baseline.
9. **Terminal.Native.Probe**: `FUNC_TERMINAL_NATIVE_TESTS=1 dotnet test test/Func.Tests/Func.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ConPtySmokeTests"` → 2/2 passed (the test itself runs `dotnet build …Terminal.Native.Probe.csproj -c Release -p:SpectreAssemblyPath=…` and asserts exit 0; output `out/bin/Terminal.Native.Probe/release/Azure.Functions.Cli.Tests.TerminalProbe.dll`, net11.0).
   - Explicit build: first attempt with a **relative** `-p:SpectreAssemblyPath=out/bin/...` failed (MSB3245 + CS0246 — HintPath resolves relative to the probe project dir; my invocation error, not a code issue). Re-run with absolute path `dotnet build test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj -c Release --no-incremental -p:SpectreAssemblyPath=<abs>\out\bin\Func.Tests\release\Spectre.Console.dll` → 0 warnings, 0 errors.
10. **Working tree**: `git status --short --untracked-files=all` → only `eng/build/Packages.props` (fix) plus workflow artifacts (`scenario.json`, `tasks.md`, this task's `task.md`/`progress-details.md`). No stray tool artifacts. Not committed (orchestrator commits).

### Files modified
- `eng/build/Packages.props` (Pkcs 11.0.0 transitive security pin)
- `.github/upgrades/scenarios/dotnet-version-upgrade/tasks/04-final-validation/task.md`, `progress-details.md`

### Open item for orchestrator/user (pre-existing, not upgrade-introduced)
- `Microsoft.AspNetCore.WebUtilities 2.3.0` (Moderate, GHSA-6527-9fp8-r4x6) in Workloads.Host via WebJobs.Script.WebHost 4.1055.100 — present before the upgrade; patched 2.3.14 is on the feed. Left unchanged (outside .NET 11 upgrade scope; would alter the shipped host's ASP.NET 2.x dependency). Restore does not warn on it today.

### Suppressions
- None added (`git diff` has no `NoWarn`/`#pragma`/`SuppressMessage`/`NuGetAuditSuppress`).

### Review
- IntegrityReviewer verdict: **PASS**, no findings (verified Pkcs 11.0.0 on feed, transitive pinning enabled, assets consistent, Workloads.Sdk unchanged, no suppressions). No review.md created.
