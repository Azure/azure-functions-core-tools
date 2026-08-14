## Why

Azure-Samples quickstarts will be distributed as ordinary `FuncTemplate` packages, but `func init` needs a coherent installed-template experience that works for both single-project and multi-project solutions. Users must be able to discover installed quickstarts, understand why a template is restricted, configure every generated Functions project, and find additional packages without turning initialization into an implicit package installer.

## What Changes

- Make interactive `func init` template-first by listing installed TemplateEngine templates with `tags.type = project` before resolving remaining template choices.
- Run an installed template by exact identity or short name through `func init --template <template>`.
- Require explicit template package installation; an unknown template reference provides browse and `func new install` guidance rather than installing implicitly.
- Show a stable Functions-owned browse URL alongside the installed-template experience without defining the backing gallery or catalog in this change.
- Keep workload-restricted installed templates visible as unavailable, show a concise restriction summary, and render actionable remediation supplied by the constraint system.
- Support one or more independently configured Functions projects from one project template without introducing a separate solution template type.
- Treat `--stack` and `--language` as whole-template filters: every declared Functions project must match an explicitly supplied value.
- Require each Functions project to be represented by a configuration finalization action that references a resolved primary-output file at the project root and supplies canonical stack and language.
- Validate every configuration action before scaffolding, then use the trusted action to write CLI-owned `.func/config.json` files after template creation and before ordinary post-actions.
- Preserve metadata-only adoption and healing for existing projects without invoking quickstart templates.
- Report configuration failures after scaffolding as partial initialization without deleting generated content.
- Defer workload-constraint syntax and evaluation details to `template-engine-constraints` and configuration-action details to `template-engine-post-actions`.

## Capabilities

### New Capabilities

- `func-init-quickstarts`: Installed quickstart discovery, template-first selection, restriction guidance, multi-project configuration finalization, and browse guidance for `func init`.

### Modified Capabilities

None.

## Impact

- Extends and revises planned behavior in `func-init-execution`, including template selection order and project configuration ownership.
- Depends on `template-engine-integration` for installed catalog metadata, constraint-aware resolution, resolved primary outputs, dry-run effects, and invocation.
- Depends on `template-engine-constraints` for workload restriction outcomes and customer calls to action.
- Depends on `template-engine-post-actions` for the mandatory Functions project configuration action.
- Depends on `template-package-install` for explicit package installation and on `azure-samples-template-pipeline` for packaged quickstart supply.
- Requires corresponding package-authoring changes so synthesized and authored quickstarts declare workload requirements, project primary outputs, and configuration actions.
- Does not define remote template search, a gallery implementation, implicit installation, package publication, workload acquisition, or multi-project adoption.
