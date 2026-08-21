## Purpose

Defines how Azure-Samples quickstart releases are onboarded, converted into safe `FuncTemplate` NuGet packages, staged, approved, and published to NuGet.org.

## ADDED Requirements

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

For each release snapshot, the pipeline SHALL accept exactly one template source: root `.template.config/template.json` or `.github/azure-functions-template.yaml`. The pipeline SHALL fail package generation when both sources are present or both sources are absent. Onboarding MUST NOT select the mode or point to another path.

#### Scenario: Authored configuration and synthesis descriptor are present

- **WHEN** the release snapshot contains root `.template.config/template.json`
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

When the release snapshot contains root `.template.config/template.json` and no synthesis descriptor, the pipeline SHALL preserve the authored file byte-for-byte. It SHALL load and dry-run the template through Microsoft.TemplateEngine and require valid identity and short-name metadata, project template type, at least one trusted Functions project configuration finalization action, valid resolved primary-output references, canonical stack and language values, no direct `.func/config.json` content effect, and no behavior rejected by the central safety policy. Invalid authored configuration SHALL fail package construction rather than being rewritten.

#### Scenario: Valid authored template exists

- **WHEN** the root template configuration passes loading, dry-run, configuration-action, and safety validation
- **THEN** the pipeline packages it without modification

#### Scenario: Authored template omits configuration finalization

- **WHEN** the authored template does not declare a valid configuration finalization action for an active Functions project
- **THEN** package construction fails

#### Scenario: Authored configuration is invalid

- **WHEN** TemplateEngine cannot load or dry-run the authored template configuration
- **THEN** package construction fails without synthesizing a replacement

### Requirement: Synthesized project declarations are safe and complete

The `.github/azure-functions-template.yaml` descriptor SHALL use `schemaVersion: 1`, reject unknown properties, and require non-empty `identity`, `shortName`, `name`, `description`, and `projects`. Each project SHALL require `root`, `stack`, and `language`. `root` SHALL be `.` or a normalized `/`-separated repository-relative path. Project roots MUST NOT be absolute, contain `..`, use excluded directories, collide case-insensitively, or overlap as ancestor and descendant roots. The declared stack and language SHALL be canonical and compatible. The filtered release snapshot SHALL contain a regular `host.json` directly under each declared root.

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

When the release snapshot contains `.github/azure-functions-template.yaml` and does not contain root `.template.config/template.json`, the pipeline SHALL synthesize template configuration in staging using the descriptor's identity, short name, name, description, and projects. The synthesized template SHALL have project type and SHALL treat the complete filtered release snapshot as template content. For each declared project, it SHALL add `<root>/host.json` as a primary output and add one mandatory trusted Functions project configuration finalization action referencing that output and supplying the declared canonical stack and language. It SHALL define no parameter symbols, content replacements, or ordinary post-actions.

#### Scenario: Repository has no template configuration

- **WHEN** the release snapshot lacks root `.template.config/template.json`
- **AND** the release snapshot contains a valid `.github/azure-functions-template.yaml`
- **THEN** the pipeline adds a minimal synthesized project template to staging

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

### Requirement: Package licensing is release-specific and allowlisted

The pipeline SHALL determine licensing from the exact release commit. It SHALL first use an explicit reviewed `licenseExpression` when present, otherwise use the GitHub repository-license API at the release commit, and otherwise inspect a root license file from the staged snapshot. Only SPDX expressions `MIT` and `Apache-2.0` SHALL be accepted. An explicit override SHALL NOT remove the requirement for a root license file.

#### Scenario: GitHub detects an allowed license

- **WHEN** GitHub identifies the release commit license as MIT or Apache-2.0
- **THEN** the detected SPDX expression is used in package metadata

#### Scenario: Detection fails but override is valid

- **WHEN** automatic detection does not identify an allowed license
- **AND** YAML supplies an allowed expression
- **AND** the release snapshot contains a root license file
- **THEN** the reviewed override is used

#### Scenario: License is unsupported

- **WHEN** the effective license is not MIT or Apache-2.0
- **THEN** package construction fails

### Requirement: NuGet packages have func template identity and provenance

Each generated package SHALL use the explicit package ID, the release-derived package version, and package type `FuncTemplate`. NuGet metadata SHALL include the effective description and license, a project URL for the GitHub repository, repository type `git`, the canonical repository URL, the resolved commit SHA, and a release-notes link to the corresponding GitHub release. The root license file SHALL remain in template content.

#### Scenario: Package metadata is inspected

- **WHEN** a generated package is opened
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

Every validated package SHALL first be published to the Azure Artifacts staging feed. The staging feed and NuGet.org SHALL be queried by package ID and version to determine publication state. Pipeline-run artifacts SHALL NOT be the durable publication record.

#### Scenario: Version is absent from both feeds

- **WHEN** an eligible package version exists in neither feed
- **THEN** the pipeline builds, validates, and publishes it to staging

#### Scenario: Version exists only in staging

- **WHEN** the expected package version exists in staging but not NuGet.org
- **THEN** the pipeline reuses the staged package for promotion without rebuilding it

#### Scenario: Version exists in both feeds

- **WHEN** the expected package version exists in both feeds with matching provenance
- **THEN** the release is complete and skipped

### Requirement: One approval promotes the successful run set

After all release candidates in a scheduled or manual run have been processed through staging, the pipeline SHALL present one summary and request one approval for all successfully staged packages in that run. Approval SHALL promote the exact staged `.nupkg` files to NuGet.org without rebuilding them. Releases that failed before staging SHALL be reported separately and SHALL NOT block approval or promotion of successful packages.

#### Scenario: Run has multiple successful packages

- **WHEN** several packages reach staging in one run
- **THEN** one approval authorizes promotion of the complete successful set

#### Scenario: Run has partial failure

- **WHEN** some releases fail and others reach staging
- **THEN** the approval includes the successfully staged packages
- **AND** failures are reported separately

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

- **WHEN** a package is present in staging but absent from NuGet.org
- **THEN** a later run can promote the staged artifact

#### Scenario: Publication run overlaps

- **WHEN** another publication run already owns the feed mutation lock
- **THEN** the new run waits or exits without concurrently publishing

### Requirement: Operators can target manual recovery

The publication pipeline SHALL support a manual run filtered by onboarding ID and optional package version. Manual execution SHALL use the same discovery, validation, staging, approval, and immutable publication behavior as scheduled execution and SHALL NOT provide a force-overwrite mode.

#### Scenario: Operator targets one entry

- **WHEN** an operator starts a manual run for one onboarding ID
- **THEN** unrelated entries are not processed

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
