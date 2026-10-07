## Purpose

Defines the proposed acquisition of approved stack companion templates without adding package mutation to template execution.

## ADDED Requirements

### Requirement: Approved mappings govern companion acquisition

The planner SHALL identify basic project/item companions from a reviewed Functions-owned mapping of approved stack identities to package IDs, version ranges, and source policy. It MUST NOT infer approval from stack aliases, template text, or package-name prefixes. TemplateEngine SHALL remain authoritative for installed package registration.

#### Scenario: Mapping is unavailable or invalid

- **WHEN** an acquisition request cannot validate its companion mapping
- **THEN** the operation fails before deploying the planned packages
- **AND** reports the invalid or missing mapping rather than installing guessed package IDs

### Requirement: Stack acquisition supplies basic templates without curated preinstallation

Setup and direct installation of an approved stack SHALL acquire basic project/item companions. It MUST NOT preinstall curated quickstarts or expose a curated preinstallation option. Non-stack workload installation SHALL NOT acquire template packages. Template execution SHALL NOT acquire companions implicitly.

#### Scenario: Fresh stack installation

- **WHEN** the user explicitly installs an approved stack with default options
- **THEN** the acquisition plan identifies its basic package versions
- **AND** installs them through the existing template lifecycle without separate per-package user commands
- **AND** no curated quickstart payload is acquired

#### Scenario: Non-stack workload installation

- **WHEN** the user installs a worker, host, or other workload without an approved stack acquisition mapping
- **THEN** no companion template operation is performed

### Requirement: Curated discovery and guided use are separate from stack acquisition

Curated availability SHALL come from discovery metadata that can be browsed without payload installation. Browsing/search SHALL remain read-only. Any guided acquisition SHALL require a separate explicitly authorized action under the discovery and template lifecycle contracts. Stack acquisition MUST NOT uninstall already-installed curated packages or restore user-removed packages through unrelated invocations.

#### Scenario: Curated package is already installed

- **WHEN** a stack installation encounters a user-installed curated package
- **THEN** the stack operation does not uninstall or update it
- **AND** it remains visible to the normal installed-template catalog

### Requirement: Existing user package choices are preserved

Acquisition SHALL use the owning lifecycle's version, source, locking, and replacement rules. It MUST NOT silently override a user pin, downgrade a package, or replace its recorded source. Repeating an explicit acquisition request SHALL be able to reuse an installed stack and repair missing companions without unnecessary redeployment.

#### Scenario: Installed package conflicts with the plan

- **WHEN** the installed companion has a user pin or source incompatible with the acquisition plan
- **THEN** the operation reports the conflict and an explicit next action
- **AND** does not silently replace that package

#### Scenario: Stack is installed but basic companion is missing

- **WHEN** the user explicitly retries acquisition
- **THEN** the planner reuses the installed stack where valid
- **AND** attempts the missing companion through its lifecycle

### Requirement: Partial completion remains observable and recoverable

Acquisition SHALL resolve and validate the plan before writes, SHALL honor cancellation between operations, and SHALL rely on per-package rollback. It MUST NOT promise an atomic transaction across workload and template stores. Failure of a later requested package SHALL return non-zero, retain earlier valid installations, and report an idempotent recovery action.

#### Scenario: Basic companion acquisition fails after stack installation

- **WHEN** the stack is installed but a required basic companion fails
- **THEN** completed valid packages remain installed
- **AND** the operation returns non-zero and reports incomplete acquisition and the missing basic package
- **AND** a retry does not redeploy already-satisfied components

#### Scenario: Offline request lacks a required package

- **WHEN** verified installed or local state cannot supply every requested component offline
- **THEN** the operation reports incomplete acquisition rather than claiming success

### Requirement: Existing machines have a qualified acquisition path

The command migration SHALL include an explicit upgrade or repair path for approved basic companions on existing installations. It SHALL preserve unrelated user packages and installed pins/source choices. Legacy template workloads MUST NOT be considered new-engine registrations without migration validation.

#### Scenario: Machine has legacy template workloads

- **WHEN** a user authorizes the approved upgrade/repair acquisition on a machine with legacy template content but no registered companion bundle
- **THEN** migration validates and acquires the required new-engine package through the approved path
- **AND** does not assume equivalent names make the old content usable by the new catalog

#### Scenario: Upgrade acquisition has not been authorized

- **WHEN** an unrelated CLI invocation observes legacy content without an acquisition request
- **THEN** it does not acquire companion packages implicitly
- **AND** qualification of the initializer switch cannot treat that machine as having completed companion acquisition