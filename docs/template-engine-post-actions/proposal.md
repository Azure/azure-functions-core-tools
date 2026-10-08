## Why

Project templates must declare the Functions projects they create so the CLI can validate topology and write its own configuration. Ordinary work such as dependency restore also needs a defined boundary, rather than template-controlled shell commands. The init, quickstart, and Samples designs defer action identity, arguments, projection, and execution to this change.

## What Changes

- Define a mandatory trusted Functions project configuration action tied to a primary output.
- Define an initial ordinary package-restore action with CLI-owned adapters and fixed process argument construction.
- Validate action structure, project roots, languages, combined file effects, and supported processors before mutation.
- Project action effects for dry-run without executing configuration writes or restore commands.
- Keep configuration finalization separate from ordinary action execution and report cancellation and partial completion accurately.

## Capabilities

### New Capabilities

- `template-post-actions`: Supported action declarations, preflight, preview, ordered execution, cancellation, and failure outcomes.

### Modified Capabilities

- `func-init-execution`: Use a concrete trusted configuration action and finalize projects before ordinary actions.
- `azure-samples-template-pipeline`: Validate trusted action declarations without executing repository-controlled code.

## Impact

The proposal supplies the contract consumed by runtime projection, init orchestration, and template packaging. It does not implement processors, install language toolchains, execute arbitrary scripts, or change the CLI's general process-launching policy. The proposed action identifiers, initial restore managers, and ordinary-action defaults require team review before implementation.