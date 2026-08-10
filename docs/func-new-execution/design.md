## Context

See `proposal.md` for motivation and `specs/func-new-execution/spec.md` for the command contract.

`NewCommand` currently inherits the shared optional `[path]` argument, exposes template selection only through `--template`, and uses `--output` for plain/JSON rendering. It disables unmatched-token errors, dynamically attaches template options to the singleton command, manually walks unmatched tokens, and can ignore unknown template arguments. `NewCommandArgPreparer` performs a synchronous pre-parse lookup from process current directory and intentionally swallows hydration failures. `NewCommandRunner` then combines project and bundle resolution, legacy template workload lookup, group selection, prompting, and provider dispatch.

The `template-engine-integration` change establishes the replacement boundary: one command-scoped `Templater`, constraint-aware immutable template groups, projected `func.host.json` parameter metadata, and self-invoking `ResolvedTemplate` instances. This change defines how `func new` consumes that boundary for item templates. It does not define `func init`.

Microsoft.TemplateEngine 10.0.301 directly supports preview through `TemplateCreator.InstantiateAsync(..., dryRun: true, ...)`. Preview returns creation effects, including file changes and post-actions, without creating the target directory or applying template content.

## Goals / Non-Goals

**Goals:**

- Keep `NewCommand` limited to the stable System.CommandLine surface and delegation.
- Preserve the user's raw template token sequence until an item-specific schema is available.
- Make explicit input authoritative and make every narrowing step observable and testable.
- Use one immutable project context and one `Templater` throughout discovery, parsing, selection, preview, and invocation.
- Separate invalid explicit input from promptable missing input.
- Reuse the same invocation path and result model for preview and creation.

**Non-Goals:**

- Design project-template discovery or `func init` execution.
- Decide how missing or unsupported TemplateEngine `tags.type` values are classified; `func new` consumes only candidates classified as `TemplateType.Item`.
- Define authorization or execution policy for real template post-actions. Dry-run reports but never executes them.
- Reproduce every `dotnet new` option or its exact command-tree mutation strategy.
- Let `--force` bypass template type, constraints, argument validation, or selection ambiguity.

## Decisions

### `func new` owns a template positional argument

`NewCommand` stops calling `AddPathArgument()` and instead adds an optional positional template reference. A new `--path` option binds to `WorkingDirectory` and defaults to the process current directory through the existing domain conversion rules. This directory is where TemplateEngine runs and applies item-template output; it is not required to be the Functions project root.

```text
func new [<template>] [host options] [template options]
func new --template <identity-or-short-name> [...]
func new install|update|uninstall ...
```

The positional reference and `--template` / `-t` are mutually exclusive. The explicit option remains necessary for full identities and names consumed by registered lifecycle subcommands. System.CommandLine routes a positional `install`, `update`, or `uninstall` token to the corresponding subcommand; `func new --template install` stays on the execution path.

An omitted selector is valid only as the start of interactive discovery. The runner lists eligible item-template families and asks the user to select one. Non-interactive execution reports that a template must be supplied.

**Alternative considered:** keep `[path]` positional and require `--template`. This preserves the current grammar but makes the principal operation less natural and prevents parity with TemplateEngine-oriented CLIs. It is rejected.

### `TemplateType.Item` is part of the resolution query

`Templater` projects TemplateEngine's existing `tags.type` convention into a func-owned `TemplateType` representation. `func new` requests `TemplateType.Item` when listing or resolving:

```csharp
Task<TemplateGroupResolution> ResolveGroupAsync(
    string reference,
    TemplateType templateType,
    CancellationToken cancellationToken);
```

Type is supplied during matching rather than applied as a `TemplateGroup` filter afterward. This prevents project and item templates that share a short name from creating cross-type group ambiguity. Wrong-type exact matches remain diagnostic matches so the command can direct users to `func init`; they never enter the eligible group.

The `template-engine-integration` design and requirements must expose this query dimension before command implementation begins. Exact identity remains an ambiguity escape hatch, not a type or constraint escape hatch.

**Alternative considered:** resolve every type and filter the returned group in `NewCommandRunner`. This is too late because group-identity ambiguity may already have been reported across templates the command can never execute. It is rejected.

### Stage A parses only the stable host surface

The static command owns:

- optional positional `<template>`;
- `--template` / `-t`;
- `--path`;
- `--name` / `-n`;
- `--language`;
- `--output`;
- `--force`;
- `--non-interactive`;
- `--dry-run`;
- `--list` / `-l`;
- help and inherited global options.

Stage A may tolerate unmatched tokens only as an intermediate mechanism for preserving the exact template-specific token sequence. No unmatched token can be accepted as a successful final parse. `NewCommand` packages parsed host values and the untouched template token tail into an immutable `NewExecutionRequest` and delegates once.

```text
NewExecutionRequest
|- WorkingDirectory
|- TemplateReference
|- Name
|- ExplicitLanguage
|- Force
|- NonInteractive
|- DryRun
|- JsonOutput
`- RawTemplateTokens
```

This replaces both dynamic option attachment and manual key/value walking. `NewCommandArgPreparer` is removed; the preliminary parse no longer resolves projects through a service locator, blocks on asynchronous work, reads `Environment.CurrentDirectory` in business logic, or swallows failures.

**Alternative considered:** attach every candidate option to the singleton `NewCommand` before the root parse. Candidate variants can expose different aliases and types, stale options can survive across invocations, and failures occur before project-aware diagnostics are available. It is rejected.

### Context is fully resolved before creating `Templater`

`NewCommandRunner` resolves:

1. the `--path` template execution directory, defaulting to process current directory;
2. the containing Functions project by walking upward from that directory, or from its nearest existing ancestor when the requested directory does not yet exist;
3. stack and explicit language override, or project language when no override exists;
4. extension bundle ID and version.

The effective language is canonicalized and validated before constructing `TemplateEngineContext`. Explicit `--language` is authoritative over ambient project language. A fresh `Templater` is then created and retained until listing or invocation completes.

`TemplateEngineContext.CommandDirectory` receives the requested template execution directory, while `TemplateEngineContext.Project.RootDirectory` receives the separately discovered project root. Consequently, `host:WorkingDirectory` can identify a nested creation location while `host:func:project-root` continues to identify the containing Functions project.

Failure to discover a Functions project anywhere in the execution path's hierarchy is a command error with guidance to run `func init`. The command does not create an environment with guessed or ambient project defaults.

**Alternative considered:** create `Templater` before language and bundle resolution and apply those values only as later filters. Constraints and bind symbols would observe incomplete context while selection observes different values. It is rejected.

### Candidate parsing distinguishes invalid input from missing input

`ITemplateArgumentParser` is a DI-registered command-layer service. For each `ResolvedTemplate`, it receives the candidate's projected parameter definitions, the reserved alias set, and the same raw template tokens. It builds an ephemeral parser and returns:

```text
TemplateCandidateParseResult
|- TemplateIdentity
|- ValuesByCanonicalName
|- InvalidInputDiagnostics
|- MissingRequiredParameters
`- IsArgumentCompatible
```

Unknown options, missing option values, invalid choices, and invalid types make a candidate argument-incompatible. A required parameter that has no explicit or resolved default does not make otherwise valid explicit input incompatible; it remains as missing input that can be prompted after selection. This distinction requires the `template-engine-integration` candidate-parsing wording to treat unresolved required values as non-invocable but still selectable.

The selected candidate is parsed once more with the same ephemeral schema after prompt values are added. That final result is authoritative. The root command graph is not mutated or reparsed.

**Alternative considered:** treat every missing required value as a candidate parse failure. This prevents interactive prompting and can incorrectly eliminate every otherwise viable candidate. It is rejected.

### Reserved aliases are assigned deterministically

The parser derives reserved aliases from the complete effective `func new` surface: host options, help aliases, recursive/global options, lifecycle convenience options, and registered subcommand names where they affect template selection. It then combines those aliases with each candidate's projected `longName` and `shortName`.

- A non-conflicting long name becomes `--{longName}`.
- A colliding long name falls back to `--param:{canonicalName}`.
- A non-conflicting explicit short name becomes `-{shortName}`.
- A colliding explicit short name falls back to `-p:{shortName}`.
- An absent or explicitly empty `shortName` produces no short alias.
- Short aliases are never generated automatically.

Help, parsing, missing-value diagnostics, and prompt labels all use the effective assigned aliases. Canonical names remain unchanged in the invocation map.

**Alternative considered:** reject every template with a host-option collision. Deterministic fallback aliases preserve access to valid third-party templates without weakening the host command grammar. It is rejected.

### Narrowing order is explicit and command-owned

The execution runner applies:

```text
resolve reference with TemplateType.Item
  -> mandatory constraints inside Templater
  -> explicit language, otherwise project language
  -> candidate argument compatibility
  -> highest remaining precedence
  -> count-based selection
```

Argument compatibility precedes precedence so a higher-precedence candidate cannot hide a lower-precedence candidate that uniquely understands the user's explicit options. Precedence narrows variants within an already resolved template group; it does not silently choose between unrelated group identities.

No separate `TemplateSelectionPolicy` abstraction is introduced. `NewCommandRunner` can express the short immutable-filter sequence directly through `TemplateGroup`. Extraction remains possible if future commands develop materially different reusable selection rules.

**Alternative considered:** copy `dotnet new` and apply precedence before candidate parsing. This can reject explicit arguments that intentionally identify another viable candidate. It is rejected.

### Prompting is a completion mechanism, not a questionnaire

The runner invokes `IInteractionService` only for:

1. choosing an item-template family when no selector was supplied;
2. choosing among candidates or groups that remain genuinely ambiguous;
3. obtaining visible required parameter values that have no supplied or resolved default.

Optional parameters are left to template defaults. Invalid explicit values fail immediately rather than being replaced through a prompt. Hidden required values without defaults surface as template-authoring diagnostics because the user has no advertised way to supply them.

`--non-interactive` and `IInteractionService.IsInteractive == false` use the same refusal path. Diagnostics enumerate candidate identities or all missing required aliases so automation can correct the invocation in one pass.

Existing selection rendering can be reused where it accepts func-owned candidate models, but prompting must not depend on legacy template workload or provider types.

### `--path` is the template execution directory

`--path` has one concrete filesystem meaning: the directory passed to TemplateEngine as its working and output directory.

- omitted path defaults to process current directory;
- relative path resolves from process current directory;
- absolute path remains absolute;
- the requested path may be a nested directory that TemplateEngine creates.

Project discovery starts from that resolved path and walks upward. If the path does not yet exist, discovery starts from its nearest existing ancestor without changing the requested TemplateEngine directory. This keeps item placement intuitive while allowing stack, language, bundle, constraints, and bind symbols to come from the containing project.

The current `NewCommand.OutputOption` remains the plain/JSON rendering selector. No `--format` migration is required by this change.

**Alternative considered:** make `--path` identify the project root and add a separate template output option. Item templates commonly run in nested project directories, and two path-like options create unnecessary ambiguity. It is rejected.

### Dry-run is an invocation mode on `ResolvedTemplate`

`TemplateInvocationRequest` gains an invocation mode rather than adding a separate preview method:

```text
TemplateInvocationMode
|- Create
`- DryRun
```

`ResolvedTemplate.InvokeAsync` maps `DryRun` to TemplateEngine's native `dryRun` argument. Both modes perform identical selection, constraint, argument, default, output-path, and conflict evaluation. The returned func-owned result always carries projected file changes and post-actions; create mode additionally carries applied creation outputs.

Dry-run never creates the requested execution directory, writes files, or executes post-actions. Destructive effects are still returned. Without `--force`, they are rendered as changes that would block actual creation; `--force` remains orthogonal and changes conflict permission, not whether preview writes.

The command renderer distinguishes create, modify, and delete effects and clearly labels the output as a preview. JSON and plain formatting consume the same result model.

**Alternative considered:** implement dry-run by invoking against a temporary directory. This can differ from the real target's existing files and therefore misreport destructive effects. Native TemplateEngine dry-run is rejected only if the pinned API ceases to provide creation effects.

### Command outcomes remain func-owned and render once

`NewCommandRunner` returns a typed execution result or renders through a focused `NewCommandRenderer`, following the repository's existing command boundary. Expected user states include:

- project not resolved;
- template not found or wrong type;
- restricted template;
- ambiguous groups or candidates;
- invalid explicit arguments;
- missing non-interactive input;
- destructive conflict;
- dry-run success;
- creation success.

Known user failures become `GracefulException` only at the command boundary when that matches the final command architecture. Unexpected TemplateEngine or orchestration defects remain unwrapped. Cancellation propagates through context resolution, listing, parsing preparation where asynchronous, prompting, and invocation.

## Risks / Trade-offs

- **[Stage A temporarily accepts unmatched tokens]** -> Preserve them only for Stage B and require a strict candidate parse before any success path.
- **[Active integration artifacts currently describe missing required values as parse failures]** -> Refine that boundary so missing required values remain selectable but cannot be invoked until supplied.
- **[TemplateEngine type metadata may be absent or malformed]** -> Keep classification policy in the integration change; `func new` requests only positively classified item templates.
- **[Changing the positional path meaning breaks previews and scripts]** -> Mark the change as breaking, update help and documentation together, and provide precise parser diagnostics for obsolete forms.
- **[Execution paths can differ from project roots]** -> Carry both values in immutable context and test `WorkingDirectory` separately from `func:project-root`.
- **[Candidate parsing cost grows with group size]** -> Reuse projected immutable schemas and parse only the already type-, constraint-, and language-filtered group; optimize only after measuring.
- **[Dry-run results can be mistaken for applied changes]** -> Use explicit preview headings and result kinds in both plain and JSON rendering.
- **[Lifecycle names shadow template short names]** -> Preserve deterministic access through `--template` / `-t` and show that escape hatch in diagnostics.

## Migration Plan

1. Extend `template-engine-integration` with `TemplateType` projection and type-scoped listing and resolution.
2. Refine candidate parsing so invalid explicit input and unresolved required input are distinct.
3. Introduce the static `func new` grammar, template execution `--path`, existing rendering `--output`, and `--dry-run`.
4. Add strict candidate parsing and deterministic alias assignment using projected parameter definitions.
5. Route listing, filtering, prompting, dry-run, and creation through command-scoped `Templater` and `ResolvedTemplate`.
6. Replace legacy workload/provider dispatch, dynamic option hydration, and `NewCommandArgPreparer`.
7. Update command help, tests, and user documentation for the positional template and output-option changes.

During migration, the legacy execution path can remain behind the existing runner while the new func-owned models are introduced. The final command switch must remove permissive fallback parsing so behavior cannot vary by whether pre-parse hydration happened to succeed. Rollback restores the previous command wiring and option meanings; the shared TemplateEngine settings hive and installed package registry are unchanged.
