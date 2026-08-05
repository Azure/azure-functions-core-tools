## Purpose

Defines how users install, update, and uninstall template packages through `func new`, including package ownership, version changes, source conflicts, and safe replacement behavior.

## ADDED Requirements

### Requirement: func new owns template package installation

The CLI SHALL expose `func new install <package>` as the canonical command for installing a template package. The CLI SHALL also accept `func new --install <package>` as an equivalent convenience form with the same validation, installation behavior, output, and exit code.

#### Scenario: Install through the canonical subcommand

- **WHEN** a user runs `func new install <package>`
- **THEN** the CLI installs the requested template package into the func template store

#### Scenario: Install through the option form

- **WHEN** a user runs `func new --install <package>`
- **THEN** the CLI performs the same operation as `func new install <package>`

#### Scenario: Force is accepted by both forms

- **WHEN** a user appends `--force` to either template install form
- **THEN** the CLI applies the same forced-replacement policy for both forms

#### Scenario: Source is accepted by both forms

- **WHEN** a user appends `--source <feed>` to either template install form
- **THEN** the CLI uses the specified feed for NuGet package resolution

### Requirement: func new owns template package uninstallation

The CLI SHALL expose `func new uninstall <package>` as the canonical command for uninstalling a template package. The CLI SHALL also accept `func new --uninstall <package>` as an equivalent convenience form with the same validation, uninstall behavior, output, and exit code.

#### Scenario: Uninstall through the canonical subcommand

- **WHEN** a user runs `func new uninstall <package>` for an installed template package
- **THEN** the CLI uninstalls the requested package from the func template store
- **AND** templates provided by that package are no longer available through `func new`

#### Scenario: Uninstall through the option form

- **WHEN** a user runs `func new --uninstall <package>`
- **THEN** the CLI performs the same operation as `func new uninstall <package>`

### Requirement: TemplateEngine owns uninstall request semantics

The CLI SHALL use Microsoft.TemplateEngine's installed-package identity and package manager semantics to resolve and uninstall template packages. Uninstallation through `func new` MUST NOT remove or modify packages in the func workload store.

#### Scenario: Installed template package is resolved by TemplateEngine

- **WHEN** a user supplies an installed template package identity supported by Microsoft.TemplateEngine
- **THEN** the CLI delegates removal of that package to Microsoft.TemplateEngine

#### Scenario: Workload packages are unaffected

- **WHEN** a user invokes either template uninstall form
- **THEN** packages managed by `func workload` remain unchanged

### Requirement: func new owns template package updates

The CLI SHALL expose `func new update <package>` as the canonical command for updating one installed template package. The CLI SHALL also accept `func new --update <package>` as an equivalent convenience form with the same validation, update behavior, output, and exit code.

#### Scenario: Update through the canonical subcommand

- **WHEN** a user runs `func new update <package>`
- **THEN** the CLI checks the specified installed template package for a newer version and applies an available update

#### Scenario: Update through the option form

- **WHEN** a user runs `func new --update <package>`
- **THEN** the CLI performs the same operation as `func new update <package>`

#### Scenario: Update targets one package

- **WHEN** a user updates one installed template package
- **THEN** other installed template packages remain unchanged

#### Scenario: Source is accepted by both update forms

- **WHEN** a user appends `--source <feed>` to either template update form
- **THEN** the CLI uses the specified feed to resolve an update for that package

#### Scenario: No updates are available

- **WHEN** a user runs either template update form
- **AND** the specified template package has no newer available version
- **THEN** the command succeeds without changing the installed template package

### Requirement: TemplateEngine owns update semantics

The CLI SHALL use Microsoft.TemplateEngine's installed-package identity, source identity, and package manager semantics to discover and apply an update for the specified template package. Updating through `func new` MUST NOT remove, modify, or update packages in the func workload store.

#### Scenario: Template package update is available

- **WHEN** Microsoft.TemplateEngine resolves a newer version for an installed template package
- **THEN** the CLI delegates the update to Microsoft.TemplateEngine
- **AND** templates from the newer package version replace those from the previous version

#### Scenario: Workload packages are unaffected by update

- **WHEN** a user invokes either template update form
- **THEN** packages managed by `func workload` remain unchanged

### Requirement: TemplateEngine owns install request semantics

The CLI SHALL accept the package-expression forms supported by the configured Microsoft.TemplateEngine installer and SHALL preserve their resolution semantics. Template package acquisition, installed-package identity, and installer/source identity SHALL be determined by Microsoft.TemplateEngine rather than redefined by the CLI.

#### Scenario: Supported package expression is passed to TemplateEngine

- **WHEN** a user supplies a package expression supported by the configured TemplateEngine installer
- **THEN** the CLI resolves and acquires the package according to that installer's semantics

#### Scenario: Unsupported package expression fails

- **WHEN** a user supplies a package expression that the configured TemplateEngine installer does not support
- **THEN** the CLI exits non-zero and reports that the package request could not be resolved

### Requirement: Initial install sources are folder and NuGet feed

The CLI SHALL initially support template package installation from a folder and from a NuGet feed using the corresponding Microsoft.TemplateEngine installers. Other install sources are outside the initial template-install capability.

#### Scenario: Install from a folder

- **WHEN** a user supplies a folder package expression supported by the TemplateEngine folder installer
- **THEN** the CLI installs the template package from that folder

#### Scenario: Install from a NuGet feed

- **WHEN** a user supplies a NuGet package expression supported by the TemplateEngine NuGet installer
- **THEN** the CLI resolves and installs the template package from the configured NuGet feed

#### Scenario: Unsupported source type

- **WHEN** a user supplies a package expression for a source other than folder or NuGet feed
- **THEN** the CLI exits non-zero and reports that the install source is unsupported

### Requirement: NuGet template operations share workload feed configuration

The CLI SHALL accept `--source <feed>` on template install and update commands as an explicit NuGet feed override. NuGet feed selection for both operations SHALL use `--source` when supplied, then the `FUNC_CLI_WORKLOADS_SOURCE` environment variable when configured, matching the precedence used by `func workload install`. Folder installation and update MUST NOT use NuGet feed configuration.

#### Scenario: Explicit NuGet source is supplied

- **WHEN** a user installs a template package with `--source <feed>`
- **THEN** the CLI resolves the package from the specified NuGet feed

#### Scenario: Explicit source overrides the environment

- **WHEN** `FUNC_CLI_WORKLOADS_SOURCE` contains a configured NuGet feed
- **AND** the user installs a template package with a different `--source <feed>`
- **THEN** the CLI resolves the package from the feed specified by `--source`

#### Scenario: Explicit NuGet source is supplied for update

- **WHEN** a user updates a NuGet template package with `--source <feed>`
- **THEN** the CLI resolves the package update from the specified NuGet feed

#### Scenario: Update source overrides the environment

- **WHEN** `FUNC_CLI_WORKLOADS_SOURCE` contains a configured NuGet feed
- **AND** the user updates a NuGet template package with a different `--source <feed>`
- **THEN** the CLI resolves the package update from the feed specified by `--source`

#### Scenario: NuGet feed override is configured

- **WHEN** `FUNC_CLI_WORKLOADS_SOURCE` contains a configured NuGet feed
- **AND** the user does not supply `--source`
- **AND** the user installs a template package through the TemplateEngine NuGet installer
- **THEN** the CLI resolves the package from that configured feed

#### Scenario: Source is invalid for a folder install

- **WHEN** a user supplies a folder package expression with `--source <feed>`
- **THEN** the CLI exits non-zero and reports that `--source` applies only to NuGet installs

#### Scenario: Source is invalid for a folder update

- **WHEN** a user updates a folder-installed template package with `--source <feed>`
- **THEN** the CLI exits non-zero and reports that `--source` applies only to NuGet updates

#### Scenario: Folder install ignores the environment

- **WHEN** `FUNC_CLI_WORKLOADS_SOURCE` contains a configured NuGet feed
- **AND** the user installs a folder package without `--source`
- **THEN** the CLI installs from the requested folder without consulting the configured NuGet feed

### Requirement: Package types determine the owning command

The CLI SHALL install a package through `func new install` only when it declares the `FuncTemplate` package type. The CLI SHALL install a package through `func workload install` only when it declares the `FuncCliWorkload` package type. A package declaring both types SHALL be rejected as ambiguous, and a package declaring neither type SHALL be rejected as unsupported.

#### Scenario: Template package uses func new install

- **WHEN** a package declares only the `FuncTemplate` package type
- **AND** the user invokes `func new install <package>`
- **THEN** the CLI proceeds with template installation

#### Scenario: Workload package is sent to func new install

- **WHEN** a package declares only the `FuncCliWorkload` package type
- **AND** the user invokes `func new install <package>`
- **THEN** the CLI exits non-zero
- **AND** directs the user to run `func workload install <package>`

#### Scenario: Template package is sent to workload install

- **WHEN** a package declares only the `FuncTemplate` package type
- **AND** the user invokes `func workload install <package>`
- **THEN** the CLI exits non-zero
- **AND** directs the user to run `func new install <package>`

#### Scenario: Force does not bypass command ownership

- **WHEN** a user passes a package to the wrong owning command with `--force`
- **THEN** the CLI still rejects the package and provides the owning-command guidance

#### Scenario: Package declares both package types

- **WHEN** a package declares both `FuncTemplate` and `FuncCliWorkload`
- **THEN** the CLI exits non-zero and reports that the package type declaration is ambiguous

#### Scenario: Package declares neither package type

- **WHEN** a package declares neither `FuncTemplate` nor `FuncCliWorkload`
- **THEN** the CLI exits non-zero and reports that the package is unsupported

### Requirement: Same-source installation supports version replacement and is idempotent

The CLI SHALL compare an install request with the existing package using the installer/source identity reported by Microsoft.TemplateEngine. For the same package and source, a different explicitly resolved version SHALL replace the installed version, whether newer or older, and the same version SHALL succeed without reinstalling.

#### Scenario: Newer version from the same source upgrades

- **WHEN** a template package is installed from a TemplateEngine source
- **AND** the user installs a newer version of the same package from the same source
- **THEN** the CLI upgrades the installed package to the newer version

#### Scenario: Same version from the same source is a no-op

- **WHEN** a template package version is already installed from a TemplateEngine source
- **AND** the user installs the same package version from the same source
- **THEN** the command succeeds without reinstalling or changing the installed package

#### Scenario: Older version from the same source downgrades

- **WHEN** a template package is installed from a TemplateEngine source
- **AND** the user requests an older version of the same package from the same source
- **THEN** the CLI replaces the installed package with the requested older version

### Requirement: Different-source installation requires explicit replacement

The CLI SHALL reject installation of an already installed package from a different TemplateEngine source unless the user passes `--force`. With `--force`, the CLI SHALL replace the existing installation so that only the package from the requested source remains authoritative.

#### Scenario: Different source without force is rejected

- **WHEN** a template package is already installed from one TemplateEngine source
- **AND** the user requests the same package from a different TemplateEngine source without `--force`
- **THEN** the CLI exits non-zero
- **AND** reports the source conflict and that `--force` is required to replace the installed package

#### Scenario: Force replaces a package from another source

- **WHEN** a template package is already installed from one TemplateEngine source
- **AND** the user requests the same package from a different TemplateEngine source with `--force`
- **THEN** the CLI replaces the existing installation with the package resolved from the requested source
- **AND** the requested source becomes authoritative for the installed package

### Requirement: Source replacement is failure-safe

The CLI SHALL preserve the previously installed template package until a forced cross-source replacement has completed successfully. A failed replacement MUST leave the previous package installed and usable.

#### Scenario: Replacement succeeds

- **WHEN** a forced cross-source replacement completes successfully
- **THEN** the previous installation is removed
- **AND** templates from the replacement package are available

#### Scenario: Replacement fails

- **WHEN** a forced cross-source replacement fails during resolution, acquisition, validation, or registration
- **THEN** the command exits non-zero
- **AND** the previously installed package remains installed and usable
