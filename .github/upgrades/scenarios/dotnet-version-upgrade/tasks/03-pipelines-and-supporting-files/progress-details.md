# Progress details — 03-pipelines-and-supporting-files

## Summary
Updated pipeline comments, the devcontainer image, docs, AGENTS.md, and the create-workload skill so references to the repo's own target framework/SDK say .NET 11 / `net11.0`. The ESRP `9.x` SDK installs remain. `func init` product-behavior references were left unchanged.

## Files modified
- `eng/ci/templates/steps/setup-dependencies.yml`: changed the comment on the ESRP `UseDotNet@2` step. `version: 9.x` is kept.
- `eng/ci/templates/jobs/stage-cli-artifacts.yml`: same comment change. `version: 9.x` is kept.
- `.devcontainer/devcontainer.json`: `mcr.microsoft.com/dotnet/sdk:10.0` → `sdk:11.0`.
- `AGENTS.md:6`: .NET 10 → .NET 11.
- `README.md:107,142`: target framework and prerequisites → .NET 11.
- `docs/repo-structure.md:7`: .NET 10 → .NET 11. `:86`: stale "Pins .NET SDK 10.0.100" → "11.0.100", which matches global.json.
- `docs/building-a-workload.md:58,85`: `net10.0` → `net11.0`.
- `.github/skills/create-workload/SKILL.md:34,169`: scaffold `<TargetFramework>` → `net11.0`. Replaced 2 occurrences with no BOM and line endings preserved.
- `proposed/workload-package-layout.md:190,941-942`: `net10.0` → `net11.0` in illustrative text.
- `docs/func-new-execution/design.md:9`, `docs/template-package-install/design.md:15`: Microsoft.TemplateEngine 10.0.301 → 11.0.100, matching `eng/build/Packages.props`.
- `src/Workloads/Stacks/DotNet/README.md:22`: "The CLI and workload binaries continue to target .NET 10." → "...target .NET 11." See the deviations below.

## Deviations from task.md
- **`docs/cli-architecture.md:137-138` was not changed.** The snippet shows the `func init` dotnet `--target-framework` option with default `net10.0`. The real default, `DotNetProjectInitializer.DefaultFramework`, stays `net10.0` per the scenario Decision. Changing the sample would contradict product behavior.
- **`src/Workloads/Stacks/DotNet/README.md`: only line 22 was changed.** The task said to leave this README unchanged for func-init references. Lines 5 and 13 are func-init behavior and were left alone. Line 22 describes the CLI and workload binaries' own TFM, which became false after task 02, so it was corrected.
- No docs cite NuGet.* versions, so nothing needed updating for the NuGet 8.0.0 bump.

## Toolchain wiring verification (no change needed)
- `global.json` has sdk 11.0.100, rollForward latestMinor, and allowPrerelease true.
- The CI build SDK comes from `UseDotNet@2 useGlobalJson: true` (setup-dependencies.yml:7). The GitHub workflow uses `actions/setup-dotnet@v4 global-json-file: global.json` (terminal-macos.yml:61). No other workflow or CI template pins a .NET SDK version.
- Scripts (`.github/skills/*/scripts/*.ps1`, `install.ps1`, `install.sh`) and `.github/instructions/*.md` contain no .NET 10 references.

## Validation (working dir = repo root)
- YAML parse with Python `yaml.safe_load` on both CI files: OK (exit 0).
- JSON parse of `devcontainer.json` (comment lines stripped) with Python `json.loads`: OK.
- `git grep -n "9.x" -- eng/ci` returns `stage-cli-artifacts.yml:61` and `setup-dependencies.yml:18`, so the ESRP installs are intact.
- No build or test run. No build-relevant inputs changed (docs, comments, devcontainer only).
- Residual search: `git grep -n -I -E "net10|\.NET 10|10\.0\.|sdk:10|10\.0\.301" -- . ":!.github/upgrades"`. Every remaining hit is intentional:
  - `docs/cli-architecture.md:137-138`: func init option sample (product behavior; see deviations).
  - `docs/func-init-execution/stack-inventory.md:14`, `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs:21,23,50,82`, `src/Workloads/Stacks/DotNet/README.md:5,13`, `release_notes.md:5`, `test/Workloads/Stacks/DotNet.Tests/*`: func init product behavior.
  - `eng/build/Packages.props:19-20`: System.Reflection.MetadataLoadContext and System.Text.Json 10.0.9, kept for netstandard2.0 Workloads.Sdk per the Decision (owned by task 02).
  - `test/Func.Tests/Workloads/Storage/WorkloadStoreTests.cs:123`: opaque test data.
  - `ProfileCommandTests.cs:71` `[9.0.0, 10.0.0)` and `AzureWebJobsStorageClassifierTests.cs:249` `10.0.0.1`: unrelated version and IP literals.

## Blockers
None.

## Review
- IntegrityReviewer verdict: PASS. No findings. No review.md was created because this was a clean first review. The reviewer confirmed that func init net10.0 behavior is unchanged and that the ESRP 9.x installs are still there.

