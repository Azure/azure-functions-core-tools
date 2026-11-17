# 03-pipelines-and-supporting-files: Update pipelines, container, docs, and agent guidance for .NET 11

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

## Research findings (task executor, before any source edit)

Confirmed against the repo at HEAD e003cacd (tasks 01/02 committed). Search: `git grep -n -I -E "net10|\.NET 10|10\.0\.|sdk:10|10\.x|TemplateEngine [0-9]|NuGet\.(Packaging|Protocol|Versioning|Credentials) ?[0-9]|6\.13\.2|10\.0\.301"` plus a case-insensitive pass for `dotnet-version|setup-dotnet|UseDotNet|net ?10\b`, excluding `.github/upgrades`.

Toolchain wiring (confirmed, no change needed):
- `global.json` = SDK 11.0.100, `rollForward: latestMinor`, `allowPrerelease: true` (task 01).
- `eng/ci/templates/steps/setup-dependencies.yml:7` `UseDotNet@2 useGlobalJson: true`; `.github/workflows/terminal-macos.yml:61` `actions/setup-dotnet@v4 global-json-file: global.json`. No other workflow/CI template pins an SDK.
- Scripts (`.github/skills/*/scripts/*.ps1`, `install.ps1`, `install.sh`) and `.github/instructions/*.md`: no .NET 10 / net10 references found.
- Central pins observed: Microsoft.TemplateEngine.* 11.0.100, NuGet.Packaging/Protocol/Versioning/Credentials 8.0.0. No doc cites a NuGet.* version; only the two TemplateEngine design docs cite a version.

Edits to apply (file:line → change):
- `eng/ci/templates/steps/setup-dependencies.yml:14`, `eng/ci/templates/jobs/stage-cli-artifacts.yml:57`: comment `# ESRP doesn't yet support net10 SDK` → `# ESRP tool requires the .NET 9 runtime (not yet on newer SDKs)`. Keep `version: 9.x`.
- `.devcontainer/devcontainer.json:3`: `sdk:10.0` → `sdk:11.0`.
- `AGENTS.md:6`, `README.md:107,142`, `docs/repo-structure.md:7`: .NET 10 → .NET 11.
- `docs/repo-structure.md:86`: stale "Pins .NET SDK 10.0.100" → "Pins .NET SDK 11.0.100".
- `docs/building-a-workload.md:58,85`, `.github/skills/create-workload/SKILL.md:34,169`: `net10.0` → `net11.0` (workloads build in-repo against the CLI's TFM).
- `proposed/workload-package-layout.md:190,941-942`: `net10.0` → `net11.0` (illustrative, CLI TFM).
- `docs/func-new-execution/design.md:9`, `docs/template-package-install/design.md:15`: "Microsoft.TemplateEngine 10.0.301" → "11.0.100" (bumped in task 02).

Deviation from task list:
- `docs/cli-architecture.md:137-138` sample `--target-framework` option with default `net10.0`: this illustrates the `func init` dotnet `--target-framework` option, whose real default (`DotNetProjectInitializer.DefaultFramework`) stays `net10.0` per the scenario Decision. Changing the sample to `net11.0` would contradict product behavior, so it is left unchanged as a product-behavior reference.

Intentional leftovers (product behavior / opaque data / unrelated numbers):
- `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs`, `README.md`, `release_notes.md`, `docs/func-init-execution/stack-inventory.md`, DotNet.Tests (`DotNetProjectInitializerTests.cs`, `DotNetSourceProjectTests.cs`): func init product behavior.
- `test/Func.Tests/Workloads/Storage/WorkloadStoreTests.cs:123` `lib/net10.0/Foo.dll`: opaque test data.
- `eng/build/Packages.props:19-20` System.Reflection.MetadataLoadContext / System.Text.Json 10.0.9: netstandard2.0 Workloads.Sdk pins kept per Decision (task 02 scope).
- `ProfileCommandTests.cs:71` `[9.0.0, 10.0.0)`, `AzureWebJobsStorageClassifierTests.cs:249` `10.0.0.1`: unrelated version/IP literals.

Decomposition: atomic — ~14 non-project text edits, no projects/builds affected. Execution stage `execution.md` + `breakdown-hints/common.md` evaluated (no hint matches: no TFM/package change, no project scope); `test.md` / framework-migration hints not applicable (no projects in scope). No stubs (no project code touched).

Validation plan: YAML parse of the two CI files, JSON(C) parse of devcontainer.json, re-run the residual search. No build needed (no build-relevant inputs change).
