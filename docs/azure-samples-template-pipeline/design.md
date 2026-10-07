## Context

See `proposal.md` for motivation and `specs/azure-samples-template-pipeline/spec.md` for the behavior contract.

Azure-Samples repositories release independently through GitHub. The package builder must consume those repositories as untrusted content, produce packages accepted by the `FuncTemplate` contract, and preserve a traceable connection to an immutable Git release without requiring packaging infrastructure in each source repository.

This draft extends the original single-template model so independent quickstarts can come from different folders of one repository. It does not require per-stack aggregation or private intermediate packages. Metadata browsing and authorized guided use provide the streamlined UX without prescribing package boundaries. Scope schemas and identity ownership remain under publishing review.

The control plane lives separately from both Azure-Samples and Azure Functions Core Tools:

```text
https://dev.azure.com/azfunc/internal/_git/func-templates
|
+-- src/
|   +-- Quickstarts/
|   |   `-- *.yaml
|   `-- Schema/
|       +-- quickstart.schema.json
|       `-- azure-functions-template.schema.json
`-- eng/
    `-- ci/
        +-- validate-onboarding.yaml
        `-- publish-releases.yaml
```

The Azure Functions CLI, general template discovery, quickstart catalog generation, and package unlisting have separate owners and are not implemented by this repository.

## Goals / Non-Goals

**Goals:**

- Make onboarding reviewable, deterministic, and safe to divide across files.
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

### YAML files form one logical source manifest

Onboarding sources use this shape:

```yaml
schemaVersion: 1

quickstarts:
  - id: python-openai-chat
    repository: Azure-Samples/functions-python-openai-chat
    packageId: Azure.Functions.Templates.PythonOpenAIChat
    enabled: true

    release:
      minimumVersion: 1.0.0
      includePrerelease: false

    licenseExpression: MIT
```

`schemaVersion`, `quickstarts`, `id`, `repository`, `packageId`, and `release.minimumVersion` are always required. Onboarding contains no template metadata or project paths.

Onboarding defaults are resolved before validation:

```text
enabled                    -> true
release.includePrerelease  -> false
licenseExpression          -> detection from release commit
```

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

The onboarding JSON Schema owns control-plane structure and rejects unknown fields. A separate centrally owned descriptor schema validates `.github/azure-functions-template.yaml`. Repository-wide onboarding validation owns central identity collisions, GitHub slug restrictions, SemVer validation, package-prefix policy, and license policy. Release packaging owns descriptor metadata, project path safety, canonical stack/language pairs, and TemplateEngine validation against the exact tagged content.

Files are scanned non-recursively in ordinal filename order. The order exists only for deterministic diagnostics; it does not establish precedence. Duplicate values are errors rather than last-write-wins behavior.

**Alternative considered:** keep every repository in one YAML file. This creates a merge hotspot and makes ownership grouping difficult. It is rejected.

**Alternative considered:** require one repository per file. The filename would become an accidental identity surface and would complicate grouping and migration. Files therefore contain zero or more entries.

### Explicit operational and package identities remain stable

The onboarding `id` is an internal operational key. `packageId` is explicitly reviewed and must use the reserved `Azure.Functions.Templates.` prefix. Neither is derived from mutable GitHub metadata.

Onboarding and package IDs remain globally unique case-insensitively. The original schema retains repository uniqueness. An explicitly versioned extension may select named source definitions for separate packages from one repository or several definitions for one package. It validates disjoint package-to-template assignments instead of silently accepting duplicate repositories under the old schema. Competing packages must not publish the same template identity without an explicit migration plan.

### Independent source scopes preserve sample choices

The [Python connectors source](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1) exposes [Office 365](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/office365App) and [SharePoint](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/sharepointApp) as separate samples. They remain independent templates, whether published together or in separate scoped packages. Discovery maps each choice to its actual package reference. Basic project/item companions remain outside this producer.

A versioned repository-owned descriptor enumerates named definitions, normalized content roots, and exactly one authored or synthesized configuration per definition. Each owns its identity, short name, and Functions project topology. Extended onboarding may select definition IDs for a package, but cannot supply project paths or overwrite source-owned metadata. The exact schema and package-grouping policy remain review questions.

Acquire an exact eligible source release and validate each scope against traversal, links, exclusions, and collisions. Project roots are relative to that scope. Preserve authored configuration byte-for-byte; synthesized actions cover only its declared projects. Retain source-root license material even when a selected folder has no local license. Isolated template roots prevent Office 365 selection from copying SharePoint or unrelated source folders.

The completed package must discover exactly its reviewed definitions and keep independent samples in distinct groups. Shared groups are allowed only for intentional variants of one definition. Reject duplicate identities, ambiguous short names, missing definitions, invalid actions, and unintended output scopes. One selected definition's failure blocks its package; unrelated valid packages can continue.

Package versions remain source-release-derived. Provenance records selected definition IDs, normalized scopes, source commit, and content digests outside scaffolded roots. Changed scope or content under an existing package ID/version is an immutable conflict. No private source-unit feed or independent aggregate version is required.

Functions declarations remain authoritative. A Python Functions sample with a JavaScript frontend is not automatically mixed-stack. A sample with Python and Node Functions projects keeps both constraints rather than an arbitrary primary-stack label or duplicated identities. Discovery can describe those requirements without changing the package boundary.

### Publication supplies evidence, not the discovery manifest

The pipeline publishes validated packages and source provenance. The separate discovery component scans configured feeds under its curation policy, maps templates to exact package/version/source references, and publishes the CLI's cached metadata manifest. Onboarding YAML is not the public index or the installed TemplateEngine catalog.

The approval record must identify the published artifact digest, package/version, source definitions, and reviewed publication so discovery can verify sanction. A self-declared tag or reserved prefix is not proof. Evidence transport and authentication belong to discovery and publisher-trust review; this draft does not invent a signed-index protocol or broaden the source allowlist.

**Alternative considered:** require every quickstart in one package per stack. Guided use can acquire the selected package without manual package-ID discovery, so aggregation is not needed for that UX. A future aggregation policy may be separate work, but is not a prerequisite here.

**Alternative considered:** derive package ID from repository name. Repository renames, normalization collisions, and NuGet namespace ownership would make package identity unstable. It is rejected.

**Alternative considered:** keep synthesized template metadata or project paths in onboarding. That makes repository structure and template presentation depend on a separately versioned control-plane file. It is rejected so the release commit atomically owns its content and synthesis descriptor.

### The minimum version is the backfill boundary

The daily pipeline enumerates all published releases rather than tracking only the newest release. `minimumVersion` establishes the first eligible version and makes onboarding backfill explicit and repeatable.

Release eligibility is:

```text
published GitHub release
+ non-draft
+ tag matches v<SemVer 2.0>
+ no SemVer build metadata
+ version >= minimumVersion
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

The packager also adds the workload constraint defined by `template-engine-constraints`, derived from the declared project stacks. The descriptor has no workload field, so requirements always follow the declared projects.

Both modes pass the same TemplateEngine load, dry-run, action, output-path, and package safety validation during release packaging. Onboarding PR validation does not acquire source releases or validate repository-owned template definitions.

**Alternative considered:** inject generated actions into an authored file. This changes source-owned behavior without a source PR and cannot safely reproduce authored conditions or rename behavior. It is rejected.

**Alternative considered:** require the synthesis descriptor alongside authored templates as a topology assertion. That duplicates source-owned topology and creates conflicting authorities. It is rejected.

### License detection is pinned to release content

The allowlist contains SPDX `MIT` and `Apache-2.0`. Detection order is:

```text
reviewed YAML licenseExpression
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
