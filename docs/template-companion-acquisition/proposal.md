## Why

Project scaffolding is moving out of stack workloads into `FuncTemplate` packages. Users who install a stack should still receive basic creation templates and curated quickstarts without discovering and installing each package separately. The installed-template runtime does not own that acquisition step.

## What Changes

- Propose a reviewed stack-to-companion mapping, with separate basic and curated quickstart bundles.
- Acquire companions during explicit stack installation through setup and direct stack install, using the shared template lifecycle.
- Propose a quickstart-only opt-out that does not remove installed packages or basic templates.
- Define source and version boundaries, repeat installation, fresh and upgraded machines, cancellation, and partial completion.
- Keep initialization, automatic updates, and package publication outside acquisition.

## Capabilities

### New Capabilities

- `template-companion-acquisition`: Planned and explicit acquisition of stack companions through the existing workload and template lifecycles.

### Modified Capabilities

None.

## Impact

- Depends on `template-package-install` for validation, locking, replacement safety, and installed package identity.
- Supplies packages consumed by `func-init-execution`, `func-new-execution`, and `func-init-quickstarts`.
- Requires publishing to provide stable per-stack bundle identities and migration coverage.
- Does not define a new template store, trust protocol, automatic refresh scheduler, or package-manager dependency traversal.