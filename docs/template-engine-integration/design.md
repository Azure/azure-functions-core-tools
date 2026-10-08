## Context

See `proposal.md` for motivation and `specs/template-execution/spec.md` for the behavioral contract.

The current `Templater` creates a `FuncTemplateEngineHost`, `EngineEnvironmentSettings`, `TemplatePackageManager`, and `TemplateCreator`, but exposes raw engine objects and only wraps listing. Selection logic is split across resolver helpers, constraint evaluation is not an unavoidable part of resolution, and the current host parameter names do not accurately distinguish bundle identity from bundle version.

Several TemplateEngine components capture environment settings at construction. Host defaults are snapshotted, constraint instances may close over the environment, and creation and bind-symbol evaluation retain the same settings. The integration therefore needs a command-scoped lifetime that is broader than one candidate operation but narrower than the process. The persisted func settings hive remains shared across those environments.

## Goals / Non-Goals

**Goals:**

- Give commands one func-owned entry point for engine bootstrapping, catalog access, constraint-aware resolution, and creation.
- Prevent commands from depending on TemplateEngine implementation types.
- Guarantee that discovery, constraints, host bindings, parameter metadata, and creation observe one immutable command context.
- Make invalid selection states explicit and impossible to invoke.
- Preserve diagnostics throughout progressive candidate narrowing.
- Keep the resolved template responsible for its own invocation.

**Non-Goals:**

- Resolve the current project, stack, language, or extension bundle inside the template integration.
- Own `System.CommandLine` command construction, prompting, or console rendering.
- Implement package installation, update, uninstall, or source selection.
- Automatically inject func context symbols into templates.
- Allow file-conflict force behavior to bypass compatibility constraints.

## Decisions

### Templater is a command-scoped facade

`ITemplaterFactory` is registered through dependency injection and creates one `Templater` from a validated `TemplateEngineContext`. The factory owns only stable dependencies and settings-location policy. `Templater` owns the context-bearing host and engine session.

```text
command context resolution
          |
          v
ITemplaterFactory.Create(context)
          |
          v
      Templater
      |       |
      |       +-- ListAsync(type)
      |
      +-- ResolveGroupAsync(reference, type)
                  |
                  v
       TemplateGroup : IReadOnlyList<ResolvedTemplate>
                  |
                  +-- immutable command-selected filters
                  |
                  `-- selected item.InvokeAsync(...)
```

The facade exposes func-owned models only. Raw `IEngineEnvironmentSettings`, `TemplateCreator`, package manager, constraint manager, and `ITemplateInfo` objects remain internal.

**Alternative considered:** register one reusable `Templater` or engine environment. This risks stale project and bundle defaults because TemplateEngine components capture the environment. It is rejected.

**Alternative considered:** create a new engine environment for listing, every candidate, and invocation. This can make one command internally inconsistent and repeats cache and component initialization. It is rejected.

### Context is an immutable value snapshot

`TemplateEngineContext` contains:

```text
TemplateEngineContext
|- CommandDirectory
|- Project
|  |- RootDirectory
|  |- Stack
|  `- Language
`- Bundle
   |- Id
   `- Version
```

Project and bundle data may be absent only when the calling command's policy permits that state. Stack and language can be unavailable while the project root is known, as for every new project in `func init`. The integration does not infer missing values. Strong domain value types should be retained where available so canonical stack, language, bundle ID, and semantic version representations are decided before engine creation.

The context is copied into host defaults:

| Host parameter | Source |
|---|---|
| `WorkingDirectory` | `CommandDirectory` |
| `func:project-root` | `Project.RootDirectory` |
| `func:stack` | `Project.Stack` |
| `func:language` | `Project.Language` |
| `func:bundle-id` | `Bundle.Id` |
| `func:bundle-version` | `Bundle.Version` |

`func:` remains a namespace delimiter. It avoids collisions with built-in and third-party host defaults, and `host:func:bundle-id` is correctly parsed by TemplateEngine as binding source `host` with parameter name `func:bundle-id`.

The existing `func:bundle` and `func:bundle-channel` names are replaced before they become an authoring contract. `FuncTemplateEngineHost` overrides host-default lookup for `WorkingDirectory`, because the base host's built-in value is derived from `Environment.CurrentDirectory` and cannot be replaced by adding a dictionary default.

**Alternative considered:** flatten names to `func-bundle-id`. This is technically valid but loses a clear func-owned host parameter namespace and scales poorly as more host context is added. It is rejected.

### One internal engine session owns lifetime and disposal

An internal engine session creates the host, environment settings, package manager, constraint manager, and creator once. `Templater`, `TemplateGroup`, and `ResolvedTemplate` retain only the narrow internal services they require plus func-owned snapshots of engine metadata.

The command disposes `Templater` after listing or after resolved-template invocation completes. A `ResolvedTemplate` is deliberately valid only within that scope. It carries a reference to the session's invocation service, and that service checks disposal before creation so it never silently constructs a context-free replacement environment.

The initial implementation does not promise concurrent operations on one session. Commands execute one discovery and invocation pipeline. This avoids claiming thread safety for TemplateEngine components that are not controlled by this repository.

### Runtime and lifecycle share a store lease contract

The proposed handoff keeps the store rooted at the existing func home `templates` directory and keeps one owning implementation for cross-process locks, package mounts, cache refresh, and recovery. Runtime code consumes that implementation; it does not create a second registry or lock protocol. A live read session acquires a shared lease before opening managed package state and retains it while catalog entries, selected-template mounts, preview, or invocation are in use, including an interactive selection. Disposal releases the lease. Long prompts can delay a writer; that is an explicit safety tradeoff to review, not a promise of lock-free replacement.

Lifecycle operations use an exclusive live-store lease and cannot upgrade an active runtime read lease in place. Callers dispose read sessions before invoking acquisition or replacement, then create a fresh runtime session afterward. Isolated preflight uses an isolated hive and does not take a live read lease while its caller owns the exclusive lease. Cancellation releases leases through disposal. The store owner decides the lock implementation and pinned-engine recovery mechanics; runtime tests substitute the lease boundary.

The store supplies raw configuration bytes, owning package identity/version/source and available package-type metadata, the complete configuration inventory, and fingerprints needed to bind validation to the actual mounted content. Cached engine metadata is not proof of raw validity. The runtime retains func-owned diagnostics and the selected template's validated snapshot; immediately before creation it verifies that snapshot under the same lease. It never resolves a reference again to replace a selection. Lifecycle commits verify actual acquired declarations/metadata against preflight evidence and finish cache rebuilding before making the new state visible.

### Workload availability is not installed inventory

The proposed per-command availability snapshot combines the actual startup-loaded inventory with installed entries used only for diagnostics. Runtime workloads contribute the selected version that successfully loaded and completed initialization. A runtime whose `Configure` failed does not satisfy `func-workload`, even though current startup can leave partial services registered; changing or rolling back those services is outside this proposal. Content workloads contribute versions admitted to the CLI's RID-compatible inventory. That admission is not proof that worker configuration files or other payloads are intact; existing consuming commands/resolvers retain their own payload checks. Known consumer rejection must remain diagnostic rather than be relabeled usable, but the evaluator does not proactively scan every content payload. Older installed runtime versions, foreign-RID content, or rejected workloads do not become fallback candidates merely because a template range accepts them.

The snapshot distinguishes missing, usable, incompatible/rejected, load-failed, and initialization-failed inputs, with package identity/version, aliases, logical published identity where available, and a CLI-owned diagnostic. Construction happens once for the command and never loads workload code or contacts a feed during template evaluation. Until version-compatibility enforcement exists, it must not invent a passed compatibility check from package age or the closed coordination proposal. Future gates contribute their actual rejection state through this same input boundary. The proposal adds an availability adapter and startup outcome capture, not a new ABI schema or a startup recovery policy.

Before implementation, the runtime and store owners need to agree the read/write/isolated session API, how raw bytes and package metadata are exposed, and how startup outcomes enter the immutable snapshot. Engine-version selection, replacement/cancellation/cache-rebuild spike evidence, and transaction implementation remain store-owner work. The independently testable raw validator consumes bytes and supplied metadata, so it does not wait for these I/O agreements.

### Catalog projection preserves eligibility diagnostics

`ListAsync(type)` loads installed templates of the requested type and evaluates their constraints in the command context. It projects each engine template into a func-owned `TemplateCatalogEntry`, including identity, template type, aliases, group identity, language, precedence, package origin when known, visibility, parameter metadata, and eligibility diagnostics. The same projected candidate model is used by template groups so listing and execution cannot disagree about a symbol.

Restricted entries remain in the catalog so `func new --list` and diagnostic flows can explain why an installed template cannot run. The command decides whether ordinary presentation hides host-hidden templates; exact identity lookup still has access to them.

Commands build their context from what exists before a template is picked, so the catalog a command reads before a pick comes from the session that later invokes the template, and its eligibility is the eligibility resolution enforces. When a user picks from that catalog, the command forms the template group from the listed entries instead of looking the reference up again, so a package change during the prompt cannot replace the template. An explicit reference is still resolved once through type-aware resolution in the same session. `func init` shows entries that are not eligible as unavailable, with the summary from `template-engine-constraints`, and does not let users pick them. Project template entries also carry the projects their configuration actions declare, as `template-engine-post-actions` projects them, so `func init` can narrow its picker before parameters are known.

The integration uses a common `ConstraintEvaluation` model for both catalog entries and resolution:

```text
Eligible
Restricted(message, call-to-action)
NotEvaluated(message, constraint type)
Failed(message, constraint type)
```

`NotEvaluated` and recognized evaluation failures are fail-closed states. They are kept distinct from an ordinary restriction because they point to template or host configuration defects.

`template-engine-constraints` defines the func constraint types, how engine results map to these states, and the diagnostic kept for each unmet constraint.

Listing and resolution apply its raw declaration validation to current mounted template configuration, including already-installed packages and folder content. Engine scanning and cache entries cannot prove that no requirement was discarded. Eligibility uses the validated content snapshot, and invocation verifies that the selected template's configuration is still readable and unchanged before creation. A failed check blocks that selection with a diagnostic and rerun guidance, without resolving the reference again or replacing the selected template.

### Template type scopes listing and resolution

`TemplateType` projects TemplateEngine's `tags.type` convention:

```text
tags.type = item     -> TemplateType.Item
tags.type = project  -> TemplateType.Project
missing or other     -> matches neither type
```

`func new` requests `TemplateType.Item` and `func init` requests `TemplateType.Project`. Type is part of matching rather than a later group filter, so project and item templates that share a short name never form one ambiguous group. A wrong-type match is kept as a diagnostic so the command can point users to `func new` or `func init`, but it never enters the eligible group. A reference that matches only templates without a recognized type reports an authoring diagnostic rather than not found.

### Resolution returns an immutable read-only TemplateGroup

`ResolveGroupAsync(reference, type)` applies this order:

1. Find exact full identity matches.
2. If none exist, find exact case-insensitive short-name matches.
3. Set aside matches of another template type as wrong-type diagnostics.
4. Evaluate all constraints for the remaining matches.
5. Partition short-name matches by `groupIdentity`; an ungrouped identity is its own singleton group.
6. Project every eligible matched template into an invocation-ready `ResolvedTemplate`.
7. Return a func-owned resolution outcome containing either a group or targeted diagnostics.

Exact identity is an ambiguity escape hatch, not a type or compatibility escape hatch. Constraints are evaluated before any candidate enters the eligible set.

`TemplateGroup` implements `IReadOnlyList<ResolvedTemplate>`. Its items are the eligible templates for one group identity, in stable deterministic order. Rejected-template diagnostics are retained as group metadata but are not list items. Every item contains immutable projected command parameter definitions, including the effective func host aliases.

Filtering methods return new groups:

```csharp
internal sealed class TemplateGroup : IReadOnlyList<ResolvedTemplate>
{
    public int Count { get; }

    public ResolvedTemplate this[int index] { get; }

    public TemplateGroup FilterByLanguage(string language);

    public TemplateGroup FilterByArguments(IReadOnlySet<string> matchingIdentities);

    public TemplateGroup FilterToHighestPrecedence();
}
```

The concrete filter surface can grow with command needs, but every operation follows the same invariant: it can only narrow eligible items, preserve their order and symbol metadata, and retain diagnostics. The original group remains unchanged, making ambient and explicit selection attempts easy to compare without mutation.

The integration does not provide `Resolve()` and does not automatically select an item. `Count` is the boundary presented to command policy:

```text
Count == 0 -> command reports a targeted error
Count == 1 -> command may invoke group[0]
Count > 1  -> command filters further, reports ambiguity, or prompts
```

Precedence remains item metadata. A command can explicitly call `FilterToHighestPrecedence` when that is its policy, but precedence is not an implicit integration-level tiebreaker.

**Alternative considered:** return either one template or an error from `Templater`. This makes staged parsing, prompting, and command-specific narrowing difficult and loses a useful representation of partial resolution. It is rejected.

**Alternative considered:** expose `IReadOnlyCollection<ResolvedTemplate>`. A list better supports stable prompt ordering, indexed display, and direct access after a command has established that one item remains.

### ResolvedTemplate is the invocation capability

Every eligible template projected into a `TemplateGroup` is a `ResolvedTemplate`: it has passed mandatory constraints, has valid host metadata, and carries everything required for invocation. Its constructor remains internal so callers cannot fabricate an invocation from an unchecked template.

```csharp
Task<TemplateInvocationResult> InvokeAsync(
    TemplateInvocationRequest request,
    CancellationToken cancellationToken);
```

The object carries:

- the exact internal template descriptor represented by the list item;
- the command-scoped invocation service;
- its immutable canonical parameter and func host mapping metadata;
- the completed constraint evaluation snapshot;
- the originating context identity used for diagnostics and lifetime validation.

`ResolvedTemplate.Parameters` is established before the item enters `TemplateGroup` and is preserved through every group filter. This allows item-specific parsing, help, diagnostics, and invocation to share the exact schema, while invocation still receives values keyed only by canonical symbol name.

`TemplateInvocationRequest` contains output location, values keyed by canonical template symbol name, and file-conflict policy. It does not contain a template identity because selection has already occurred. `--force` is translated only into file-change conflict policy.

Invocation returns a func-owned result containing file changes, creation effects, and post-actions. The command renders that result through `IInteractionService`. TemplateEngine exceptions are translated only when they represent known creation outcomes; unexpected exceptions remain bugs and propagate.

**Alternative considered:** pass `ResolvedTemplate` back to `Templater.InvokeAsync`. This produces a procedural facade, permits mismatching templates and sessions, and makes each list item an anemic data carrier. It is rejected.

**Alternative considered:** expose `TemplateCreator` on the resolved object. This leaks the external engine API and lets callers bypass approved metadata and result translation. It is rejected.

### Host-specific metadata is loaded for the func host

Templates customize func aliases and visibility through `.template.config/func.host.json`. TemplateEngine discovers the available `*.host.json` files and selects the configuration for `FuncTemplateEngineHost.Identifier == "func"`. The integration's internal `FuncHostTemplateMetadataReader` reads that selected host configuration during candidate projection; the command layer never opens or parses the file.

The reader deserializes the root template visibility and the `symbolInfo` dictionary into func-owned metadata. The projector joins `symbolInfo` to TemplateEngine parameter definitions using the canonical symbol name as the key:

- `symbolInfo` keys are canonical template symbol names.
- `longName` and `shortName` omit dashes and define aliases only.
- `isHidden` hides a parameter from ordinary details while leaving it parseable.
- `alwaysShow` forces a parameter into combined help.
- root `IsHidden` hides a template from ordinary presentation.
- an empty `shortName` suppresses a short alias.

For every parameter symbol, projection produces an immutable func-owned definition:

```text
TemplateParameterDefinition
|- CanonicalName
|- LongName
|- ShortName
|- Description
|- DataType
|- Choices
|- DefaultValue
|- IsRequired
|- IsHidden
`- AlwaysShow
```

`LongName` is the host override when present and otherwise the canonical symbol name. `ShortName` is absent when it is missing or explicitly empty. The stored names omit command-line prefix characters; the command parser adds `--` or `-` when constructing options. Alias mapping never renames `CanonicalName`, which remains the key passed to template creation.

Only TemplateEngine parameter symbols are projected into this collection. Bind, computed, generated, and other internal symbols remain engine-managed template inputs and never become command options.

Malformed host metadata produces a candidate metadata diagnostic and prevents that candidate from becoming invocable. This avoids silently falling back to different aliases than the template author intended.

The resulting `TemplateParameterDefinition` collection is stored directly on each eligible `ResolvedTemplate` before it enters a group. Immutable `TemplateGroup` operations preserve the items and their collections. No phase reparses the host file or reconstructs symbol aliases.

The integration does not declare `dotnetcli` as a fallback host. A fallback host name can satisfy built-in host constraints and could incorrectly admit dotnet-only templates.

### Func context bind symbols are opt-in

Template authors can deliberately expose context inside template processing:

```json
{
  "symbols": {
    "FuncStack": {
      "type": "bind",
      "binding": "host:func:stack"
    },
    "FuncBundleVersion": {
      "type": "bind",
      "binding": "host:func:bundle-version"
    }
  }
}
```

The integration supplies only host defaults. It does not add symbols to template configurations. Bind symbols remain non-parameter values and therefore do not become dynamic CLI options or accept user overrides.

Compatibility constraints continue to query immutable host defaults directly. A generated-content symbol cannot become the authority for eligibility.

### Commands own strict two-stage parsing

The integration exposes candidate parameter definitions and func aliases; it does not construct the root command. A DI-registered `ITemplateArgumentParser` belongs to the `func new` orchestration layer. It depends on `System.CommandLine`, but neither `TemplateGroup` nor its candidates do. The thin command handler delegates the execution flow to this orchestration layer.

For each remaining `ResolvedTemplate`, `ITemplateArgumentParser` receives its projected `TemplateParameterDefinition` collection and the raw template argument tokens. It creates an ephemeral item-specific command, adds `--{LongName}` and the optional `-{ShortName}` aliases, configures type and choice validation, and parses the same token sequence independently. It returns template identity, diagnostics, values keyed by `CanonicalName`, and unresolved required parameters.

The `func new` command performs:

```text
stage A parse
  -> resolve command directory/project/bundle
  -> create Templater
  -> match and constrain candidates
  -> obtain item-specific parameter schemas
  -> stage B parse against group items
  -> narrow TemplateGroup
  -> command selects, errors, or prompts based on Count
  -> invoke selected ResolvedTemplate
```

Stage B maps aliases to canonical symbols. Unmatched options, missing option values, and invalid choices or types make a candidate argument-incompatible. A required parameter with no value or default leaves the candidate selectable but not invocable until the value is supplied, so the command can prompt for it. Parse errors are never swallowed and unmatched tokens are not accepted as successful defaults.

Successful item parse results narrow `TemplateGroup`; parsing itself does not mutate the group. When command policy selects an item, the orchestration layer uses only that item's canonical symbol-value mapping to create `TemplateInvocationRequest`. `ResolvedTemplate.InvokeAsync` consumes the mapping and does not parse command tokens again.

This design does not decide whether the template reference is ultimately positional or supplied through `--template`; that command grammar can evolve without changing the integration boundary.

### Func-owned discriminated outcomes cross the boundary

Catalog, group discovery, and invocation return typed outcomes rather than throwing for expected user states. Empty and multi-item groups are ordinary values interpreted by command policy. The supporting models provide enough detail for commands to distinguish:

- not found;
- wrong template type;
- restricted;
- constraint not evaluated or failed;
- ambiguous group;
- unsatisfied filters;
- multiple remaining templates;
- invalid arguments;
- unresolved required values;
- destructive file conflict;
- success.

Specific exceptions remain appropriate for programmer contract violations, cancellation, disposed scope usage, and unexpected engine failures. This gives commands exhaustive, testable rendering while preserving stack traces for defects.

## Risks / Trade-offs

- **[Resolved templates retain command-scoped engine objects]** -> Keep constructors internal, validate scope state on invocation, and dispose the facade only after invocation completes.
- **[Listing evaluates every template constraint]** -> Reuse one constraint manager and environment for the command; optimize only after measuring catalog size and latency.
- **[Func host parameters become an authoring contract]** -> Finalize accurate names now, keep them in the `func:` namespace, and test exact values through constraints and bind symbols.
- **[External engine metadata can be incomplete or malformed]** -> Fail closed for eligibility and return authoring diagnostics instead of silently selecting a candidate.
- **[Immutable group copies retain more metadata]** -> Store compact candidate snapshots and share immutable collections where practical.
- **[TemplateEngine upgrades may change host metadata or constraint behavior]** -> Centralize all external API adaptation inside the integration and cover it with focused tests against the pinned package version.

## Migration Plan

1. Introduce the context, catalog, group, resolution, invocation, and result models alongside the existing facade.
2. Move host construction and accurate func host defaults behind the command-scoped factory.
3. Adapt listing and existing resolver tests to the new constraint-aware projections.
4. Route template execution through `TemplateGroup` and `ResolvedTemplate.InvokeAsync`.
5. Remove public exposure of raw engine objects after all command callers migrate.

Rollback consists of reverting command callers to the previous facade while the new models remain unused; the shared settings hive and installed package registry format do not change.
