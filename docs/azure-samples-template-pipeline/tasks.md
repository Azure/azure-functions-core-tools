## 1. Repository and Tooling Foundation

- [ ] 1.1 Create the `func-templates` repository in `azfunc/internal` with `src/Quickstarts`, `src/Schema`, central tooling and test projects, and `eng/ci`.
- [ ] 1.2 Add dependency restore, build, test, and code-quality configuration for the central packaging tooling.
- [ ] 1.3 Add shared typed models for onboarding entries, effective metadata, GitHub releases, package provenance, feed state, and per-release outcomes.
- [ ] 1.4 Add dependency-injected boundaries for filesystem, GitHub API, source acquisition, TemplateEngine validation, NuGet packing, feeds, time, retries, and reporting.

## 2. Onboarding Schema and Loading

- [ ] 2.1 Author `src/Schema/quickstart.schema.json` for schema version 1 with strict properties, optional synthesis-only `template`, required non-empty `template.projects` when present, project fields, defaults, formats, package prefix, repository owner, and license allowlist.
- [ ] 2.2 Implement non-recursive ordinal scanning of `src/Quickstarts/*.yaml` and combine zero-or-more entries from each file into one logical manifest.
- [ ] 2.3 Implement schema validation with file, entry, property, and source-location diagnostics.
- [ ] 2.4 Resolve defaults for enablement, prerelease policy, synthesis-only template identity, short name, display name, description, and license.
- [ ] 2.5 Implement deterministic repository-name title casing and canonical GitHub repository URL construction.
- [ ] 2.6 Enforce case-insensitive global uniqueness for onboarding ID, repository, package ID, synthesized effective template values, and authored identity and short names resolved during source-aware validation.
- [ ] 2.7 Add unit tests for authored and synthesized entry shapes, valid project roots, missing or empty projects, malformed YAML, unknown fields, invalid formats, defaults, title casing, and every global collision.

## 3. Pull Request Validation

- [ ] 3.1 Add a validation command that parses every onboarding source and applies schema and repository-wide rules atomically.
- [ ] 3.2 Add source-aware validation that acquires each enabled entry's latest eligible published release and resolves authored-versus-synthesized template ownership.
- [ ] 3.3 Create `eng/ci/validate-onboarding.yaml` as the required PR pipeline.
- [ ] 3.4 Load and dry-run every resolved project template, validate configuration actions and effective identities, and fail the complete PR result when any entry is unavailable or invalid.
- [ ] 3.5 Add pipeline fixtures proving malformed YAML, no eligible validation release, conflicting identities, dual or missing template ownership, and one failed dry-run fail the complete validation result.

## 4. GitHub Release Discovery

- [ ] 4.1 Implement authenticated, paginated release enumeration for enabled `Azure-Samples` repositories.
- [ ] 4.2 Filter drafts, tag-only refs, versions below `minimumVersion`, and prereleases not explicitly enabled.
- [ ] 4.3 Parse `v`-prefixed SemVer 2.0 tags, reject build metadata, and derive the NuGet version.
- [ ] 4.4 Resolve each eligible release tag to an exact commit independently of `target_commitish`.
- [ ] 4.5 Add bounded retry and cancellation behavior for GitHub API and tag-resolution operations.
- [ ] 4.6 Add tests for pagination, drafts, tag-only refs, stable/prerelease policies, minimum versions, malformed versions, build metadata, missing tags, and moved tags.

## 5. Safe Release Staging

- [ ] 5.1 Implement isolated acquisition of the exact release-tag commit without initializing Git submodules or Git LFS.
- [ ] 5.2 Implement filtered staging that excludes `.git`, `.github`, generated build output, dependency caches, and configured credential findings.
- [ ] 5.3 Detect and reject symbolic or equivalent links that escape the staging root.
- [ ] 5.4 Ensure source-controlled build, package-manager, and pipeline code is never executed by staging or packaging.
- [ ] 5.5 Inspect the final staged content independently and fail when excluded content remains.
- [ ] 5.6 Add adversarial tests for workflows, credentials, traversal, escaping links, build instructions, submodules, LFS pointers, and cleanup after failure or cancellation.

## 6. License Resolution

- [ ] 6.1 Implement license resolution from reviewed YAML override, GitHub repository-license API at the release commit, and root release license-file inspection.
- [ ] 6.2 Allow only SPDX `MIT` and `Apache-2.0`, and require a root license file even when an override is supplied.
- [ ] 6.3 Preserve the root license file as template content and project the effective expression into package metadata.
- [ ] 6.4 Add tests for both allowed licenses, GitHub detection, file fallback, valid override, missing file, unknown detection, and unsupported expressions.

## 7. Template Configuration

- [ ] 7.1 Detect only the root `.template.config/template.json` as an authored template configuration.
- [ ] 7.2 Reject onboarding `template` when authored configuration exists, and reject generation when neither authored configuration nor `template.projects` exists.
- [ ] 7.3 Preserve authored configuration byte-for-byte and validate TemplateEngine loading, dry-run, identity, short names, project type, required configuration finalization actions, resolved primary outputs, and safety policy.
- [ ] 7.4 Validate each synthesized project root for normalized relative syntax, case-insensitive uniqueness, non-overlap, excluded paths, and a regular direct-child `host.json`.
- [ ] 7.5 Validate canonical stack and language compatibility for every synthesized project.
- [ ] 7.6 Synthesize project `template.json` from effective metadata, adding each project `host.json` as a primary output and one mandatory trusted configuration finalization action per project.
- [ ] 7.7 Omit parameter symbols, replacements, and ordinary post-actions from synthesized configuration; emit a singular language tag only for homogeneous project topology.
- [ ] 7.8 Validate synthesized configuration through the same TemplateEngine load, dry-run, action, output-path, and safety path as authored configuration.
- [ ] 7.9 Add tests for valid authored configuration, byte preservation, dual and missing ownership, invalid actions, unsafe authored behavior, root and nested synthesized projects, path attacks, overlapping roots, missing host files, mixed stacks and languages, metadata derivation, and dry-run failures.

## 8. NuGet Package Construction and Validation

- [ ] 8.1 Generate package metadata for explicit ID, release-derived version, `FuncTemplate` package type, description, license, project URL, Git repository URL, commit SHA, and GitHub release-notes link.
- [ ] 8.2 Pack the filtered source and effective template configuration without adding Azure Pipeline definition or run metadata.
- [ ] 8.3 Inspect the completed `.nupkg` and verify package identity, version, type, metadata, source commit, release URL, content, and exclusions.
- [ ] 8.4 Load and dry-run templates from the completed package through Microsoft.TemplateEngine and require valid project type, configuration finalization actions, and resolved primary outputs.
- [ ] 8.5 Produce deterministic package hashes for staging and promotion verification.
- [ ] 8.6 Add package-level tests for authored and synthesized single- and multi-project templates, configuration actions, dry-run effects, metadata provenance, license inclusion, exclusions, invalid archives, and TemplateEngine discovery.

## 9. Feed State and Staging

- [ ] 9.1 Implement exact package ID/version lookup and package download for the Azure Artifacts staging feed and NuGet.org.
- [ ] 9.2 Verify repository commit provenance for every package found in either feed and report immutable version conflicts.
- [ ] 9.3 Model absent, staging-only, and complete feed states without a separate processing ledger.
- [ ] 9.4 Publish newly validated packages to staging and treat the staged artifact as the durable promotion source.
- [ ] 9.5 Prevent concurrent feed mutation by scheduled and manual publication runs.
- [ ] 9.6 Add tests for all feed states, conflicting commits, idempotent reruns, concurrent runs, and interrupted staging.

## 10. Approval and NuGet.org Promotion

- [ ] 10.1 Build one run summary containing every successfully staged package and all separately failed releases.
- [ ] 10.2 Configure one approval-gated environment for promoting the complete successful run set.
- [ ] 10.3 Download each exact staged package after approval and revalidate identity, provenance, safety, and hash without rebuilding.
- [ ] 10.4 Push unchanged approved packages to NuGet.org and verify resulting feed provenance.
- [ ] 10.5 Preserve staging-only packages when approval is withheld or promotion is interrupted so a later run can resume.
- [ ] 10.6 Add tests for multi-package approval, partial pre-staging failure, withheld approval, exact-artifact promotion, interrupted promotion, and immutable destination conflicts.

## 11. Scheduled and Manual Publication Operations

- [ ] 11.1 Create `eng/ci/publish-releases.yaml` with daily discovery, independent candidate processing, staging, summary, approval, promotion, and final outcome stages.
- [ ] 11.2 Add manual onboarding-ID and optional version filters that reuse the complete scheduled pipeline path.
- [ ] 11.3 Apply bounded exponential backoff to transient GitHub, network, staging-feed, and NuGet.org operations.
- [ ] 11.4 Continue independent candidates after item failures, fail the final run when unresolved failures remain, and report discovered, staged, promoted, complete, skipped, and failed counts.
- [ ] 11.5 Notify the central `func-templates` operations team after retries are exhausted and redact credentials and tokens from logs and summaries.
- [ ] 11.6 Add end-to-end pipeline tests for scheduled discovery, filtered manual recovery, partial success, retries, cancellation, redaction, and notification.

## 12. Deployment and Documentation

- [ ] 12.1 Document mutually exclusive authored and synthesized onboarding, `template.projects`, derived metadata and actions, release conventions, license policy, and source-aware PR validation diagnostics.
- [ ] 12.2 Document publication states, the shared approval gate, manual recovery, immutable conflicts, and central operational ownership.
- [ ] 12.3 Provision GitHub read access, Azure Artifacts staging, NuGet.org prefix ownership and credentials, concurrency controls, approval environment, and notifications with least privilege.
- [ ] 12.4 Run a no-publication canary against representative authored and synthesized repositories and inspect the resulting packages.
- [ ] 12.5 Stage representative packages and verify installation through the Azure Functions CLI `FuncTemplate` package path.
- [ ] 12.6 Approve and promote the exact canary artifacts, verify NuGet.org metadata and installation, then enable the daily schedule.
