## Why

Basic project scaffolding is moving out of stack workloads into `FuncTemplate` packages. Explicit stack installation should still supply basic project/item templates without another manual install command. Curated quickstarts instead preserve metadata discovery and on-selection acquisition through a separate guided flow.

## What Changes

- Propose a reviewed stack-to-basic-companion mapping without mandatory curated bundles.
- Acquire companions during explicit stack installation through setup and direct stack install, using the shared template lifecycle.
- Exclude curated preinstallation and its flags, preserving existing user-installed packages.
- Define source and version boundaries, repeat installation, fresh and upgraded machines, cancellation, and partial completion.
- Keep template execution, curated discovery/guided use, automatic updates, and publication outside basic companion acquisition.

## Capabilities

### New Capabilities

- `template-companion-acquisition`: Planned and explicit acquisition of stack companions through the existing workload and template lifecycles.

### Modified Capabilities

None.

## Impact

- Depends on `template-package-install` for validation, locking, replacement safety, and installed package identity.
- Supplies packages consumed by `func-init-execution`, `func-new-execution`, and `func-init-quickstarts`.
- Requires published basic companions and references discovery-owned sanctioned metadata and explicit guided use for curated samples.
- Does not define a new template store, trust protocol, automatic refresh scheduler, or package-manager dependency traversal.