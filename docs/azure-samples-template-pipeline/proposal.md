## Why

Azure-Samples quickstart repositories are independently released source repositories, but the Azure Functions CLI template system consumes versioned `FuncTemplate` NuGet packages. A centrally managed supply pipeline is needed to onboard eligible repositories, detect new releases, convert immutable release snapshots into safe template packages, and publish those packages without requiring each sample repository to own packaging infrastructure.

## What Changes

- Establish `https://dev.azure.com/azfunc/internal/_git/func-templates` as the control-plane repository for Azure-Samples template packaging.
- Add PR-reviewed onboarding sources under `src/Quickstarts/*.yaml` and a shared JSON Schema at `src/Schema/quickstart.schema.json`.
- Add `eng/ci/validate-onboarding.yaml` to validate every onboarding file and enforce repository-wide uniqueness.
- Add `eng/ci/publish-releases.yaml` to scan onboarded GitHub repositories daily for eligible releases.
- Require published releases to use `v` followed by SemVer 2.0, support prereleases only through explicit opt-in, and use a required minimum version as the initial backfill boundary.
- Build packages from the exact commit referenced by each eligible release tag without executing repository-controlled code.
- Preserve and dry-run a valid authored root `.template.config/template.json` when onboarding omits `template`, or synthesize a project template with one required `.func/config.json` finalization action per declared `template.projects` entry.
- Exclude `.git`, `.github`, generated build output, credentials, and unsafe links from package content.
- Require an MIT or Apache-2.0 license detected from the release commit or declared through a reviewed override.
- Create NuGet packages under the `Azure.Functions.Templates.` prefix with package type `FuncTemplate`, source repository metadata, commit provenance, and a release-notes link to the GitHub release.
- Publish validated packages to an Azure Artifacts staging feed, request one approval for the successful packages in the pipeline run, and promote those exact package artifacts to NuGet.org.
- Process releases independently, retry transient failures, resume incomplete publication from feed state, and report partial success without blocking valid packages.

## Capabilities

### New Capabilities

- `azure-samples-template-pipeline`: PR-based repository onboarding, GitHub release discovery, safe template synthesis and packaging, staged approval, and NuGet.org publication for Azure-Samples quickstarts.

### Modified Capabilities

None.

## Impact

- Introduces the separate internal `func-templates` Azure DevOps repository and its source, schema, tooling, tests, and pipeline layout.
- Requires read-only GitHub API access, an Azure Artifacts staging feed, and NuGet.org publication credentials protected by an approval-gated environment.
- Produces packages compatible with the `FuncTemplate` ownership contract from `template-package-install`.
- Does not change Azure Functions CLI commands, package installation behavior, quickstart catalogs, or template discovery manifests.
