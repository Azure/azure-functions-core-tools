## Why

`func init` needs a unified project-template execution path that can select an installed project template before any Functions project context exists. The current workload initializers combine stack discovery, command options, file generation, and post-generation behavior, preventing project templates from using the shared func TemplateEngine integration.

## What Changes

- Execute installed templates with TemplateEngine `tags.type = project` for new and forced project initialization.
- Select a project template first through `--template` or a prompt, and treat `--stack` and `--language` as filters that every generated project must match, prompting only when deterministic filtering leaves a genuine choice.
- Preserve existing-project detection and metadata-only adoption/healing without invoking project templates. Replace initializer-based stack metadata with `IProjectStack`, always persist canonical stack and language, make configuration-write failures fatal, and reject `--template` on these paths unless `--force` selects reinitialization.
- Reuse the same strict candidate parsing, reserved-alias handling, required-value prompting, precedence ordering, dry-run behavior, and non-interactive policy specified for `func new`.
- Create an immutable template context from the target directory, without a stack, language, or bundle, and one command-scoped `Templater` from it before listing project templates.
- Require project templates to declare trusted Functions project configuration actions that reference resolved primary outputs and supply canonical stack and language; use those actions to generate CLI-owned `.func/config.json`.
- Preserve destructive `--force` behavior by clearing target content except `.git`, including those planned deletions in dry-run output.
- Run mandatory project configuration actions after scaffolding and before ordinary project-template post-actions.
- **BREAKING** Replace workload-facing `IProjectInitializer` with metadata-only `IProjectStack`; workload-specific init options move to project-template symbols and workloads no longer scaffold project files directly.

## Capabilities

### New Capabilities

- `func-init-execution`: Installed-stack discovery, project-template compatibility, interactive and non-interactive selection, project creation, dry-run, configuration generation, and adoption boundaries for `func init`.

### Modified Capabilities

None.

## Impact

- Reworks `InitCommand` orchestration, template selection, workload registration, project creation, dry-run rendering, and project configuration persistence.
- Replaces `IProjectInitializer`, `InitContext`, `IInitOptionRegistry`, and workload-owned project scaffolding with `IProjectStack` metadata and TemplateEngine project templates.
- Depends on `template-engine-integration` for constraint-aware catalog entries, `TemplateType.Project` resolution, command-scoped `Templater`, projected parameters, immutable groups, and self-invoking `ResolvedTemplate`.
- Depends on `template-engine-post-actions` for the trusted Functions project configuration action, projected action metadata, resolved primary-output references, and dry-run behavior.
- Depends on the strict parser and alias behavior designed in `func-new-execution`.
- Follows `func-init-quickstarts` for template-first selection, whole-template filters, and multi-project topology.
- Requires project template packages for every supported installed stack and language.
- Does not define a new authorization policy for ordinary post-actions; mandatory project configuration actions cannot be skipped or marked continue-on-error.

## Staged installed-project consumer

`InitExecutionCommand` is an unregistered, invocation-scoped harness for the
installed-project subset. It does not replace public `InitCommand`, alter its
adoption/healing or workload scaffolding, add a feature flag, or remove providers.
Its proposed grammar is:

```text
init [path] --template <identity-or-short-name> [--stack <stack>] [--language <language>]
     [--name <name>] [--non-interactive] [--dry-run] [--output plain|json] [template options]
```

Put an explicit target before template options. The optional positional value is
always a target, never a template reference. Omitted templates are selected
automatically only when one applicable family remains, or through a family picker
before variant parsing. The shared strict parser, literal prompts and required
completion are also used by staged NEW; argument compatibility precedes variant
precedence, and host name remains outside canonical template parameters.

The state boundary is resolved before any template discovery. An absent/empty
target enters the installed-project template path. A root `host.json` without
`.func/config.json` enters adoption. Existing CLI configuration with an installed
multi-language runtime but no language enters repair. Complete configuration,
legacy runtime-only single-language configuration and unsupported partial shapes
refuse without modification. Malformed or unreadable CLI configuration is an
explicit failure, not an empty-config fallback. Nested projects are not adopted
recursively. `--force` remains unsupported and never deletes content.

Adoption/repair reads `local.settings.json`, the existing root-project resolver,
and the initializer's read-only stack/language metadata. It never opens a
`Templater`, performs template/package discovery or acquisition, invokes an
initializer, or executes a configuration post-action. Explicit invalid filters,
conflicting runtime ownership, ambiguous aliases and unresolvable input fail
without silently choosing metadata. Missing adoption stack prompts even when
only one stack is installed; redirected/non-interactive callers supply `--stack`.
`--template`, `--name` and raw template arguments are rejected on these paths
rather than ignored.

Successful staged adoption/repair always persists canonical stack and language,
including a single-language adoption. Unlike public init's incomplete-language
adoption, an unresolved multi-language project requires `--language` or an
interactive language selection. Runtime-only single-language configs retain the
public initialized-state refusal. The public command's behavior is unchanged.
The only shared extraction is pure create/merge JSON serialization from its
existing persistence helpers; public init retains its I/O order, optional fields,
error causes and cancellation checks. Both paths still use `IFileSystem`'s
atomic file writer. Repair merges the latest readable input, preserves profiles
and unknown keys, and refuses if it disappeared, became invalid, changed runtime
ownership or acquired a language while input was being resolved.

Existing-project dry-run reports the planned configuration path, canonical
metadata and create/modify operation with `written=false`; it writes neither
configuration nor source and opens no template store. Actual success is
`adopted` or `repaired` only after persistence and cancellation checks. Cancellation
after a committed write preserves that file but renders no success. These checks
do not promise a transaction across concurrent external edits or output writes.

On the empty-target path, the directory becomes both command directory and
prospective project root before opening one protected project flow; stack,
language, bundle and existing-project profile defaults remain unavailable.

Project catalog entries carry the existing planner's unconditional declarations
from that flow's configuration snapshot. Whole-template filters use their
installed canonical stack/language metadata before selection. Restricted entries
and their diagnostics remain visible alongside the interactive picker, but are
never offered as selectable choices. After required completion, native preview
supplies the actual configuration plans for the authoritative whole-template
assertion. Preview and creation invoke the exact selected capability in the same
protected flow. Creation alone finalizes through the existing runtime and atomic
configuration writer; there is no secondary creation or initializer fallback.

Plain and JSON output distinguish **planned** configurations/effects from
`ConfiguredProjects`, the actually finalized subset. Configuration failure is
nonzero, preserves generated content and completed roots, and stops subsequent
finalization. Cancellation releases the flow and never reports success.
Every result after successful plan computation retains that ordered planned
metadata, including subsequent conflict or revalidation failures; a result
before planning has no synthetic plan. Project configuration diagnostics remain
separate from func host metadata diagnostics and block capability creation.

In JSON mode, unavailable discovery entries and their diagnostics travel as
`unavailableTemplates` data rather than a pre-rendered table. Prompts stay on
stderr and complete stdout is one JSON result, or empty when cancellation
prevents a result. The shared Spectre adapter writes unchanged serialized JSON
through its existing raw stdout writer, honoring both injected and default
stdout without markup rendering.

The installed-fixture tests compose actual Node workload metadata, native project
creation, canonical configuration, project resolution, and `ProfileResolver`.
They exercise a literal two-root layout as well as homogeneous variants and
mixed-language unconditional roots. This is not qualification of conditional
topology, renaming, remapped sources, supplemental configuration or ordinary
post-actions: the existing planner rejects those. The fixture configuration
action GUID and its input schema remain provisional, not a public authoring ABI.

Separate private-root adoption/repair fixtures compose the registered Node
project factory, physical existing files, CLI configuration readers and default
or custom profile resolution with no template packages installed. They exercise
no-write preview, initialized/repeat refusal, missing/invalid input, literal
Spectre prompts, structured redirected and interactive errors, persistence
failures and cancellation before and after a relevant write. Multiple profiles
without a default remain ambiguous to `ProfileResolver`; init neither picks one
nor rewrites their selection.

The temporary metadata source is the same `IProjectInitializer` projection already
required by the project planner; no initializer behavior is invoked. The approved
metadata-only `IProjectStack` migration is a separate owning change and must replace
this adapter when composed. Package acquisition, production default templates for
all six stacks, fresh/upgraded installability, and a Functions-owned browse URL
remain switch gates. Missing-template diagnostics give explicit
`func new install <package>` guidance without inventing a package ID or URL.

The published lower runtime still has pending mounted-type/output-topology work.
This stage neither repairs nor clears that dependency, and its controlled
fixtures do not establish general type, path or platform safety. The additive
declaration/result projection hunks in `Templater.GetCandidatesAsync`,
`TemplateEngineSession.ValidateProjectConfiguration` / project result initializers,
`TemplateCatalogEntry` and `TemplateInvocationResult` are explicit future
composition conflict surfaces; snapshot shape, mounted-type authorization,
read-snapshot validation, effect/path guards and native invocation order are
unchanged. No completed public INIT switch or milestone is claimed.
