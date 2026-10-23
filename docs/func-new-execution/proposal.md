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
- Changes the positional argument while preserving the existing plain/JSON `--format` rendering option.
- Uses TemplateEngine's native dry-run support and creation-effects model.
- Does not define or change `func init` project-template behavior.

## Staged native-item consumer

`NewExecutionCommand` is an **unregistered** command harness for the proposed
grammar. The registered `NewCommand`, its `[path]` positional, preparation hook,
help hydration and legacy provider dispatch are unchanged. There is no public
feature flag or runtime fallback between these paths.

The harness's `ParseInvocation` entry point accepts tokens after `new`, binds
host options once and preserves the schema-specific token sequence, including
colon aliases and inline values. It creates a fresh graph per invocation with
POSIX bundling and response-file expansion disabled. A host-only lexical routing
pass selects a lifecycle child only before a template selector or schema-specific
tail begins. Item parsing does not expose lifecycle children, so separated
parameter values such as `--greeting install` cannot dispatch a package command.
All lifecycle aliases remain reserved in either graph. Existing help/global
options and actual lifecycle requests remain parser-owned. Lifecycle tests
supply fresh inert children, not implemented package installation commands.

`NewExecutionRunner` consumes an explicitly supplied
`TemplateConstraintEvaluationSnapshot`, the existing project resolver and
installed initializer language metadata, named project configuration/profile
consumers, the host.json reader, installed bundle resolver, shared argument
parser and protected `ITemplaterFactory`. It does not implement startup
availability capture or acquire packages. Project discovery starts at the
nearest existing ancestor and checks the closest `host.json` or
`.func/config.json` root before using installed project factories; nested source
files or a standalone package.json do not establish a separate Functions root.
An unrecognized marked root is a repair error, not permission to borrow context
from an outer project.

The composed factory uses workload-only availability; the resolved bundle
identity and version come solely from the immutable command context. The
consumer remains an item-only `CreateAsync` caller. Its test factory follows
the paired `TemplateProjectSupport` signature without enabling project creation.
Installed initializer language metadata delegates to the existing workload
stack metadata; this consumer does not introduce a second catalog or generation
policy.

One disposable native environment spans item listing/resolution, language and
argument filtering before precedence, family/candidate selection, required-only
completion, canonical reparse and exact-capability preview/create. The host name
stays separate from canonical symbol values. Shared-parser defaults are consumed
as returned, including optional defaults; the consumer does not invent answers
for optional values or override explicit input. Scalar/repetition/choice and
terminator policies remain **proposals**, not approved public behavior.

Plain and JSON output use one typed result, retaining candidate diagnostics and
raw-tail token indices, selected identity, distinct execution/project paths,
native invocation status and file effects. Preview never creates the requested
directory or applies content. Native item post-actions remain unsupported;
the consumer does not manufacture post-action or applied-output information
absent from the current runtime result.

Listing and dry-run requests require a clean installed store. A pending recovery
journal is refused before native bootstrap, without restoring the live hive or
changing its journal and backup. Ordinary creation keeps the store's existing
recovery behavior. Admission markers and private caches are not a promise of zero
filesystem activity. `--force` with dry-run still previews without applying
changes. Listing rejects creation-only `--name` and `--force` options and gives
guidance when no installed items match.

`NewExecutionCommandTests` and `NewExecutionJourneyTests` exercise binding and
the real journey through a private installed native folder package, the Node
workload's actual factory/initializer registrations, scoped configuration/profile
consumers and the installed-bundle resolver over controlled catalog rows.
Controls cover nested/nonexistent targets, aliases/name, language override,
argument compatibility before precedence, constraints/type/visibility,
interactive versus redirected input, required completion, invalid input,
conflicts/force, cancellation/disposal, preview/create and an empty no-op item.
These are controlled consumer tests, **not** production template qualification.

Prompt titles and selection labels use the existing literal markup escaping;
selected labels map back to unchanged native values before canonical validation.
Required boolean prompting accepts the shared parser's case-insensitive scalar
spellings. Multiple-profile guidance uses the existing `defaultProfile` setting
or `func profile set`, not an unsupported `--profile` option.

After native creation returns validated success, the consumer preserves that
completed create result through both runner boundaries and command rendering,
including a legitimate zero-output success. This is limited to the created
phase of a non-dry-run invocation. Preview, pre-create decisions, conflicts,
unsuccessful native returns and cancellation thrown during creation retain their
existing cancellation behavior. File effects or a non-null invocation alone do
not establish completion.

Public cutover still requires production native `FuncTemplate` inventory and
package lifecycle/trust wiring; .NET/Node/Python restore/build/start/invocation
baselines and replacement coverage; template-specific help integration; and a
qualified startup availability handoff. The published parent runtime owns mounted
validation and output-boundary behavior; ordinary composed consumer checks do
not independently qualify its guard algorithms, concurrency or wider topology.
Parent runtime results and earlier consumer results are not evidence for a new
composed tree. This staged consumer is not a fully qualified public replacement.
Project-template create-only configuration remains distinct from legacy
init/adoption/repair persistence. No package IDs or feeds are guessed here.
