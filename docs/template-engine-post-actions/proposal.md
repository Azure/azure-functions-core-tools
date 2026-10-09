## Why

Project templates must declare the Functions projects they create so the CLI can validate topology and write its own configuration. Ordinary work such as dependency restore also needs a defined boundary, rather than template-controlled shell commands. The init, quickstart, and Samples designs defer action identity, arguments, projection, and execution to this change.

## What Changes

- Define a mandatory trusted Functions project configuration action tied to a primary output.
- Define an initial ordinary package-restore action with CLI-owned adapters and fixed process argument construction.
- Propose a separate Go module-tidy action rather than treat its manifest mutation as restore-only behavior, and bind all adapter launches to validated absolute tool identities.
- Validate action structure, project roots, languages, combined file effects, and supported processors before mutation.
- Project action effects for dry-run without executing configuration writes or restore commands.
- Keep configuration finalization separate from ordinary action execution and report cancellation and partial completion accurately.

## Capabilities

### New Capabilities

- `template-post-actions`: Supported action declarations, preflight, preview, ordered execution, cancellation, and failure outcomes.

### Modified Capabilities

- `func-init-execution`: Use a concrete trusted configuration action and finalize projects before ordinary actions.
- `func-init-quickstarts`: Consume independent resolved project inventory before matching configuration metadata and applying whole-template filters.
- `func-new-execution`: Own create-mode ordinary action dispatch and partial outcomes while keeping dry-run non-executing.
- `azure-samples-template-pipeline`: Validate trusted action declarations without executing repository-controlled code.

## Impact

The proposal supplies the contract consumed by runtime projection, init/item orchestration, and template packaging. It does not implement processors, provision missing language toolchains, execute actions in the central packager, or rewrite the CLI's general process-launching implementation. Restore-only processors do not add missing manifest dependencies or resolve the Durable template dependency bug; that requires an author fix or separately reviewed package-add contract. The distinct Go tidy action, absolute tool/launcher resolution, proposed action identifiers, initial managers, item execution policy, and ordinary-action defaults require team review before implementation.