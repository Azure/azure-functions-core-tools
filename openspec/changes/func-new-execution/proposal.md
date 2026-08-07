## Why

`func new` needs a deterministic item-template execution path that resolves the current Functions project, selects only compatible item templates, strictly interprets template-specific arguments, and prompts only when user input is genuinely required. The current command mixes project targeting, dynamic option hydration, permissive unmatched-token handling, and legacy provider dispatch in ways that can ignore invalid input or select a template against stale context.

## What Changes

- **BREAKING** Replace the inherited positional `[path]` argument on `func new` with positional `<template>` selection; `--path` becomes the directory where the item template runs.
- Restrict `func new` discovery, listing, resolution, and invocation to templates using TemplateEngine's `tags.type = item` convention.
- Keep `--template` / `-t` as an explicit selector for full identities and template names that conflict with reserved `func new` subcommands.
- Resolve the Functions project, stack, language, and extension bundle before creating one command-scoped `Templater`.
- Perform strict two-stage parsing: first parse the stable command surface while preserving raw template tokens, then parse those tokens independently against each candidate's projected symbol definitions.
- Apply explicit filters and argument compatibility before using template precedence; prompt only when selection or required values remain unresolved.
- Add `--non-interactive` so any operation that would require a prompt fails with actionable diagnostics instead.
- Add `--dry-run` to evaluate and render TemplateEngine creation effects without writing files or running post-actions.
- Discover the containing Functions project by walking the directory hierarchy from the template execution path rather than requiring `--path` to identify the project root directly.
- Reserve lifecycle command names such as `install`, `update`, and `uninstall`; conflicting templates remain addressable through `--template` / `-t`.
- Remove successful fallbacks that ignore unknown, malformed, or invalid template arguments.

## Capabilities

### New Capabilities

- `func-new-execution`: Item-template discovery, candidate parsing, explicit filtering, selection, prompting, preview, and invocation through `func new`.

### Modified Capabilities

None.

## Impact

- Reworks `NewCommand`, `NewCommandRunner`, command argument preparation, template option hydration, language filtering, and template selection.
- Depends on the `template-engine-integration` change for command-scoped `Templater`, `TemplateType.Item` resolution, projected parameter metadata, immutable template groups, and `ResolvedTemplate.InvokeAsync`.
- Changes the positional argument while preserving the existing plain/JSON `--output` rendering option.
- Uses TemplateEngine's native dry-run support and creation-effects model.
- Does not define or change `func init` project-template behavior.
