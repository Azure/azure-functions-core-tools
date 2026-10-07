## Purpose

Defines how Azure-Samples quickstart releases are onboarded, converted into safe `FuncTemplate` NuGet packages, staged, approved, and published to NuGet.org.

The scope extension permits independently selectable templates from one source repository. A reviewed package assignment may select one or several source-owned definitions; per-stack aggregation and private intermediate packages are not required. Source-scope and assignment schema extensions must be explicitly versioned.

## ADDED Requirements

### Requirement: Source scopes preserve independent sample choices

Publication SHALL retain separate selectable identities for independent source samples, including different folders of one repository. Reviewed assignments MAY select one or several definitions from an exact source release. It MUST NOT turn independent choices into one combined solution, require per-stack aggregation for discovery UX, or duplicate a template identity into competing packages implicitly.

#### Scenario: Python connectors source contains two samples

- **WHEN** Office 365 and SharePoint definitions select different folders of the same approved release
- **THEN** published package/template references expose two independent project templates
- **AND** each creates only its intended content scope

### Requirement: Multiple source scopes have explicit versioned ownership

The extended descriptor SHALL use a supported explicit schema version to define named identities, normalized content roots, and exactly one authored or synthesized configuration per definition. Onboarding MAY select definition IDs under its supported extension but MUST NOT supply project paths or topology. Validation SHALL reject traversal, escaping links, excluded content, unsafe roots, and ambiguous ownership. Authored configuration MUST NOT be rewritten to hide scope or grouping conflicts.

#### Scenario: Unsupported descriptor fields use the old version

- **WHEN** a source declares multiple template scopes using a schema version that does not define them
- **THEN** source validation fails rather than reinterpret the original descriptor

#### Scenario: A scope escapes the release snapshot

- **WHEN** a declared template content root or linked content escapes the acquired source boundary
- **THEN** source validation fails before assembly

### Requirement: Package assignments select a complete source definition set

Each assignment SHALL identify an approved source release and a complete set of named source-owned definitions. A missing or invalid selected definition MUST NOT be silently omitted. Changed definitions or source content under an existing package ID/version SHALL be immutable conflicts. Unrelated valid package candidates MAY continue.

#### Scenario: Selected definition is invalid

- **WHEN** one of two selected definitions fails validation
- **THEN** that package candidate fails without publishing a reduced set
- **AND** unrelated valid package candidates may continue

### Requirement: Classification follows Functions project declarations

Non-Functions content MUST NOT fabricate another required Functions stack. Mixed-stack templates SHALL retain every Functions project declaration and constraint. Publishing MUST NOT infer one primary stack from a manifest language or implicitly duplicate a template into stack packages.

#### Scenario: Python sample contains a web frontend

- **WHEN** a sample declares only Python Functions projects and also includes a JavaScript web frontend
- **THEN** the opaque frontend does not by itself make the sample a mixed Functions-stack member

#### Scenario: Template declares Python and Node Functions projects

- **WHEN** validated project declarations require both stacks
- **THEN** package validation retains both requirements
- **AND** does not infer Python-only placement from the manifest language label

### Requirement: Completed packages validate independent definitions

The packager SHALL discover and dry-run every template from the completed package and compare discovery with its reviewed assignment. It SHALL reject duplicate identities, ambiguous short names across distinct groups, missing definitions, invalid actions, and unintended content effects. Independent samples SHALL use distinct groups; shared groups require intentional variants of one named definition. Only a fully validated package SHALL enter staging/promotion.

#### Scenario: Package contains a duplicate identity

- **WHEN** two members advertise the same full identity
- **THEN** package validation fails before staging or promotion

#### Scenario: Independent samples reuse one template group

- **WHEN** two independent definitions advertise the same group identity
- **THEN** validation fails rather than allow precedence to collapse one sample into another's variants
- **AND** authored grouping metadata is not rewritten to hide the conflict

### Requirement: Scope provenance and publication evidence are complete

Package provenance SHALL retain the source release/version/commit and selected definition IDs, normalized scopes, content digests, and relevant license notices. Provenance metadata SHALL remain outside scaffolded roots. The approval record SHALL identify the exact published artifact and definitions so discovery can verify curated status under its own trust policy. Prefixes or self-declared tags MUST NOT count as sanction. Publishing SHALL NOT create the public discovery manifest or implement its trust transport.

#### Scenario: Scope changes under an existing version

- **WHEN** the same package ID/version selects different definitions or source content
- **THEN** publication reports an immutable conflict and does not replace the artifact

#### Scenario: Discovery consumes published packages

- **WHEN** approved packages are publicly available
- **THEN** their publication evidence identifies exact artifacts and source scopes
- **AND** the separate discovery component applies its feed/curation policy and publishes browsing metadata

### Requirement: The func-templates repository owns the packaging control plane

The system SHALL use the `func-templates` repository in the `internal` project of the `azfunc` Azure DevOps organization as the source of truth for quickstart onboarding and release automation. It SHALL keep onboarding YAML under `src/Quickstarts/`, the onboarding JSON Schema at `src/Schema/quickstart.schema.json`, the source synthesis-descriptor JSON Schema at `src/Schema/azure-functions-template.schema.json`, and pipeline definitions under `eng/ci/`.

#### Scenario: Repository layout is validated

- **WHEN** the control-plane repository is validated
- **THEN** onboarding sources are read from `src/Quickstarts/*.yaml`
- **AND** the schemas are read from `src/Schema/quickstart.schema.json` and `src/Schema/azure-functions-template.schema.json`
- **AND** `eng/ci/validate-onboarding.yaml` and `eng/ci/publish-releases.yaml` define the validation and publication pipelines

### Requirement: Onboarding is PR-reviewed YAML

Each file directly under `src/Quickstarts/` with the `.yaml` extension SHALL use a centrally supported schema version and contain a `quickstarts` array. Version 1 retains its original single-package semantics; scoped assignments SHALL require an explicitly supported extension. The scanner SHALL combine matching files without using filenames as identity.

#### Scenario: Multiple onboarding files are combined

- **WHEN** multiple `.yaml` files contain valid quickstart entries
- **THEN** the system validates and processes their entries as one logical manifest

#### Scenario: Empty onboarding file is accepted

- **WHEN** a valid onboarding file contains an empty `quickstarts` array
- **THEN** validation succeeds for that file

#### Scenario: Nested and alternate-extension files are not scanned

- **WHEN** a YAML file is nested below `src/Quickstarts/` or uses an extension other than `.yaml`
- **THEN** the onboarding scanner does not treat it as an onboarding source

### Requirement: The onboarding schema is strict

Every onboarding file SHALL validate against its centrally owned supported schema. Unknown fields and unsupported versions SHALL be rejected. Extended assignments SHALL NOT reinterpret version 1 or move project topology into onboarding.

#### Scenario: Unknown field is rejected

- **WHEN** an onboarding file or entry contains a property not defined by schema version 1
- **THEN** PR validation fails with the file and property identified

#### Scenario: Unsupported schema version is rejected

- **WHEN** an onboarding file declares a version not supported by the central schema
- **THEN** PR validation fails

### Requirement: Each onboarding entry has stable package and release identity

Each entry SHALL require a stable `id`, an `Azure-Samples/<repository>` slug, a `packageId` beginning with `Azure.Functions.Templates.`, and `release.minimumVersion`. `enabled` SHALL default to `true`, and `release.includePrerelease` SHALL default to `false`. Onboarding entries MUST NOT contain template metadata or project topology.

#### Scenario: Minimal entry is accepted

- **WHEN** an entry supplies all required fields and omits optional fields
- **THEN** validation applies the documented defaults

#### Scenario: Repository outside Azure-Samples is rejected

- **WHEN** an entry identifies an owner other than `Azure-Samples`
- **THEN** validation fails

#### Scenario: Package ID has the wrong prefix

- **WHEN** an entry's package ID does not begin with `Azure.Functions.Templates.`
- **THEN** validation fails

#### Scenario: Disabled entry is not scanned

- **WHEN** an entry has `enabled: false`
- **THEN** scheduled release discovery skips that entry without removing previously published packages

#### Scenario: Onboarding contains template metadata

- **WHEN** an onboarding entry contains a `template` property or project path
- **THEN** schema validation fails because the release snapshot owns template definition

### Requirement: Onboarding identities are globally unique

Onboarding and package IDs SHALL be globally unique case-insensitively. Version 1 SHALL also enforce repository uniqueness. A supported scoped extension MAY reference one repository in several assignments only when their named template-definition ownership is disjoint. Moving entries between files MUST NOT change identity.

#### Scenario: Duplicate value exists in another file

- **WHEN** entries collide on ID/package identity, violate original-mode repository uniqueness, or assign one template definition to competing packages
- **THEN** PR validation fails and identifies both entries

### Requirement: PR validation is atomic

`eng/ci/validate-onboarding.yaml` SHALL parse and validate every onboarding file and apply repository-wide onboarding invariants before accepting a PR. A malformed file, invalid entry, or central identity collision SHALL fail the complete validation run. Onboarding validation SHALL NOT require acquiring a source release or validating repository-owned template definitions.

#### Scenario: One file is malformed

- **WHEN** any scanned onboarding file cannot be parsed
- **THEN** PR validation fails without treating the remaining files as an accepted manifest

#### Scenario: All files and global invariants are valid

- **WHEN** every scanned file passes schema and repository-wide onboarding validation
- **THEN** PR validation succeeds

#### Scenario: Source repository has no eligible release

- **WHEN** an enabled entry has no published release satisfying its release policy
- **THEN** onboarding validation can succeed
- **AND** scheduled discovery finds no release candidate for that entry

### Requirement: Scheduled discovery scans GitHub releases

`eng/ci/publish-releases.yaml` SHALL run daily and enumerate all published GitHub releases for every enabled onboarding entry, following pagination. It SHALL ignore drafts and SHALL NOT treat a Git tag without a GitHub release as a release candidate.

#### Scenario: Published release is discovered

- **WHEN** an enabled repository has a published GitHub release
- **THEN** the release is evaluated for packaging eligibility

#### Scenario: Draft release exists

- **WHEN** GitHub returns a draft release
- **THEN** the pipeline does not package it

#### Scenario: Tag has no GitHub release

- **WHEN** a repository has a tag that is not associated with a GitHub release
- **THEN** the pipeline does not package that tag

### Requirement: Eligible releases use unambiguous SemVer tags

An eligible release tag SHALL be `v` followed by a valid SemVer 2.0 version. The package version SHALL be the tag with the leading `v` removed. Build metadata SHALL be rejected. The version SHALL be greater than or equal to `release.minimumVersion`, and a GitHub prerelease SHALL be eligible only when `release.includePrerelease` is `true`.

#### Scenario: Stable release is eligible

- **WHEN** a published release tag is `v1.2.3`
- **AND** version `1.2.3` meets the configured minimum
- **THEN** package version `1.2.3` is eligible

#### Scenario: Version predates onboarding boundary

- **WHEN** a release version is lower than `release.minimumVersion`
- **THEN** the release is skipped

#### Scenario: Prerelease is not enabled

- **WHEN** GitHub marks a release as a prerelease
- **AND** `release.includePrerelease` is false
- **THEN** the release is skipped

#### Scenario: Build metadata is present

- **WHEN** a release tag includes SemVer build metadata
- **THEN** the release is rejected to prevent NuGet identity collisions

### Requirement: Package input is pinned to the release tag commit

The pipeline SHALL resolve the release tag to an exact commit SHA and package that commit. It SHALL record the resolved commit in NuGet repository metadata. An existing package version associated with a different commit SHALL be treated as a conflict and MUST NOT be overwritten or silently accepted.

#### Scenario: Release tag resolves successfully

- **WHEN** an eligible release tag resolves to a commit
- **THEN** the pipeline checks out and packages that exact commit

#### Scenario: Release tag cannot resolve

- **WHEN** an eligible release tag cannot be resolved to a commit
- **THEN** that release fails before staging

#### Scenario: Tag moved after package publication

- **WHEN** the release tag now resolves to a commit different from the commit recorded in an existing package
- **THEN** the pipeline reports a version conflict
- **AND** does not replace the existing package

### Requirement: Source repositories are treated as untrusted content

The packaging process SHALL NOT execute build scripts, package-manager scripts, repository pipeline definitions, or other code from the source repository. It MAY parse the fixed `.github/azure-functions-template.yaml` synthesis descriptor before filtering. It SHALL exclude `.git`, `.github`, generated build output, dependency caches, credentials detected by configured scanning, and links that escape the staged root.

#### Scenario: Repository contains workflow definitions

- **WHEN** a release snapshot contains `.github`
- **THEN** the pipeline consumes any synthesis descriptor before filtering
- **AND** `.github` is absent from the template package

#### Scenario: Repository contains an escaping link

- **WHEN** a symbolic or equivalent link resolves outside the staged source root
- **THEN** package construction fails

#### Scenario: Repository contains executable build instructions

- **WHEN** a release snapshot contains build or package-manager configuration
- **THEN** the files may be copied as template content
- **BUT** the packaging process does not execute them

### Requirement: Authored and synthesized template ownership is exclusive

In the original single-definition mode, the pipeline SHALL accept exactly one template source: root `.template.config/template.json` or `.github/azure-functions-template.yaml`. It SHALL reject both-present and both-absent snapshots in that mode. In the explicitly versioned multi-definition mode, the source descriptor SHALL select independently scoped authored or synthesized definitions, and each definition SHALL have exactly one owner. A descriptor MAY reference authored subfolder configurations but MUST NOT synthesize or override those same definitions. Onboarding MUST NOT select project topology or arbitrary configuration paths.

#### Scenario: Authored configuration and synthesis descriptor are present

- **WHEN** a single-definition release snapshot contains root `.template.config/template.json`
- **AND** the release snapshot contains `.github/azure-functions-template.yaml`
- **THEN** package generation fails with a redundant template ownership diagnostic

#### Scenario: Neither template source is present

- **WHEN** the release snapshot lacks root `.template.config/template.json`
- **AND** the release snapshot lacks `.github/azure-functions-template.yaml`
- **THEN** package generation fails without creating a template configuration

#### Scenario: Exactly one template source is present

- **WHEN** the release snapshot contains exactly one recognized template source
- **THEN** package generation continues with that source

### Requirement: Authored template configuration is preserved and dry-run

For an authored definition, whether selected by the original root convention or the explicitly versioned multi-definition descriptor, the pipeline SHALL preserve the authored file byte-for-byte. It SHALL load and dry-run the template through Microsoft.TemplateEngine and require valid identity and short-name metadata, project template type, workload constraints in the form defined by `template-engine-constraints` that require every stack its configuration actions declare, at least one trusted Functions project configuration finalization action, valid resolved primary-output references, canonical stack and language values, no direct `.func/config.json` content effect, and no behavior rejected by the central safety policy. Invalid authored configuration SHALL fail package construction rather than being rewritten.

#### Scenario: Valid authored template exists

- **WHEN** the root template configuration passes loading, dry-run, configuration-action, and safety validation
- **THEN** the pipeline packages it without modification

#### Scenario: Authored template omits configuration finalization

- **WHEN** the authored template does not declare a valid configuration finalization action for an active Functions project
- **THEN** package construction fails

#### Scenario: Authored template omits workload constraints

- **WHEN** the authored template declares no workload constraints
- **THEN** package construction fails

#### Scenario: Authored workload constraints miss a project stack

- **WHEN** a configuration action declares a stack that the authored workload constraints do not require
- **THEN** package construction fails

#### Scenario: Authored configuration is invalid

- **WHEN** TemplateEngine cannot load or dry-run the authored template configuration
- **THEN** package construction fails without synthesizing a replacement

### Requirement: Synthesized project declarations are safe and complete

The original `.github/azure-functions-template.yaml` single-definition descriptor SHALL use `schemaVersion: 1`, reject unknown properties, and require non-empty `identity`, `shortName`, `name`, `description`, and `projects`. Extended definitions SHALL use an explicitly versioned strict schema rather than accept new fields under version 1. Each synthesized definition SHALL require the same metadata and non-empty project declarations. Each project SHALL require `root`, `stack`, and `language`. `root` SHALL be `.` or a normalized `/`-separated path relative to the validated template content scope. Project roots MUST NOT be absolute, contain `..`, use excluded directories, collide case-insensitively, or overlap as ancestor and descendant roots within the definition. The declared stack and language SHALL be canonical and compatible. Each scope SHALL contain a regular `host.json` directly under every declared root.

#### Scenario: Descriptor metadata is incomplete

- **WHEN** the synthesis descriptor omits required template metadata or contains an unknown property
- **THEN** package generation fails and identifies the descriptor property

#### Scenario: Root project is declared

- **WHEN** a project uses `root: .`
- **THEN** the pipeline resolves its primary-output anchor as root `host.json`

#### Scenario: Nested project is declared

- **WHEN** a project uses `root: src/api`
- **THEN** the pipeline resolves its primary-output anchor as `src/api/host.json`

#### Scenario: Project roots overlap

- **WHEN** one declared project root is an ancestor of another declared project root
- **THEN** validation fails before template synthesis

#### Scenario: Project host file is missing

- **WHEN** the filtered release snapshot has no regular `host.json` directly under a declared project root
- **THEN** package generation fails and identifies the project

#### Scenario: Stack and language are incompatible

- **WHEN** a declared canonical stack does not recognize the declared canonical language
- **THEN** validation fails before template synthesis

### Requirement: Source descriptor is synthesized into template configuration

For each synthesized definition, the pipeline SHALL generate template configuration using that definition's identity, short name, name, description, and projects. The template SHALL have project type and SHALL use only its validated filtered content scope. The original single-root mode uses the complete filtered snapshot; an extended scoped definition MUST NOT copy unrelated sample folders. For each declared project, it SHALL add `<root>/host.json` as a primary output and add one mandatory trusted Functions project configuration finalization action referencing that output and carrying canonical stack and language. It SHALL define no parameter symbols, replacements, or ordinary post-actions. It SHALL add workload constraints derived from every declared project stack.

#### Scenario: Repository has no template configuration

- **WHEN** the original single-definition snapshot lacks root `.template.config/template.json`
- **AND** it contains a valid single-definition synthesis descriptor
- **THEN** the pipeline adds a minimal synthesized project template to staging

#### Scenario: Extended descriptor selects authored subfolder definitions

- **WHEN** a versioned descriptor selects authored configurations from subfolders
- **THEN** those definitions remain authored and pass authored validation
- **AND** absence of a root configuration does not trigger synthesis for them

#### Scenario: Synthesized template is validated

- **WHEN** template configuration has been synthesized
- **THEN** the same TemplateEngine load, dry-run, action, output-path, and safety validation used for authored configuration succeeds before packing

#### Scenario: Synthesized projects share one language

- **WHEN** every declared project uses the same canonical language
- **THEN** the synthesized template may expose that singular TemplateEngine language tag

#### Scenario: Synthesized projects use mixed languages

- **WHEN** declared projects use different canonical languages
- **THEN** the synthesized template omits a singular language tag
- **AND** preserves per-project languages in configuration actions

#### Scenario: Synthesized projects use mixed stacks

- **WHEN** declared projects use different canonical stacks
- **THEN** the synthesized workload constraint requires the workloads for every declared stack

### Requirement: Package licensing is release-specific and allowlisted

For each package, licensing SHALL come from the exact source release through reviewed override, release-specific repository detection, or source-root license inspection. Only SPDX `MIT` or `Apache-2.0` SHALL be accepted, and an override SHALL NOT remove the file requirement. Selected subfolder scopes SHALL retain relevant source-root notices. Cross-repository aggregate license policy is outside this scope.

#### Scenario: GitHub detects an allowed license

- **WHEN** GitHub identifies the release commit license as MIT or Apache-2.0
- **THEN** the detected SPDX expression is used in package metadata

#### Scenario: Detection fails but override is valid

- **WHEN** automatic detection does not identify an allowed license
- **AND** YAML supplies an allowed expression
- **AND** the release snapshot contains a root license file
- **THEN** the reviewed override is used

#### Scenario: License is unsupported

- **WHEN** the source release license is not MIT or Apache-2.0
- **THEN** package construction fails

### Requirement: NuGet packages have func template identity and provenance

Each package SHALL use the reviewed assignment's `packageId`, not its internal operational `id`, along with the source-release-derived version, `FuncTemplate` type, effective description/license, canonical source repository/commit, and release URL. It SHALL include selected-definition provenance and notices and exclude private pipeline identifiers.

#### Scenario: Package metadata is inspected

- **WHEN** a published package is opened
- **THEN** its ID, version, package type, description, license, project URL, repository URL, commit, and release-notes link match the resolved release

#### Scenario: Pipeline implementation metadata is inspected

- **WHEN** a generated package is opened
- **THEN** it does not expose Azure Pipeline definition or run identifiers as package provenance

### Requirement: Packages are validated before staging

Before publication, the pipeline SHALL inspect the completed `.nupkg`, load and dry-run its templates through Microsoft.TemplateEngine, validate every required Functions project configuration finalization action and resolved primary output, and verify the expected identity, version, package type, metadata, content, and exclusions. A package that fails any check MUST NOT enter the staging feed.

#### Scenario: Completed package is valid

- **WHEN** package inspection and TemplateEngine loading succeed
- **THEN** the package is eligible for staging

#### Scenario: Excluded content is present

- **WHEN** final package inspection finds excluded content
- **THEN** publication fails before staging

### Requirement: Azure Artifacts staging is the publication checkpoint

Every validated template package SHALL first enter Azure Artifacts staging. Staging and NuGet.org SHALL be queried by package ID/version and selected-definition provenance to determine publication state. Run-retained artifacts SHALL NOT be the durable checkpoint.

#### Scenario: Version is absent from both feeds

- **WHEN** a validated package version exists in neither publication feed
- **THEN** the pipeline builds, validates, and publishes it to staging

#### Scenario: Version exists only in staging

- **WHEN** the expected package version exists in staging but not NuGet.org
- **THEN** the pipeline reuses the staged package for promotion without rebuilding it

#### Scenario: Version exists in both feeds

- **WHEN** the expected package version exists in both feeds with matching provenance
- **THEN** the release is complete and skipped

### Requirement: One approval promotes the successful run set

The pipeline SHALL request one approval for successfully staged packages and their complete selected definitions. Approval SHALL promote the exact staged bytes without rebuilding. An invalid selected definition blocks its package; unrelated fully validated packages MAY be approved and promoted.

#### Scenario: Run has multiple successful packages

- **WHEN** several fully validated packages reach staging
- **THEN** one approval authorizes their exact complete artifacts

#### Scenario: Run has partial failure

- **WHEN** some package candidates fail and other complete packages reach staging
- **THEN** approval includes only those fully validated packages
- **AND** failures are reported separately

#### Scenario: One selected scope fails validation

- **WHEN** a package has one valid definition and another invalid selected definition
- **THEN** that package is not staged or approved with only the valid definition

#### Scenario: Approval is withheld

- **WHEN** the run is not approved
- **THEN** staged packages remain available for a later promotion attempt
- **AND** nothing from that run is published to NuGet.org

### Requirement: Publication is retryable and concurrency-safe

The pipeline SHALL retry transient GitHub, network, and feed failures with bounded backoff. Subsequent daily or manual runs SHALL resume from feed state. Only one publication run SHALL mutate the staging and production feeds at a time, and no operation SHALL overwrite an existing package version.

#### Scenario: Transient operation succeeds on retry

- **WHEN** a transient external operation fails and then succeeds within the retry policy
- **THEN** processing continues without producing a duplicate package

#### Scenario: Previous promotion was interrupted

- **WHEN** a validated package is present in staging but absent from NuGet.org
- **THEN** a later run can promote the staged artifact

#### Scenario: Publication run overlaps

- **WHEN** another publication run already owns the feed mutation lock
- **THEN** the new run waits or exits without concurrently publishing

### Requirement: Operators can target manual recovery

The pipeline SHALL support manual recovery by onboarding/package ID and optional source-release version, preserving the reviewed definition selection. It SHALL reuse normal validation, staging, approval, and immutable promotion without force overwrite.

#### Scenario: Operator targets one entry

- **WHEN** an operator starts a recovery run for one onboarding/package ID
- **THEN** unrelated entries are not processed
- **AND** promotion still requires the normal approval gate

#### Scenario: Operator requests an existing conflicting version

- **WHEN** a manual run targets a version whose feed provenance conflicts with the release commit
- **THEN** the run reports the conflict rather than overwriting it

### Requirement: Runs report partial and complete outcomes

Each run SHALL produce a final summary of discovered, staged, promoted, already-complete, skipped, and failed releases. The pipeline SHALL continue processing independent releases after an item failure, SHALL fail its final status when any item remains failed, and SHALL notify the central `func-templates` operations team after configured retries are exhausted. Logs SHALL redact credentials and feed tokens.

#### Scenario: One release fails

- **WHEN** one release fails while another succeeds
- **THEN** the successful release continues through staging and approval
- **AND** the final run status reports the failure

#### Scenario: Credentials are used

- **WHEN** the pipeline authenticates to GitHub or a package feed
- **THEN** credentials and tokens are not emitted in logs or summaries
