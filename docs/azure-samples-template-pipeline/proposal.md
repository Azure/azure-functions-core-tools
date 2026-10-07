## Why

Azure-Samples quickstart repositories are independently released source repositories, but the Azure Functions CLI template system consumes versioned `FuncTemplate` NuGet packages. A centrally managed supply pipeline is needed to onboard eligible repositories, detect new releases, convert immutable release snapshots into safe template packages, and publish those packages without requiring each sample repository to own packaging infrastructure.

The source-scope extension preserves independent subfolder samples without mandatory per-stack aggregation. Discovery metadata and explicitly authorized guided use provide the streamlined selection/acquisition flow.

## What Changes

- Establish `https://dev.azure.com/azfunc/internal/_git/func-templates` as the control-plane repository for Azure-Samples template packaging.
- Add PR-reviewed onboarding sources under `src/Quickstarts/*.yaml` plus centrally owned onboarding and synthesis-descriptor schemas under `src/Schema/`.
- Add `eng/ci/validate-onboarding.yaml` to validate every onboarding file and enforce repository-wide onboarding uniqueness.
- Add `eng/ci/publish-releases.yaml` to scan onboarded GitHub repositories daily for eligible releases.
- Require published releases to use `v` followed by SemVer 2.0, support prereleases only through explicit opt-in, and use a required minimum version as the initial backfill boundary.
- Build packages from the exact commit referenced by each eligible release tag without executing repository-controlled code.
- Preserve and dry-run a valid authored root `.template.config/template.json`, or synthesize a project template from the release-owned `.github/azure-functions-template.yaml` descriptor with one required `.func/config.json` finalization action per declared project and a workload constraint derived from the declared stacks.
- Read the synthesis descriptor before excluding `.git`, `.github`, generated build output, credentials, and unsafe links from package content.
- Require an MIT or Apache-2.0 license detected from the release commit or declared through a reviewed override.
- Create NuGet packages under the `Azure.Functions.Templates.` prefix with package type `FuncTemplate`, source repository metadata, commit provenance, and a release-notes link to the GitHub release.
- Stage validated packages, request one approval for successful candidates, and promote their exact artifacts without rebuilding.
- Process independent package candidates, retry transient failures, and recover from feed state without silently omitting a selected definition.
- Extend source-owned definitions through a versioned schema to support multiple independently selectable scopes, while retaining authored/synthesized ownership and the no-source-code-execution rule.
- Validate every selected definition from the completed package, retaining independent groups, source-scope provenance, and notices.
- Supply verified publication evidence to the separate discovery component rather than treating package labels as curation proof.

## Capabilities

### New Capabilities

- `azure-samples-template-pipeline`: PR-based repository onboarding, GitHub release discovery, safe template synthesis and packaging, staged approval, and NuGet.org publication for Azure-Samples quickstarts.

### Modified Capabilities

None.

## Impact

- Introduces the separate internal `func-templates` Azure DevOps repository and its source, schema, tooling, tests, and pipeline layout.
- Requires read-only GitHub API access, an Azure Artifacts staging feed, and NuGet.org publication credentials protected by an approval-gated environment.
- Produces packages compatible with the `FuncTemplate` ownership contract from `template-package-install`.
- Depends on `template-engine-constraints` for the workload constraint form and on `template-engine-post-actions` for the trusted configuration finalization action.
- Does not change Azure Functions CLI commands, package installation behavior, quickstart catalogs, or template discovery manifests.
- Leaves versioned descriptor/assignment shape, package grouping, allowlist coverage, publication evidence, and identity migration as review questions.
