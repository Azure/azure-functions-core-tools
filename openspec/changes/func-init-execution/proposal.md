## Why

`func init` needs a unified project-template execution path that can select an installed stack and language before any Functions project context exists. The current workload initializers combine stack discovery, command options, file generation, and post-generation behavior, preventing project templates from using the shared func TemplateEngine integration.

## What Changes

- Execute installed templates with TemplateEngine `tags.type = project` for new and forced project initialization.
- Build project-template compatibility by intersecting required template `language` tags with languages exposed by installed stack workloads.
- Support stack-first, language-first, and template-first selection through `--stack`, `--language`, and `--template`, prompting only when deterministic filtering leaves a genuine choice.
- Preserve existing-project detection and metadata-only adoption/healing without invoking project templates. Replace initializer-based stack metadata with `IProjectStack`, always persist canonical stack and language, make configuration-write failures fatal, and reject `--template` on these paths unless `--force` selects reinitialization.
- Reuse the same strict candidate parsing, reserved-alias handling, required-value prompting, precedence ordering, dry-run behavior, and non-interactive policy specified for `func new`.
- Create a prospective immutable project context from the selected target directory, stack, and language before constructing one command-scoped `Templater`.
- Make project-template invocation also generate CLI-owned `.func/config.json`, include that file in creation effects, and always persist canonical stack and language.
- Preserve destructive `--force` behavior by clearing target content except `.git`, including those planned deletions in dry-run output.
- Run project-template post-actions by default after project and CLI configuration generation.
- **BREAKING** Replace workload-facing `IProjectInitializer` with metadata-only `IProjectStack`; workload-specific init options move to project-template symbols and workloads no longer scaffold project files directly.

## Capabilities

### New Capabilities

- `func-init-execution`: Installed-stack discovery, project-template compatibility, interactive and non-interactive selection, project creation, dry-run, configuration generation, and adoption boundaries for `func init`.

### Modified Capabilities

None.

## Impact

- Reworks `InitCommand` orchestration, stack and language selection, workload registration, project creation, dry-run rendering, and project configuration persistence.
- Replaces `IProjectInitializer`, `InitContext`, `IInitOptionRegistry`, and workload-owned project scaffolding with `IProjectStack` metadata and TemplateEngine project templates.
- Depends on `template-engine-integration` for context-free catalog metadata, `TemplateType.Project` resolution, command-scoped `Templater`, projected parameters, immutable groups, and self-invoking `ResolvedTemplate`.
- Depends on the strict parser and alias behavior designed in `func-new-execution`.
- Requires project template packages for every supported installed stack and language.
- Does not define a new post-action authorization policy; project-template post-actions run by default for this initial behavior.
