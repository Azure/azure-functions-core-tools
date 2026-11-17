# 04-final-validation: Full clean Release validation of the .NET 11 upgrade

Run the end-to-end validation the user requested on the final tree, after the code, pipeline, and doc changes:
1. Start from a clean state (`dotnet clean` and remove stale `bin/obj` or `out/` outputs as needed).
2. Run `dotnet restore` on `Azure.Functions.Cli.slnx`.
3. Run a Release build with `CI=true`.
4. Run the relevant test projects: Func.Tests, Workloads.Host.Tests, Workloads.Sdk.Tests, and the Workloads stack tests.

Confirm that `Terminal.Native.Probe` builds as part of the Func.Tests run. Any failure found here is fixed at the source and does not get suppressed.

Also confirm there are no `NU1605`/`NU1608` downgrade or conflict warnings and no new vulnerability warnings. `git status` should show only intended changes, with no stray artifacts from the upgrade tooling.

## Research findings (TaskExecutor, before execution)

- **Tree state**: HEAD `8675b35c` (tasks 01-03 committed on `fabiocav/fuzzy-robot`). `git status` only shows orchestrator-owned `scenario.json`/`tasks.md` modifications. SDK resolved by `global.json`: `dotnet --version` = 11.0.100.
- **Build outputs**: all bin/obj go to the git-ignored `out/` (`out/bin`, `out/obj`; `.gitignore:33 [Oo]ut/`; nothing under `out/` is tracked). Clean = `dotnet clean` + delete `out/`.
- **Solution** `Azure.Functions.Cli.slnx`: 28 projects — src (Abstractions, Func, Workloads.Host, Workloads.Sdk [netstandard2.0], ExtensionBundles, 6 stacks, 5 workers), 3 test fixtures, 9 test projects: Func.Tests, Workloads.Host.Tests, Workloads.Sdk.Tests, Workloads.{DotNet,Go,Java,Node,PowerShell,Python}.Tests.
- **Terminal.Native.Probe** (`test/Func.Tests/Console/Terminal/Probe/Terminal.Native.Probe.csproj`, not in slnx) is built by `ConPtySmokeTests` (Windows) via `dotnet build … -c Release -p:SpectreAssemblyPath=<Spectre.Console.dll>`; that test is skipped unless `FUNC_TERMINAL_NATIVE_TESTS=1`. So the Func.Tests run must be repeated with `FUNC_TERMINAL_NATIVE_TESTS=1` filtered to `ConPtySmokeTests` to actually build/run the probe (CONTRIBUTING.md L142).
- **Repo commands** (AGENTS.md L100/L297-300, dotnet.instructions.md L151, CONTRIBUTING.md): `dotnet restore`, `CI=true dotnet build -c Release` (TreatWarningsAsErrors = `$(CI)`), `dotnet test`.
- **Vulnerability/conflict check**: restore output scanned for NU1605/NU1608/NU1901-NU1904; plus `dotnet list Azure.Functions.Cli.slnx package --vulnerable --include-transitive`.
- **Assessment**: per execution.md §0, assessment queries are skipped for final validation. Task 02 baseline: restore clean, Release build 0/0, Func.Tests 3127 pass/3 skip; DotNet 79, Node 66, Python 56, Host 50, Java 39, Go 38, PowerShell 36, Sdk 35.
- **Stubs**: none expected (no `// STUB:` introduced by tasks 01-03) — re-checked during execution.
- **Finding during validation — upgrade-introduced vulnerability**: `dotnet list Azure.Functions.Cli.slnx package --vulnerable --include-transitive` flags Workloads.Host (+ Host.Tests): `System.Security.Cryptography.Pkcs 8.0.1` (High GHSA-846f-f764-76mx, Moderate GHSA-2fmv-rv2c-59x7; patched 8.0.2 / 10.0.13 / 11.0.0-rc.2+). Chain: WebHost → NuGet.Protocol → NuGet.Packaging (transitively pinned to central 8.0.0 by task 02) → Pkcs ≥ 8.0.1. Pre-upgrade base `6f818499` (exported via `git archive`, restored with SDK 10.0.404) resolved NuGet.Packaging 6.13.2 → Pkcs 6.0.4, not flagged → **new**. Func/Func.Tests already resolve Pkcs 11.0.0. Fix at source: central transitive pin `System.Security.Cryptography.Pkcs` 11.0.0 (on feed; `CentralPackageTransitivePinningEnabled=true`) in `eng/build/Packages.props`; only Host/Host.Tests graphs change (8.0.1 → 11.0.0).
- **Pre-existing (not introduced by upgrade)**: Workloads.Host `Microsoft.AspNetCore.WebUtilities 2.3.0` (Moderate GHSA-6527-9fp8-r4x6, advisory published 2026-11-10, patched 2.3.14) via unchanged `Microsoft.Azure.WebJobs.Script.WebHost 4.1055.100`; also flagged at base. Out of upgrade scope → reported, not changed.
- Restore emitted no NU190x despite `NuGetAuditMode=all` (assets `logs` empty) — `dotnet list --vulnerable` is the authoritative check here.
- **Decomposition**: validation-only task, single coherent unit (no source edits planned unless a failure appears) — atomic.

**Done when**: a clean `dotnet restore` succeeds. The Release build with `CI=true` finishes with 0 errors and 0 warnings. All relevant test projects pass, or any failures are shown to be pre-existing or environment-dependent and documented. The working tree contains only intended upgrade changes, all committed.
