## Context

See `proposal.md` for motivation and `specs/func-init-execution/spec.md` for the behavioral contract.

`InitCommand` currently owns project-state detection, adoption and healing, stack and language prompts, destructive cleanup, CLI configuration writes, bundle warnings, workload option registration, and direct dispatch to `IProjectInitializer.InitializeAsync`. `IProjectInitializer` combines discoverable stack metadata with stack-specific command options and filesystem behavior. Implementations either write project files directly or launch another template CLI, so project creation does not share the func TemplateEngine environment or package registry.

The `template-engine-integration` change supplies command-scoped `Templater`, context-aware constraints, immutable `TemplateGroup`, projected parameter metadata, and self-invoking `ResolvedTemplate`. The `template-engine-post-actions` change supplies the trusted Functions project configuration action and resolved action metadata. The `func-new-execution` change supplies the strict two-stage parser and alias rules that both template commands need. Unlike `func new`, init cannot resolve an existing project before selecting a template. It selects from context-free project-template metadata and only then constructs a prospective project context. `func-init-quickstarts` is authoritative for template-first selection, whole-template filters, and multi-project topology, and this design follows it.

## Goals / Non-Goals

**Goals:**

- Separate installed stack capability metadata from project generation behavior.
- Select a project template first and apply stack and language as whole-template filters.
- Create exactly one context-bearing `Templater` after the project template is selected.
- Preserve adoption and healing without forcing project templates into existing-project workflows.
- Make project-template invocation expose enough resolved primary-output and configuration-action metadata to finalize CLI project configuration.
- Share template argument semantics with `func new` rather than maintaining a second dynamic parser.
- Keep expected ambiguity, incompatibility, and missing-package states explicit and testable.

**Non-Goals:**

- Define a new policy for approving or suppressing template post-actions.
- Resolve extension bundle identity or version before a new project's `host.json` exists.
- Preserve workload-owned project scaffolding as a fallback when templates are unavailable.
- Define the trusted action identifier or serialized argument shape owned by `template-engine-post-actions`.
- Make project-template plus CLI configuration writes transactionally atomic across unexpected filesystem failures.
- Change the adoption and healing outcomes beyond moving them to shared stack metadata and configuration services.

## Decisions

### Project state is resolved before template selection

Init retains the current state boundary:

```text
target directory
  |- fully initialized, no --force -> refuse
  |- adoptable or healable, no --force -> adoption/healing path
  `- empty or --force -> project-template path
```

Adoption and healing resolve stack and language from existing files and installed stack metadata, then create or merge `.func/config.json`. They do not load project-template candidates or create a `Templater`. Supplying `--template` on those paths is rejected because ignoring an explicit template request would be misleading; `--force` moves the command onto reinitialization.

The target directory continues to use the optional positional `[path]` argument and defaults to process current directory. It is both the future project root and TemplateEngine working directory.

**Alternative considered:** route every init invocation through a project template. Adoption exists specifically to avoid overwriting an existing project, while healing changes only CLI metadata. It is rejected.

### `IProjectStack` replaces behavioral initializers

Workload assemblies register metadata-only `IProjectStack` implementations:

```text
IProjectStack
|- Stack
|- DisplayName
|- WorkerRuntimeAliases
|- SupportedLanguages
`- SupportedLanguageAliases
```

`InitializeAsync`, `GetInitOptions`, and `DefaultFunctionNameValidator` are removed. Project templates own their parameter definitions and validation. Bundle channel, target framework, package restore controls, and other former workload options become template symbols projected through `func.host.json`.

An `InstalledProjectStackCatalog` validates unique stack IDs, canonicalizes language aliases, and preserves a one-to-many canonical-language-to-stack mapping. It replaces the current resolver's first-registration-wins language map.

The refactor applies to every stack workload in one migration. Retaining `IProjectInitializer` as a temporary creation fallback would allow behavior to vary based on which templates happen to be installed and is rejected.

Project templates are expected to be delivered in a companion `FuncTemplate` package rather than embedded in the stack workload assembly. The mechanism that identifies, acquires, and installs that companion package together with its stack workload is intentionally deferred to a later design.

**Alternative considered:** keep `IProjectInitializer` for metadata while no longer calling `InitializeAsync`. Its name and remaining methods would misrepresent the contract and encourage new workload-owned scaffolding. It is rejected.

### Context-free metadata enables template-first selection

`func init` must inspect template type, group, identity, aliases, language, visibility, precedence, and projected configuration actions before any project context exists. Constraint-aware `Templater.ListAsync` cannot serve that purpose because its host defaults are immutable and compatibility constraints require the final context.

The integration therefore exposes a context-free catalog snapshot backed by the shared func settings hive:

```text
TemplateMetadataCatalog
  |- List(TemplateType.Project)
  `- ResolveReference(reference, TemplateType.Project)
```

Catalog entries are descriptors, not `ResolvedTemplate` instances. They do not evaluate context-dependent constraints, bind symbols, defaults, or invocation readiness. The catalog is safe to cache or reuse because it contains no command project context; package lifecycle changes invalidate its snapshot.

After template selection, init creates one `Templater` and resolves the chosen reference again. That second resolution is authoritative and can surface package changes, constraints, malformed host metadata, or another context-dependent restriction.

**Alternative considered:** create a partial-context `Templater`, inspect templates, dispose it, then create another after selection. This violates the one execution-environment-per-command design and repeats engine initialization. It is rejected.

**Alternative considered:** evaluate every template once for every installed stack and language. Constraint instances capture environments, making this expensive and inconsistent with immutable command context. It is rejected.

### Selection is template-first

Init follows the template-first flow defined by `func-init-quickstarts`:

```text
select project-template group
  -> resolve template variant and parameters
  -> resolve active configuration actions
  -> check stack and language filters against every active project
```

Each generated project's stack and language come from its configuration action. `--stack` and `--language` are assertions about the whole template, so every active project must match. A mixed template remains valid when neither filter is supplied. Filter values match canonical names and `IProjectStack` aliases case-insensitively.

Explicit filters apply regardless of their order on the command line, and invalid explicit values fail without prompting for substitutes. The authoritative check runs against active actions once parameters resolve. Whether filters also narrow the template picker earlier, using the catalog's projected configuration actions, is left to the preparation prototype.

At each choice point:

```text
0 values -> targeted incompatibility or missing-package result
1 value  -> automatic selection
>1       -> prompt, or non-interactive ambiguity result
```

The special name `basic` has no selection semantics. A sole applicable template group is automatically selected; multiple groups prompt even when one is named basic.

Standard `language` tags remain optional metadata for homogeneous variants. No func-specific stack tag is introduced.

**Alternative considered:** select stack, then language, then template from the intersection of installed stack languages and template language tags. No singular stack or language can represent a heterogeneous template. It is rejected.

### The static command owns only init-level options

`InitCommand` retains:

- optional positional `[path]`;
- `--stack` / `-s`;
- `--language` / `-l`;
- `--template` / `-t`;
- `--name` / `-n`;
- `--force`;
- `--non-interactive`;
- `--dry-run`;
- plain/JSON output selection;
- help and global options.

Workload-specific options are removed. As with `func new`, Stage A preserves unknown template tokens only until a project-template candidate schema is available. No unmatched token can reach a successful invocation without strict Stage B parsing.

An immutable `InitExecutionRequest` carries the static values and raw template tokens into orchestration. The command class handles binding and delegates once.

### A prospective context precedes the command-scoped `Templater`

Once a project template is selected, init creates:

```text
TemplateEngineContext
|- CommandDirectory = target directory
|- Project
|  |- RootDirectory = target directory
|  |- Stack = common stack of the active projects, or unavailable
|  `- Language = common language of the active projects, or unavailable
`- Bundle = unavailable
```

The project value is prospective: the directory may be empty and no project files are required. Stack and language are exposed only when every active project shares them, so a mixed template never sees a fabricated singular value. Host defaults still expose `WorkingDirectory`, `func:project-root`, and any available `func:stack` and `func:language`, allowing project templates to use the same bind and constraint mechanisms as item templates.

Bundle ID and version are absent. Project templates choose or generate bundle configuration through template symbols and content; they must not require an already resolved bundle constraint.

One `Templater` is created from this snapshot and reused for authoritative group resolution, candidate parsing, required-value completion, dry-run, and invocation.

### Strict parsing is shared with `func new`

`ITemplateArgumentParser` and reserved-alias assignment are shared services rather than duplicated init implementations. Init supplies its own reserved static aliases, but receives the same behavior:

- projected canonical symbol definitions;
- func host `longName` and optional `shortName`;
- `--param:<canonical-name>` and `-p:<short-name>` collision fallbacks;
- no generated short names;
- independent parsing of the raw token tail for every candidate;
- invalid explicit input separated from unresolved required input;
- final canonical reparse after prompted values;
- argument compatibility before precedence.

After authoritative context resolution, the runner applies explicit filters, candidate argument compatibility, and highest remaining precedence. One surviving template invokes directly; genuine remaining ambiguity prompts or fails non-interactively.

Required visible template symbols are prompted only when unresolved. Optional values use template defaults. Hidden required symbols without defaults are authoring failures.

### Configuration actions define CLI project finalization

`ResolvedTemplate.InvokeAsync` remains the only TemplateEngine creation entry point, but `.func/config.json` is not produced by a hidden companion template or synthesized second creation operation. Each generated Functions project is represented by one mandatory trusted configuration action:

```text
FunctionsProjectConfiguration
|- PrimaryOutputReference
|- Stack
`- Language
```

The referenced primary output is a file located directly in the Functions project root. TemplateEngine resolves its final relative path after source targets, `sourceName`, explicit renames, symbol `fileRename`, and conditions:

```text
project root = parent(resolved primary output)
config path  = project root/.func/config.json
```

The action carries canonical stack and language. Values can differ between projects, and must match `--stack` or `--language` when supplied. The exact action ID, serialized argument schema, rename propagation, and TemplateEngine projection belong to `template-engine-post-actions`.

Project template content cannot create or modify `.func/config.json` directly. The trusted action is template-declared topology but CLI-owned behavior: Func validates the declaration, computes the destination, and serializes the current CLI configuration schema.

**Alternative considered:** add stack and language properties to `primaryOutputs`. TemplateEngine discards unknown properties from its public primary-output result, forcing Func to parse and correlate raw template JSON. It is rejected.

**Alternative considered:** retain a hidden companion template or direct synthetic creation step. That duplicates topology outside the authored template, assumes one target root, and turns configuration into an unrelated second effect source. It is rejected.

### Configuration declarations and effects are preflighted

After candidate parameters are complete, init resolves active configuration actions and primary outputs during TemplateEngine effects evaluation. Before target modification, preflight rejects:

- no active trusted configuration action;
- optional or continue-on-error configuration behavior;
- a missing, ambiguous, inactive, or non-file primary-output reference;
- a resolved primary output outside the target;
- empty or non-canonical stack/language, or a value that conflicts with an explicit filter;
- a stack that is not installed, or a language its stack does not support;
- duplicate resolved project roots;
- project template file effects targeting `.func/config.json`;
- configuration output collisions with any other planned effect.

Dry-run projects one planned `.func/config.json` write for each active action without executing the action. It preserves resolved primary outputs and reports ordinary post-actions separately.

Actual execution is ordered:

```text
preflight template, actions, and combined paths
create project template
for each configuration action in declared order:
  verify resolved primary-output file exists
  derive its parent project root
  atomically write .func/config.json
run ordinary post-actions in declared order
```

Configuration actions are mandatory and do not participate in future ordinary-action consent policy. If a configuration write fails after project creation, init stops remaining finalization and ordinary post-actions, reports partial initialization, and preserves generated files and successful prior writes.

### Force cleanup is an ordered command effect

`--force` preserves the current destructive semantics: all target content except `.git` is removed before actual template creation. Interactive execution warns and confirms; non-interactive `--force` is itself authorization.

Cleanup is command policy outside `ResolvedTemplate`. Dry-run prepends planned cleanup deletions to the combined project invocation effects:

```text
Prepare   delete existing non-git content
Template  create or modify project files
Finalize  execute required configuration actions
Post      report ordinary post-actions
```

The func-owned preview preserves phase and source so deletion followed by recreation is not flattened into misleading independent output. TemplateEngine effects remain unchanged internally; the init renderer composes cleanup and invocation results.

`--force` never bypasses stack, type, filter, constraint, parsing, or ambiguity checks. Selection and combined preflight happen before destructive cleanup.

### Config persistence is mandatory

`.func/config.json` is required CLI state, not a best-effort hint. Init reports success only after every active configuration action succeeds. Both stack and language are always written, replacing the current single-language omission behavior and preventing a later stack expansion from turning an initialized project into a partial state.

Adoption and healing use the same canonical configuration serialization rules but remain command-owned because no project `ResolvedTemplate` exists on those paths.

Actual configuration writes must be atomic at the file level. A temporary sibling and replace/move avoids truncated JSON.

### Expected outcomes remain command-renderable

Init orchestration retains func-owned outcomes for:

- no installed stacks;
- duplicate stack ownership;
- unknown or incompatible explicit stack/language/template;
- no applicable project template;
- wrong template type;
- template metadata authoring failure;
- non-interactive ambiguity;
- invalid template arguments;
- restricted template;
- declined destructive confirmation;
- dry-run success;
- project creation success;
- partial creation after configuration failure.

No applicable template falls back to workload scaffolding. Diagnostics name any supplied filters, show the browse URL, and direct the user to `func new install`.

Known outcomes are rendered through `IInteractionService` or wrapped at the command boundary using the repository's `GracefulException` policy. Unexpected integration defects propagate. Cancellation is honored before cleanup and through catalog access, prompting, preflight, creation, configuration, and post-actions.

## Risks / Trade-offs

- **[Template-first selection needs metadata before context]** -> Keep catalog descriptors context-free and perform authoritative resolution again through one final context-bound `Templater`.
- **[Active projects can depend on template parameters, but context is fixed when the session starts]** -> Prototype the preparation sequence before implementation, including whether filters narrow the picker early. It must keep one immutable session and never fabricate singular values.
- **[Every stack now depends on installed project templates]** -> Provide explicit package guidance and migrate default project template packages before removing initializer fallback.
- **[Project creation and configuration finalization are sequential]** -> Preflight every action and combined path, use atomic writes, and report partial completion without destructive rollback.
- **[Actual invocation evaluates effects more than once]** -> Prefer correctness and combined preflight; optimize only if TemplateEngine exposes a safe reusable creation plan.
- **[Force dry-run evaluates against files that actual cleanup removes]** -> Preserve ordered cleanup effects and reconcile template changes following planned deletions in the func-owned renderer.
- **[Post-actions run without a new approval policy]** -> Keep this as explicit initial behavior and isolate execution so a future policy can change without altering template selection.
- **[`IProjectStack` is a breaking workload contract]** -> Migrate all in-repository workloads together and fail clearly for incompatible external workload assemblies.

## Migration Plan

1. Add context-free project-template metadata and `TemplateType.Project` resolution to the template integration.
2. Add trusted project configuration action projection, primary-output resolution, and planned configuration effects while preserving item behavior.
3. Introduce `IProjectStack` and migrate stack registrations, aliases, and tests from `IProjectInitializer`.
4. Package and install project templates covering every supported stack and canonical language.
5. Introduce template-first selection and whole-template stack and language filters.
6. Reuse the shared strict template parser and prompt services from `func-new-execution`.
7. Route empty and forced initialization through prospective context, `Templater`, and project `ResolvedTemplate`.
8. Route adoption and healing through `IProjectStack` metadata and canonical configuration serialization.
9. Remove `InitContext`, `IInitOptionRegistry`, workload-contributed options, and workload project-generation code after every stack is template-backed.
10. Update help, documentation, package guidance, and dry-run rendering.

During migration, the new path can be exercised with template-backed test stacks before switching production stack registrations. The final switch must remove initializer fallback atomically so missing template packages fail consistently rather than changing scaffolding engines. Rollback restores workload initializer registration and the previous init runner; installed template packages and the shared func template hive remain compatible.
