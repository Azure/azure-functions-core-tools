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
