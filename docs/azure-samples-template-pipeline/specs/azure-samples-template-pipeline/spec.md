## Purpose

Defines how Azure-Samples quickstart releases are onboarded, converted into safe `FuncTemplate` NuGet packages, staged, approved, and published to NuGet.org.

The proposed curated bundle mode retains single-release validation as a private source-unit boundary. Public package promotion applies to assembled bundles; source units used by bundles are not independently promoted. Single-source tag/version metadata describes the input, while an aggregate's public version and provenance follow the additional bundle requirements below. Versioned source-scope schema changes must be explicit rather than silently accepted under the original schema.

## ADDED Requirements

### Requirement: Curated bundles contain independently selectable templates

Curated publication SHALL assemble a stable per-stack `FuncTemplate` bundle from approved source definitions. Basic project/item packages SHALL remain separate. A bundle SHALL contain separate selectable template identities for independent samples, including different subfolders of one repository. It MUST NOT turn those alternatives into one template generating all samples.

#### Scenario: Python connectors source contains two samples

- **WHEN** Office 365 and SharePoint definitions select different folders of the same approved release
- **THEN** the aggregate exposes two independently selectable project templates
- **AND** each creates only its intended content scope

### Requirement: Multiple source scopes have explicit versioned ownership

The extended source descriptor SHALL use an explicitly versioned schema to define template identities, normalized content roots, and exactly one authored or synthesized configuration per definition. Central onboarding MUST NOT supply project topology. Scope validation SHALL reject traversal, escaping links, excluded content, unsafe project roots, and ambiguous ownership. Authored configuration MUST NOT be patched by aggregation.

#### Scenario: Unsupported descriptor fields use the old version

- **WHEN** a source declares multiple template scopes using a schema version that does not define them
- **THEN** source validation fails rather than reinterpret the original descriptor

#### Scenario: A scope escapes the release snapshot

- **WHEN** a declared template content root or linked content escapes the acquired source boundary
- **THEN** source validation fails before assembly

### Requirement: Reviewed recipes pin aggregate membership

A bundle recipe SHALL declare a stable public package ID, canonical stack, independent bundle version, and exact approved source releases and template identities. Changed membership or source revisions SHALL require a new bundle version. An unavailable or invalid required member MUST NOT be silently omitted. Unrelated bundle candidates MAY continue independently.

#### Scenario: New quickstart is added to Python

- **WHEN** a reviewed recipe adds a validated Python quickstart
- **THEN** publication produces a new version of the existing Python curated package
- **AND** the addition does not require a new public package ID for that quickstart

#### Scenario: Required source release cannot be acquired

- **WHEN** a pinned required member cannot be acquired or validated
- **THEN** that bundle candidate fails without publishing a reduced member set
- **AND** unrelated valid bundle candidates can continue

### Requirement: Bundle classification follows Functions project topology

A homogeneous bundle member SHALL declare Functions projects matching the bundle's canonical stack. Non-Functions content MUST NOT fabricate another required Functions stack. Mixed-stack templates SHALL retain all project declarations and constraints and MUST NOT be mislabeled or duplicated into several bundles as an implicit placement rule. Their distribution SHALL require an explicitly reviewed policy.

#### Scenario: Python sample contains a web frontend

- **WHEN** a sample declares only Python Functions projects and also includes a JavaScript web frontend
- **THEN** the opaque frontend does not by itself make the sample a mixed Functions-stack member

#### Scenario: Template declares Python and Node Functions projects

- **WHEN** validated project declarations require both stacks
- **THEN** bundle validation retains both requirements and applies the reviewed mixed-stack placement policy
- **AND** does not infer Python-only placement from the manifest language label

### Requirement: Completed aggregate validates every member

The packager SHALL discover and dry-run every template from the completed bundle, compare identities with the recipe, and reject duplicate identities, ambiguous short names across distinct groups, missing members, invalid configuration actions, and unintended content effects. Independent samples SHALL use distinct groups. Shared group identity SHALL be accepted only for variants explicitly declared as one recipe member, and validation SHALL prove every independent member remains individually selectable. Only a fully validated aggregate SHALL enter public promotion. Its private source units MUST NOT be promoted as competing public packages.

#### Scenario: Aggregate contains a duplicate template identity

- **WHEN** two members advertise the same full identity
- **THEN** aggregate validation fails before public staging or promotion

#### Scenario: Independent samples reuse one template group

- **WHEN** two independent recipe members advertise the same group identity
- **THEN** aggregate validation fails rather than allow precedence to collapse one sample into another's variants
- **AND** authored grouping metadata is not rewritten to hide the conflict

### Requirement: Aggregate provenance is immutable and complete

The public bundle version SHALL come from the approved recipe, not one member's tag. Its metadata SHALL identify the recipe revision and every template's source repository, release, resolved commit, content scope, license, and content digest. All member notices SHALL be retained and the combined package license SHALL be accurate. Metadata manifests SHALL remain outside scaffolded template roots. Feed checks SHALL reject changed provenance under an existing bundle ID/version, and promotion SHALL reuse the exact staged bytes.

#### Scenario: Bundle version is reused with a changed source

- **WHEN** the same public ID/version resolves to different membership or source commits
- **THEN** publication reports an immutable version conflict
- **AND** does not replace the existing artifact

#### Scenario: Bundle combines approved source licenses

- **WHEN** members have different permitted source licenses
- **THEN** the package retains their individual notices and uses the reviewed combined expression
- **AND** does not assign one member's license to all content

### Requirement: The func-templates repository owns the packaging control plane

The system SHALL use the `func-templates` repository in the `internal` project of the `azfunc` Azure DevOps organization as the source of truth for quickstart onboarding and release automation. It SHALL keep onboarding YAML under `src/Quickstarts/`, the onboarding JSON Schema at `src/Schema/quickstart.schema.json`, the source synthesis-descriptor JSON Schema at `src/Schema/azure-functions-template.schema.json`, and pipeline definitions under `eng/ci/`.

#### Scenario: Repository layout is validated

- **WHEN** the control-plane repository is validated
- **THEN** onboarding sources are read from `src/Quickstarts/*.yaml`
- **AND** the schemas are read from `src/Schema/quickstart.schema.json` and `src/Schema/azure-functions-template.schema.json`
- **AND** `eng/ci/validate-onboarding.yaml` and `eng/ci/publish-releases.yaml` define the validation and publication pipelines

### Requirement: Onboarding is PR-reviewed YAML

Each file directly under `src/Quickstarts/` with the `.yaml` extension SHALL contain `schemaVersion: 1` and a `quickstarts` array containing zero or more onboarding entries. The scanner SHALL combine every matching file into one logical onboarding manifest. Filenames and the file containing an entry MUST NOT contribute to entry identity.

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

Every onboarding file SHALL validate against `src/Schema/quickstart.schema.json`. The schema SHALL reject unknown properties and SHALL define required values, types, formats, allowed values, and defaults for schema version 1.

#### Scenario: Unknown field is rejected

- **WHEN** an onboarding file or entry contains a property not defined by schema version 1
- **THEN** PR validation fails with the file and property identified

#### Scenario: Unsupported schema version is rejected

- **WHEN** an onboarding file declares a schema version other than `1`
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

The system SHALL enforce case-insensitive uniqueness across all onboarding files for onboarding ID, repository slug, and package ID. Moving an unchanged entry between files MUST NOT change its effective values.

#### Scenario: Duplicate value exists in another file

- **WHEN** two entries resolve to the same globally unique value ignoring case
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

For each source unit, the pipeline SHALL determine licensing from the exact release commit. It SHALL first use an explicit reviewed `licenseExpression` when present, otherwise use the GitHub repository-license API at the release commit, and otherwise inspect the source root license file. Only source expressions `MIT` and `Apache-2.0` SHALL be accepted, and an override SHALL NOT remove the file requirement. Aggregate metadata SHALL instead use the reviewed accurate combined expression for its allowed member licenses and preserve all notices.

#### Scenario: GitHub detects an allowed license

- **WHEN** GitHub identifies the release commit license as MIT or Apache-2.0
- **THEN** the detected SPDX expression is used in package metadata

#### Scenario: Detection fails but override is valid

- **WHEN** automatic detection does not identify an allowed license
- **AND** YAML supplies an allowed expression
- **AND** the release snapshot contains a root license file
- **THEN** the reviewed override is used

#### Scenario: License is unsupported

- **WHEN** a source unit's effective license is not MIT or Apache-2.0
- **THEN** package construction fails

### Requirement: NuGet packages have func template identity and provenance

Each private source-unit package SHALL use its explicit onboarding package ID, release-derived version, `FuncTemplate` package type, effective source description/license, canonical source repository URL and resolved commit, and release URL. Its license SHALL remain with the relevant content. Each public aggregate SHALL instead use its approved recipe package ID and independent bundle version, with recipe repository metadata and complete per-template provenance and notices as required above. Both kinds SHALL exclude private pipeline identifiers.

#### Scenario: Package metadata is inspected

- **WHEN** a private source-unit package is opened
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

Every validated private source unit SHALL be checkpointed in its internal staging feed and SHALL be complete there when provenance matches. Every validated public aggregate SHALL first be published to staging, and its staging feed and NuGet.org SHALL be queried by bundle ID/version and full provenance to determine public publication state. Private source units MUST NOT be queued for promotion because they are absent from NuGet.org. Pipeline-run artifacts SHALL NOT be the durable checkpoint for either kind.

#### Scenario: Version is absent from both feeds

- **WHEN** an eligible public aggregate version exists in neither public publication feed
- **THEN** the pipeline builds, validates, and publishes it to staging

#### Scenario: Version exists only in staging

- **WHEN** the expected public aggregate version exists in staging but not NuGet.org
- **THEN** the pipeline reuses the staged package for promotion without rebuilding it

#### Scenario: Version exists in both feeds

- **WHEN** the expected public aggregate version exists in both feeds with matching provenance
- **THEN** the release is complete and skipped

### Requirement: One approval promotes the successful run set

After public aggregate candidates have been processed through staging, the pipeline SHALL request one approval for the successfully staged public bundles. Private source units SHALL be excluded from this approval set. Approval SHALL promote the exact staged bundle files without rebuilding them. Failed required source inputs SHALL block their dependent bundle, while unrelated completed bundles MAY still be approved and promoted.

#### Scenario: Run has multiple successful packages

- **WHEN** several completed public aggregates reach staging in one run
- **THEN** one approval authorizes promotion of those public aggregates only
- **AND** private source inputs are not included

#### Scenario: Run has partial failure

- **WHEN** some bundle candidates fail and other public aggregates reach staging
- **THEN** the approval includes only the successfully staged public aggregates
- **AND** failures are reported separately

#### Scenario: Private inputs stage but their dependent bundle fails

- **WHEN** source units reach private staging but their aggregate fails validation
- **THEN** none of those units enter a public approval set
- **AND** public recovery waits for a completed validated aggregate

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

- **WHEN** a public aggregate is present in staging but absent from NuGet.org
- **THEN** a later run can promote the staged artifact

#### Scenario: Publication run overlaps

- **WHEN** another publication run already owns the feed mutation lock
- **THEN** the new run waits or exits without concurrently publishing

### Requirement: Operators can target manual recovery

The pipeline SHALL support explicit manual recovery targets for source onboarding ID/release version and for public bundle ID/recipe version. Source recovery SHALL stop at private staging; bundle recovery SHALL use normal assembly, validation, staging, approval, and immutable promotion. Neither SHALL provide a force-overwrite mode.

#### Scenario: Operator targets one entry

- **WHEN** an operator starts a source recovery run for one onboarding ID
- **THEN** unrelated entries are not processed
- **AND** source staging does not authorize public bundle promotion

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
