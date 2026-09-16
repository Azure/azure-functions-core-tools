## Context

See `proposal.md` for motivation and `specs/azure-samples-template-pipeline/spec.md` for the behavior contract.

Azure-Samples repositories release independently through GitHub. The package builder must consume those repositories as untrusted content, produce packages accepted by the `FuncTemplate` contract, and preserve a traceable connection to an immutable Git release without requiring packaging infrastructure in each source repository.

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

Repository, package ID, and onboarding ID are unique case-insensitively. Repository uniqueness intentionally limits one package to one repository in the initial design. A future need for multiple packaging scopes requires an explicit schema revision rather than duplicate entries.

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

The staging feed and NuGet.org provide the durable state machine:

```text
absent from staging, absent from NuGet.org
  -> build and stage

present in staging, absent from NuGet.org
  -> await or retry promotion using staged artifact

present in staging, present in NuGet.org
  -> complete
```

Every query includes package ID and version and verifies repository commit metadata. A package at the expected identity with another commit is a conflict. Immutable package versions are never overwritten.

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

Both locations are fixed conventions; onboarding does not point to arbitrary template configuration or descriptor paths. Supporting lists of paths would imply multiple templates per package and ambiguous content roots, which are outside the initial design.

A root authored configuration is preserved byte-for-byte. The central validator loads and dry-runs it through Microsoft.TemplateEngine and requires:

- valid identity and short-name metadata;
- `tags.type` equal to `project`;
- at least one trusted Functions project configuration finalization action;
- every active configuration action to reference a resolved primary output and supply canonical stack and language;
- no direct `.func/config.json` template content;
- no behavior rejected by the centrally defined safety policy.

The authored file owns template metadata, project topology, parameters, conditions, primary outputs, and post-actions. The synthesis descriptor MUST NOT coexist with it. An invalid authored configuration fails packaging and is never rewritten or replaced with synthesis.

When the authored file is absent, `.github/azure-functions-template.yaml` supplies the complete synthesized template metadata and project topology. For each project, the packager:

1. Requires `<root>/host.json` in the filtered release snapshot.
2. Adds `<root>/host.json` as a primary output.
3. Adds one mandatory trusted configuration finalization action referencing that primary output and carrying the declared canonical stack and language.

The generated template uses the descriptor's identity, short name, name, and description, sets `tags.type` to `project`, and treats the complete filtered snapshot as content. It defines no parameter symbols, replacements, or ordinary post-actions. It emits a singular language tag only when all declared projects have the same language; mixed-language topology is represented exclusively by the configuration actions.

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

An override handles recognized license text that GitHub cannot classify, but a root license file is still required. The package uses a NuGet license expression and retains the source license as template content.

Default-branch license metadata is not authoritative because it may differ from the packaged release.

**Alternative considered:** accept any SPDX expression returned by GitHub. Publication rights and review requirements differ by license; the initial supply chain intentionally admits only the two approved licenses.

### NuGet metadata carries public provenance

The central packager generates package metadata:

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

One failed release does not prevent unrelated releases from staging or promotion. The run ultimately reports failure when unresolved failures remain, but its successful set can pass through the shared approval.

Promotion never rebuilds. Revalidation confirms staged identity, hash, provenance, and package safety before pushing the same bytes to NuGet.org.

**Alternative considered:** require approval for each package. Daily batches would create unnecessary approval load without improving artifact isolation. It is rejected.

**Alternative considered:** automatically promote after staging. A single human gate is required before public publication and is retained.

### Recovery reuses normal pipeline behavior

Transient GitHub and feed calls use bounded exponential backoff. Later daily runs discover incomplete work naturally from feed state. Manual runs can filter by onboarding ID and optional version but use the same validation, staging, approval, and promotion path.

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

1. Create the `func-templates` repository with source, schema, central tooling, tests, and both pipeline definitions.
2. Reserve and configure the `Azure.Functions.Templates.` prefix and package ownership on NuGet.org.
3. Provision the Azure Artifacts staging feed, GitHub read identity, feed publication identities, approval-gated NuGet.org environment, mutation lock, and central notifications.
4. Add representative onboarding entries through PRs and validate package construction without publication.
5. Publish representative packages to staging and verify installation through the existing `FuncTemplate` package path.
6. Enable the shared approval gate and promote the exact staged packages to NuGet.org.
7. Enable the daily schedule after end-to-end publication succeeds.

Rollback disables the scheduled pipeline and promotion environment. Packages already published remain immutable; unlisting and incident response are owned outside this design.
