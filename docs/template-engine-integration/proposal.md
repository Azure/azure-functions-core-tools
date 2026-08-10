## Why

The CLI needs one reliable boundary around Microsoft.TemplateEngine so every template operation uses the same func host, settings hive, command context, constraint policy, and result model. Without that boundary, commands can leak engine types, reuse stale project context, or invoke templates without enforcing compatibility constraints.

## What Changes

- Make `Templater` the context-bound entry point for Microsoft.TemplateEngine bootstrapping, installed-template listing, and template-group resolution.
- Create one `Templater` per command invocation from an immutable snapshot of the command directory, resolved Functions project, stack, language, and extension bundle.
- Publish stable host parameters for project, stack, language, bundle ID, and bundle version so constraints and opt-in template bind symbols consume the same context.
- Return all installed templates from listing with eligibility and constraint diagnostics.
- Resolve exact template identities and short names into context-evaluated template groups, applying constraints before any template can be selected.
- Represent each eligible template as an invocation-ready `ResolvedTemplate` and expose template groups as immutable read-only lists of those templates.
- Allow commands to progressively filter a template group and decide whether to select, reject, or prompt when multiple templates remain.
- Keep command parsing, prompting, context resolution, and console rendering outside the integration layer.

## Capabilities

### New Capabilities

- `template-execution`: Context-aware template listing, mandatory constraint evaluation, group resolution, candidate selection, and invocation of installed templates.

### Modified Capabilities

None.

## Impact

- Affects the `func new` orchestration path and template help/listing behavior.
- Refactors the current `Templater`, func template host defaults, template grouping, and bundle constraint integration.
- Introduces func-owned catalog, group-resolution, resolved-template, invocation-request, and invocation-result models.
- Uses the shared func template settings hive while isolating engine environment state to one command invocation.
- Does not change template package install, update, or uninstall behavior.
