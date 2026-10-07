## Purpose

Defines the proposed acquisition of approved stack companion templates without adding package mutation to template execution.

## ADDED Requirements

### Requirement: Approved mappings govern companion acquisition

The planner SHALL identify basic and curated companions from a reviewed Functions-owned mapping of approved stack identities to package IDs, version ranges, and source policy. It MUST NOT infer approval from stack aliases, template text, or package-name prefixes. TemplateEngine SHALL remain authoritative for installed template package registration.

#### Scenario: Mapping is unavailable or invalid

- **WHEN** an acquisition request cannot validate its companion mapping
- **THEN** the operation fails before deploying the planned packages
- **AND** reports the invalid or missing mapping rather than installing guessed package IDs

### Requirement: Stack acquisition includes separate companion roles

Setup and direct installation of an approved stack SHALL include basic companion acquisition and SHALL include curated quickstart acquisition by default. Non-stack workload installation SHALL NOT acquire template packages. Template execution SHALL NOT acquire companions implicitly.

#### Scenario: Fresh stack installation

- **WHEN** the user explicitly installs an approved stack with default options
- **THEN** the acquisition plan identifies its basic and curated package versions
- **AND** installs them through the existing template lifecycle without separate per-package user commands

#### Scenario: Non-stack workload installation

- **WHEN** the user installs a worker, host, or other workload without an approved stack acquisition mapping
- **THEN** no companion template operation is performed

### Requirement: Curated acquisition has an explicit opt-out

Stack acquisition SHALL provide a curated-only opt-out. It MUST NOT remove basic templates or packages already installed. An unrelated CLI invocation MUST NOT reset an opt-out or restore a package explicitly removed by the user.

#### Scenario: Curated acquisition is disabled

- **WHEN** the user opts out while acquiring a stack
- **THEN** basic companion acquisition remains in the plan
- **AND** curated packages are not acquired or uninstalled

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

#### Scenario: Curated acquisition fails after basic setup succeeds

- **WHEN** the stack and basic companions are installed but curated acquisition fails
- **THEN** those completed packages remain installed
- **AND** the operation reports incomplete acquisition and the missing curated package
- **AND** a retry does not redeploy already-satisfied components

#### Scenario: Offline request lacks a required package

- **WHEN** verified installed or local state cannot supply every requested component offline
- **THEN** the operation reports incomplete acquisition rather than claiming success

### Requirement: Existing machines have a qualified acquisition path

The command migration SHALL include an explicit upgrade or repair path for approved companions on existing installations. It SHALL preserve prior opt-outs and unrelated user packages. Legacy template workloads MUST NOT be considered new-engine registrations without migration validation.

#### Scenario: Machine has legacy template workloads

- **WHEN** a user authorizes the approved upgrade/repair acquisition on a machine with legacy template content but no registered companion bundle
- **THEN** migration validates and acquires the required new-engine package through the approved path
- **AND** does not assume equivalent names make the old content usable by the new catalog

#### Scenario: Upgrade acquisition has not been authorized

- **WHEN** an unrelated CLI invocation observes legacy content without an acquisition request
- **THEN** it does not acquire companion packages implicitly
- **AND** qualification of the initializer switch cannot treat that machine as having completed companion acquisition