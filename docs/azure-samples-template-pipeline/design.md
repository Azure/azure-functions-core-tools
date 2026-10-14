## Context

See `proposal.md` for motivation and `specs/azure-samples-template-pipeline/spec.md` for the behavior contract.

Azure-Samples repositories release independently through GitHub. The package builder must consume those repositories as untrusted content, produce packages accepted by the `FuncTemplate` contract, and preserve a traceable connection to an immutable Git release without requiring packaging infrastructure in each source repository.

This draft extends the original single-template model so independent quickstarts can come from different folders of one repository. It does not require per-stack aggregation or private intermediate packages. Metadata browsing and authorized guided use provide the streamlined UX without prescribing package boundaries. Scope schemas and identity ownership remain under publishing review.

Publishing automation lives in the separate `func-templates` repository. Curated registration remains in the existing [reviewed source manifest](https://github.com/Azure/azure-functions-templates/blob/dev/Functions.Templates/Template-Manifest/manifest.json); it is not migrated to another manually maintained onboarding list:

```text
https://dev.azure.com/azfunc/internal/_git/func-templates
|
+-- src/
|   `-- Schema/
|       +-- publishing-input.schema.json
|       `-- azure-functions-template.schema.json
`-- eng/
    `-- ci/
        +-- validate-onboarding.yaml
        `-- publish-releases.yaml
```

The Azure Functions CLI, general template discovery, quickstart catalog generation, and package unlisting have separate owners and are not implemented by this repository.

## Goals / Non-Goals

**Goals:**

- Keep onboarding reviewable and deterministic without duplicating curated registrations.
- Convert eligible release snapshots into valid project template packages without executing source-controlled code.
- Use package feeds as the durable publication checkpoint rather than maintaining a parallel release ledger.
- Promote the exact validated artifact through a single approval gate.
- Make independent release failures retryable without blocking successful packages.
- Keep public package provenance useful without exposing pipeline implementation details.

**Non-Goals:**

- Generate or publish a dedicated quickstart catalog or general template discovery manifest.
- Change `func init`, `func new`, or TemplateEngine package-install behavior.
- Build, test, or execute source repository code during packaging.
- Support repositories outside `Azure-Samples`.
- Initialize Git submodules or Git LFS content.
- Define SBOM generation, package size limits, unlisting, or incident response.
- Own source-repository release conventions beyond the requirements needed for packaging.

## Decisions

### The existing curated manifest owns registration

The pipeline consumes a pinned, approved commit of the existing source manifest in `azure-functions-templates`. A CDN response, an arbitrary manifest override, an `author` string, or a package prefix is not authorization to publish. The source revision and approved publishing-policy revision are recorded with every package candidate.

Preserve existing entry IDs, names, repository URLs, folder paths, Git refs, and the legacy CDN URL/schema contract for CLI, editor, and other consumers. The source manifest gains a supported publishing extension; unknown extension versions fail validation rather than silently reinterpret legacy fields. Exact extension placement and schema versioning require the manifest and publishing owners' agreement and compatibility tests before rollout.

Conceptually, one reviewed record carries:

```text
existing sample id, repositoryUrl, folderPath, gitRef, display/filter metadata
  + explicit publishing approval and stable package assignment
  + named source-definition selection and release policy
```

An entry without the approved extension remains a legacy discovery record. It neither becomes a publication candidate nor disappears from older consumers. Approved publishing inputs require a stable `packageId`, named definition selection where needed, explicit enabled state, and release-selection policy. A reviewed license override is optional; source-release detection remains the default. Adding an unreviewed optional field does not grant approval.

The approved extension explicitly authorizes eligible-release enumeration with a minimum version/backfill boundary and prerelease policy. Retain the existing GitHub release, SemVer, source-allowlist, and pinned-commit checks. A legacy discovery `gitRef` neither authorizes publication nor supplies a minimum-version policy; mutable branch refs are not eligible package snapshots.

The extension selects source-owned definition IDs, not Functions project topology. Existing `folderPath` remains discovery/content-scope metadata; a publishing candidate must verify its relationship to the selected source definition and reject mismatches. Do not duplicate `name`, `shortName`, project declarations, or authored parameters as separately authoritative publishing definitions.

The exact release snapshot owns synthesis metadata in `.github/azure-functions-template.yaml`:

```yaml
schemaVersion: 1

identity: Azure.Functions.Templates.PythonOpenAIChat
shortName: functions-python-openai-chat
name: Azure Functions Python OpenAI Chat
description: Create an Azure Functions application using Azure OpenAI.

projects:
  - root: .
    stack: python
    language: Python
```

Every field other than `schemaVersion` belongs to the template definition rather than onboarding. The descriptor requires non-empty `identity`, `shortName`, `name`, `description`, and `projects`. Each project declares:

```text
root      repository-relative Functions project root
stack     canonical func stack identifier
language  canonical language recognized by that stack
```

The root project uses `.`. Other roots use normalized `/`-separated relative paths. Roots cannot be absolute, contain `..`, use excluded directories, collide case-insensitively, or overlap through ancestor/descendant nesting. The staged release must contain a regular `host.json` directly under every declared root.

The existing manifest schema continues to govern legacy discovery fields. The publishing-input schema validates the approved extension and rejects unknown publishing fields/versions without pretending to change every existing consumer. The source descriptor schema validates repository-owned definitions. Whole-manifest validation owns registration collisions, source policy, package assignments, release policy, and license overrides; release packaging owns actual scopes, projects, raw constraints, and TemplateEngine validation.

The pipeline may generate an internal normalized input from that one reviewed source, but it must be reproducible from the pinned revision and policy. A generated projection is not a second author-maintained registry. Invalid input fails as a whole before package candidates are accepted.

**Alternative considered:** require authors to copy the current registrations into central YAML. That duplicates curation and forces a registration migration unrelated to package-backed execution. Existing pipeline YAML and repository-owned synthesis descriptors remain; only the competing central registration list is removed.

### Explicit operational and package identities remain stable

The existing manifest entry `id` stays a stable registration/discovery key, not a new internal ID replacing it. A publishing `packageId` is explicit, persisted, reviewed, and reserved under `Azure.Functions.Templates.`. Tooling may propose an initial ID from the stable entry ID, but must validate legal encoding, case-insensitive collisions, and namespace ownership before persisting it. Repository/display/template-name changes must not regenerate public package IDs during a build.

Entries may share a repository or point to separate subfolders. One package assignment may include several source definitions, and several catalog entries may reference that same assignment. Those references must agree on source, release policy, complete selected-definition set, and package identity. Conflicting assignments or one template identity owned by competing packages are rejected. A separate reviewed identity migration is required for intentional ownership changes.

### Independent source scopes preserve sample choices

The [Python connectors source](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1) exposes [Office 365](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/office365App) and [SharePoint](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/sharepointApp) as separate samples. They remain independent templates, whether published together or in separate scoped packages. Discovery maps each choice to its actual package reference. Basic project/item companions remain outside this producer.

A versioned repository-owned descriptor enumerates named definitions, normalized content roots, and exactly one authored or synthesized configuration per definition. Each owns its identity, short name, and Functions project topology. Extended onboarding may select definition IDs for a package, but cannot supply project paths or overwrite source-owned metadata. The exact schema and package-grouping policy remain review questions.

Acquire an exact eligible source release and validate each scope against traversal, links, exclusions, and collisions. Project roots are relative to that scope. Preserve authored configuration byte-for-byte; synthesized actions cover only its declared projects. Retain source-root license material even when a selected folder has no local license. Isolated template roots prevent Office 365 selection from copying SharePoint or unrelated source folders.

Independent definition content roots must be disjoint: identical or ancestor/descendant roots are rejected after normalization, including aliases introduced by links. Shared source files require explicit membership in the versioned descriptor for every consuming definition, with normalized source paths and destination paths relative to that definition's isolated output. Shared membership does not allow root overlap or implicit copying of a sibling definition. Apply containment, link, exclusion, destination-collision, and license checks to the shared inputs as well as each local scope.

Each definition's effective content set includes all declared shared inputs. Its content digest, package provenance, and relevant license notices must identify those inputs, so changing one shared file changes every consuming definition's evidence. Missing or invalid shared membership fails the affected package rather than silently omitting content. Exact field names and schema-version rollout remain publishing-owner decisions, but these ownership and isolation rules are required by the proposal.

The completed package must discover exactly its reviewed definitions and keep independent samples in distinct groups. Shared groups are allowed only for intentional variants of one definition. Reject duplicate identities, ambiguous short names, missing definitions, invalid actions, and unintended output scopes. One selected definition's failure blocks its package; unrelated valid packages can continue.

Package versions remain source-release-derived. Provenance records selected definition IDs, normalized scopes, source commit, and content digests outside scaffolded roots. Changed scope or content under an existing package ID/version is an immutable conflict. No private source-unit feed or independent aggregate version is required.

Functions declarations remain authoritative. A Python Functions sample with a JavaScript frontend is not automatically mixed-stack. A sample with Python and Node Functions projects keeps both constraints rather than an arbitrary primary-stack label or duplicated identities. Discovery can describe those requirements without changing the package boundary.

### Publication supplies evidence, not the discovery manifest

The reviewed source manifest is the single curated registration and discovery authority. Publishing consumes its approved extension and emits validated package/provenance results. The manifest/discovery owner adds package-aware browsing references only after verified publication, preserving legacy fields and URL consumers. Generated results must not overwrite reviewed policy or manufacture approval for unpublished packages. The public CDN view is distribution output, not a second source of publishing authorization or the installed TemplateEngine catalog.

General feed scanning is not a dependency of this initial curated flow. Broader discovery may later scan our and third-party packages under its own policies. Generic explicit template package installation remains available; this is not a ban on community-authored packages or a requirement to preinstall quickstarts.

The approval record must identify the published artifact digest, package/version, source definitions, and reviewed publication so discovery can verify sanction. A self-declared tag or reserved prefix is not proof. Evidence transport and authentication belong to discovery and publisher-trust review; this draft does not invent a signed-index protocol or broaden the source allowlist.

**Alternative considered:** require every quickstart in one package per stack. Guided use can acquire the selected package without manual package-ID discovery, so aggregation is not needed for that UX. A future aggregation policy may be separate work, but is not a prerequisite here.

**Alternative considered:** derive package ID from repository name. Repository renames, normalization collisions, and NuGet namespace ownership would make package identity unstable. It is rejected.

**Alternative considered:** keep synthesized template metadata or project paths in onboarding. That makes repository structure and template presentation depend on a separately versioned control-plane file. It is rejected so the release commit atomically owns its content and synthesis descriptor.

### Explicit release policy governs discovery and backfill

For approved enabled assignments, the scheduled pipeline enumerates published releases with the configured minimum version/backfill boundary. This preserves the original release-discovery behavior without inferring enumeration consent from a legacy discovery record alone.

Release eligibility is:

```text
published GitHub release
+ non-draft
+ tag matches v<SemVer 2.0>
+ no SemVer build metadata
+ version >= approved minimumVersion
+ prerelease enabled when GitHub marks it prerelease
```

Build metadata is rejected because NuGet normalization can map distinguishable SemVer tags to a conflicting package identity. GitHub release enumeration follows pagination and does not infer releases from tags alone.

The tag is resolved to an exact commit independently of `target_commitish`. The package version removes the leading `v`; repository metadata records the resolved SHA.

**Alternative considered:** process releases created after the onboarding merge timestamp. Timestamps do not express intentional backfill and behave poorly when releases are imported or republished. It is rejected.

### Feed state replaces a separate processing ledger

The staging feed and NuGet.org provide the durable state machine for published template packages:

```text
absent from staging, absent from NuGet.org
  -> build and stage

present in staging, absent from NuGet.org
  -> await or retry promotion using staged artifact

present in staging, present in NuGet.org
  -> complete
```

Every query includes package ID/version and verifies source commit and selected-definition provenance. Changed definitions or content under an existing identity are conflicts even when the repository commit is unchanged. Staging-only state is resumable promotion of the same validated package.

This design does not use Azure Pipeline artifacts as state because their lifetime follows run retention. It also avoids a separate database whose records could diverge from actual feed publication.

**Alternative considered:** commit processed releases into the onboarding repository. That would generate operational commits, serialize pipeline activity with onboarding, and represent attempted rather than actual publication. It is rejected.

### Packaging never executes repository code

The pipeline acquires the exact release-tag snapshot into an isolated staging directory. Central packaging tooling performs copying, metadata generation, TemplateEngine validation, NuGet packing, and archive inspection. It does not invoke source build instructions or package managers.

Before filtering, the pipeline reads at most the known `.github/azure-functions-template.yaml` descriptor needed to select and validate synthesis mode. The content filter then removes:

- `.git`;
- `.github`;
- known build output and dependency-cache directories;
- detected credential material;
- links that resolve outside staging.

Git submodules and Git LFS are outside the initial design and are not initialized. The final archive is inspected independently so an error in staging filters cannot publish excluded content.

**Alternative considered:** run a repository-owned packaging script. That distributes infrastructure across sample repositories and executes untrusted code with publication credentials. It is rejected.

### Authored and synthesized template modes are mutually exclusive

The exact release snapshot selects exactly one template source:

```text
root .template.config/template.json exists
  -> preserve and validate authored template

root .github/azure-functions-template.yaml exists
  -> synthesize template configuration

both present
  -> fail: redundant and conflicting ownership

neither present
  -> fail: no template can be generated
```

These fixed locations describe the original single-definition mode. The versioned extension declares multiple source-owned definitions and roots. Extended onboarding selects named definition IDs rather than arbitrary project/configuration paths. Each follows authored-or-synthesized ownership and safe scope validation.

A root authored configuration is preserved byte-for-byte. The same validation applies to an authored configuration selected by the extended source descriptor. A versioned multi-definition descriptor may enumerate authored configurations in subfolders, but cannot synthesize or override the same definition as well. The original single-root coexistence rejection remains limited to the original mode. The central validator loads and dry-runs each authored definition through Microsoft.TemplateEngine and requires:

- valid identity and short-name metadata;
- `tags.type` equal to `project`;
- workload constraints, in the form `template-engine-constraints` defines, that require every stack its configuration actions declare;
- at least one trusted Functions project configuration finalization action;
- every active configuration action to reference a resolved primary output and supply canonical stack and language;
- no direct `.func/config.json` template content;
- no behavior rejected by the centrally defined safety policy.

Each authored definition owns its metadata, topology, parameters, conditions, outputs, and actions and cannot also be synthesized. A multi-definition descriptor may reference an authored subfolder configuration but cannot overwrite it. The original single-root coexistence restriction remains limited to original mode. Invalid authored definitions are rejected rather than replaced with synthesis.

When the authored file is absent, `.github/azure-functions-template.yaml` supplies the complete synthesized template metadata and project topology. For each project, the packager:

1. Requires `<root>/host.json` in the filtered release snapshot.
2. Adds `<root>/host.json` as a primary output.
3. Adds one mandatory trusted configuration finalization action referencing that primary output and carrying the declared canonical stack and language.

The generated template uses the definition's identity, short name, name, and description, sets `tags.type` to `project`, and treats its filtered content scope as content. For the original single-root form this is the complete filtered snapshot. It defines no parameter symbols, replacements, or ordinary post-actions. It emits a singular language tag only when all declared projects have the same language; mixed-language topology is represented exclusively by the configuration actions.

The packager also adds one workload constraint per declared project stack, in the form `template-engine-constraints` defines. The descriptor has no workload field, so requirements always follow the declared projects.

Both modes pass the same TemplateEngine load, dry-run, action, output-path, and package safety validation during release packaging. Onboarding PR validation does not acquire source releases or validate repository-owned template definitions.

Template authors own the full end-to-end tests for their generated applications, normally in the source repositories. The central packager owns format, scope, provenance, action, path, and artifact safety checks, not execution of repository-controlled tests or generated application code. Release qualification can reference author-owned evidence tied to the exact source revision, package version, and tested CLI version. A source test pass is not proof that the packaged artifact preserves that source or that the CLI can consume it; packaging checks and CLI compatibility sanity checks remain separate. The evidence-report format and storage location are review decisions.

**Alternative considered:** inject generated actions into an authored file. This changes source-owned behavior without a source PR and cannot safely reproduce authored conditions or rename behavior. It is rejected.

**Alternative considered:** require the synthesis descriptor alongside authored templates as a topology assertion. That duplicates source-owned topology and creates conflicting authorities. It is rejected.

### License detection is pinned to release content

The allowlist contains SPDX `MIT` and `Apache-2.0`. Detection order is:

```text
reviewed publishing license override
  -> GitHub repository-license API with ref=<release commit>
  -> inspect root license file in staged release snapshot
  -> fail
```

An override handles recognized license text that GitHub cannot classify, but a source-root license file is still required. Scoped packages retain the release license and notices even when a selected folder has no license. This proposal does not combine independently licensed repositories or expand the source-license allowlist.

Default-branch license metadata is not authoritative because it may differ from the packaged release.

**Alternative considered:** accept any SPDX expression returned by GitHub. Publication rights and review requirements differ by license; the initial supply chain intentionally admits only the two approved licenses.

### NuGet metadata carries public provenance

The central packager generates package metadata from the reviewed assignment and exact source release. Multi-definition packages also carry their selected-definition/scope provenance:

| NuGet value | Source |
|---|---|
| ID | onboarding `packageId` |
| Version | release tag without `v` |
| Package type | `FuncTemplate` |
| Description | effective authored or synthesized template description |
| License | effective SPDX expression |
| Project URL | canonical GitHub repository URL |
| Repository type | `git` |
| Repository URL | canonical GitHub repository URL |
| Repository commit | resolved release-tag commit |
| Release notes | corresponding GitHub release URL |

GitHub release ID is unnecessary because the tag, commit, repository, and release URL identify the source release. Azure Pipeline definition and run IDs remain private implementation details and are not embedded. SBOM generation is deferred.

### Staging and approval promote one immutable artifact

The publication pipeline has distinct phases:

```text
discover
  -> build and validate candidates independently
  -> publish successful candidates to Azure Artifacts staging
  -> summarize successful and failed candidates
  -> one approval for the successful run set
  -> download exact staged packages
  -> verify again
  -> push unchanged packages to NuGet.org
```

One failed selected definition blocks its package, but unrelated valid packages may stage and pass through the shared approval. The final run reports failure when unresolved failures remain.

Promotion never rebuilds. Revalidation confirms staged identity, hash, provenance, and package safety before pushing the same bytes to NuGet.org.

Approval identifies completed packages, exact source revisions, and selected definitions. It never authorizes a silently reduced package. Discovery publication remains a separate operation after approved packages exist.

**Alternative considered:** require approval for each package. Daily batches would create unnecessary approval load without improving artifact isolation. It is rejected.

**Alternative considered:** automatically promote after staging. A single human gate is required before public publication and is retained.

### Recovery reuses normal pipeline behavior

Transient GitHub and feed calls use bounded exponential backoff. Daily runs recover incomplete publication from feed state. Manual recovery targets onboarding/package ID and optional release version, including the reviewed definition selection, and uses the same validation, approval, and immutable state rules without force overwrite.

Only one publication run holds the feed mutation lock. A manual run never bypasses immutable identity checks and no force-overwrite option exists.

Every run reports discovered, staged, promoted, already-complete, skipped, and failed releases. Exhausted failures notify one central `func-templates` operations team. Repository-specific routing is deferred until operational scale justifies additional onboarding metadata.

## Risks / Trade-offs

- **[A release contains an invalid synthesis descriptor]** -> Fail that release before staging and report descriptor diagnostics; source-repository pre-release validation can be added later without changing package semantics.
- **[Moved tags undermine release immutability]** -> Compare the resolved commit with feed metadata and reject conflicts rather than repackaging.
- **[A malicious repository can contain hostile files]** -> Never execute repository code, isolate staging, scan content, reject escaping links, and inspect the final archive.
- **[GitHub license detection can be incomplete]** -> Inspect the release license file and allow a reviewed expression override while retaining the file requirement.
- **[One approval can authorize many packages]** -> Present the complete successful set and failures before the gate, and promote only already validated staging artifacts.
- **[Staging and NuGet.org can diverge]** -> Treat staging-only state as resumable promotion and verify provenance on both feeds.
- **[Ignoring submodules or LFS can leave an incomplete sample]** -> Document the limitation and require source repositories to release self-contained content until support is designed.

## Migration Plan

Before scoped publication, agree versioned descriptor/assignment schemas and validate independent subfolder examples without publication. Map every supported manifest entry to a package/template reference and record release, license, authoring, source-allowlist, and mixed-stack gaps. Preserve published identities or plan their transition explicitly. The original one-definition publisher is not equivalent merely because the engine supports several templates.

1. Create the `func-templates` repository with source, schema, central tooling, tests, and both pipeline definitions.
2. Reserve and configure the `Azure.Functions.Templates.` prefix and package ownership on NuGet.org.
3. Provision the Azure Artifacts staging feed, GitHub read identity, feed publication identities, approval-gated NuGet.org environment, mutation lock, and central notifications.
4. Add representative onboarding entries through PRs and validate package construction without publication.
5. Publish representative packages to staging and verify installation through the existing `FuncTemplate` package path.
6. Enable the shared approval gate and promote the exact staged packages to NuGet.org.
7. Enable the daily schedule after end-to-end publication succeeds.

Rollback disables the scheduled pipeline and promotion environment. Packages already published remain immutable; unlisting and incident response are owned outside this design.

## Review Questions

1. What versioned descriptor and package-assignment shape selects named scopes without duplicating topology?
2. When should definitions share a package versus use separate scoped packages, and how are duplicate installed identities prevented?
3. What approval evidence should discovery consume to verify curation and exact artifacts?
4. Which approved path covers supported sources outside the initial Azure-Samples-only policy?
5. How should published identities transition while preserving sample names and mixed-stack requirements?
