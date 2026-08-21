## Context

See `proposal.md` for motivation and `specs/func-init-quickstarts/spec.md` for the behavior contract.

The pending `func-init-execution` design assumes stack-first selection, one prospective stack and language, one generated Functions project, and one CLI-generated `.func/config.json` at the init target. Azure-Samples quickstarts can contain multiple independently configured Functions projects plus non-Functions projects, so those assumptions cannot describe the required experience.

The existing templating program already separates concerns:

- `template-engine-integration` owns installed catalog projection, constraint-aware resolution, primary outputs, dry-run, and invocation.
- `template-package-install` owns explicit `FuncTemplate` package lifecycle.
- `template-engine-constraints` will own workload constraint syntax, evaluation, diagnostics, and remediation.
- `template-engine-post-actions` will own the trusted Functions project configuration action.
- `azure-samples-template-pipeline` owns building and publishing quickstart packages.

This change composes those capabilities at the `func init` command boundary. It does not introduce a second package manager, remote search client, constraint evaluator, or post-action implementation.

## Goals / Non-Goals

**Goals:**

- Give basic templates and Azure-Samples quickstarts one installed project-template experience.
- Make interactive selection work when one template creates heterogeneous Functions projects.
- Keep restricted installed templates discoverable and actionable without implicit state changes.
- Preserve CLI ownership of project configuration serialization.
- Resolve project roots through TemplateEngine primary-output behavior so source and symbol renames remain authoritative.
- Preserve dry-run and preflight before destructive cleanup.
- Keep existing-project adoption separate from new solution scaffolding.

**Non-Goals:**

- Define workload constraint JSON, range semantics, feed lookup, or remediation algorithms.
- Define the configuration action identifier or its serialized argument syntax.
- Build the browse page, remote catalog, package search, or implicit install flow.
- Create a distinct quickstart or solution template type.
- Recursively adopt existing multi-project solutions.
- Roll back scaffolded content after a configuration I/O failure.
- Modify ordinary post-action policy beyond ordering it after mandatory project configuration.

## Decisions

### Quickstarts remain ordinary project templates

Every init-capable template uses standard `tags.type = project`. “Quickstart” describes source and curation, not an execution type. One selected template can generate:

```text
target/
|- src/api/          Functions project: Node / TypeScript
|- src/processor/    Functions project: Python / Python
|- web/              non-Functions project
`- infra/            non-project content
```

Only declared Functions project configuration actions participate in Func topology. Other generated content is opaque to init.

**Alternative considered:** use TemplateEngine `tags.type = solution`. This would create a second init template category without changing the invocation mechanism and would force users and authors to distinguish templates based on project count. It is rejected.

**Alternative considered:** introduce a `quickstart` type. Package provenance and curation do not change TemplateEngine execution semantics. It is rejected.

### Package installation remains explicit

`func init` queries only the installed template catalog. An unknown `--template` reference produces:

```text
Template '<reference>' is not installed.
Browse available templates: <Functions-owned URL>
Install a package with: func new install <package>
```

Without a remote discovery index, an uninstalled short name cannot be mapped reliably to an exact package ID. The browse experience supplies package-specific installation instructions.

A future trusted first-party implicit flow would require a signed trust index, source policy, consent behavior, offline behavior, and lifecycle ownership. It remains deferred rather than being approximated from package naming.

**Alternative considered:** automatically query and install NuGet.org after an unknown reference. This makes init mutate global package state, introduces trust ambiguity, and couples command execution to remote search. It is rejected initially.

### The browse URL is an indirection contract

The CLI displays one stable Functions-owned URL, preferably through a redirect controlled by the Functions team. The destination can later be a dedicated gallery, documentation page, or generated discovery site.

The awesome-azd gallery is a visual precedent but not the authoritative destination: its entries are azd-curated GitHub repositories, its source field requires GitHub URLs, and it does not represent `FuncTemplate` package IDs or install commands. This change does not depend on that site's schema or availability.

### Template-first selection replaces stack-first orchestration

The interactive flow becomes:

```text
load installed project-template groups
  -> project unavailable states
  -> display picker and browse URL
  -> select one template group
  -> resolve template variant and parameters
  -> resolve active project configuration actions
  -> apply whole-template stack/language filters
  -> evaluate final constraints
  -> preflight effects
```

Template selection comes first because no singular stack or language can represent a heterogeneous template. Basic template groups can still contain stack/language variants and expose parameters after selection.

The context-free catalog projects enough trusted configuration-action metadata to describe potential stack/language requirements and unavailable state without invoking the template. Authoritative active actions are resolved with the selected candidate and final parameters before scaffolding.

**Alternative considered:** keep the stack/language/template prompt order and branch to a separate quickstart picker. This fragments installed project templates and makes package type determine command UX. It is rejected.

### Restricted templates are visible but unavailable

The picker contains eligible and restricted installed groups:

```text
Select a project template:
> Basic Node project
  Event processing solution       unavailable: requires workloads
  Python OpenAI quickstart

Browse more templates: <URL>
```

Restricted entries cannot be selected. Concise summaries fit the picker; detailed constraint diagnostics and calls to action are rendered after or alongside it using structured results from the constraint system. An explicitly requested restricted template bypasses the picker but produces the same detailed remediation.

Func does not infer package commands from constraint text. `template-engine-constraints` owns the distinction between missing, outdated, incompatible, unevaluable, and failed constraints and supplies any appropriate call to action.

**Alternative considered:** hide restricted templates. Users would not know an installed quickstart exists or how to unblock it. It is rejected.

### One aggregate workload constraint can cover heterogeneous projects

Project templates use the workload constraint capability defined elsewhere for all required stack, host, bundle, and related workload availability. Func init requires an eligible result before cleanup or scaffolding.

This change deliberately does not require an existing-project bundle constraint. A new project has no resolved bundle identity or version; bundle capability needed to use the generated solution is represented as workload availability.

The exact declaration shape and version policy remain in `template-engine-constraints`. The init contract consumes only:

```text
TemplateConstraintOutcome
|- Eligible
`- Restricted
   |- Summary
   |- Diagnostics
   `- CallsToAction
```

### Configuration actions are the project topology

Every generated Functions project is represented by one mandatory trusted configuration action. Conceptually, each action supplies:

```text
FunctionsProjectConfiguration
|- PrimaryOutputReference
|- Stack
`- Language
```

The primary output is any file located directly in the project root. TemplateEngine resolves its final relative path after `sourceName`, symbol `fileRename`, explicit rename, source target, and conditions. The action's resolved output path identifies:

```text
project root = parent(resolved primary output)
config path  = project root/.func/config.json
```

The author can choose `host.json`, a project file, `package.json`, or another stable root file. Func does not impose a stack-specific filename.

The exact action ID, argument representation, rename propagation, and projection model belong to `template-engine-post-actions`. That capability must make the configuration action distinguishable from ordinary actions and expose its validated metadata before invocation.

**Alternative considered:** put stack/language properties directly on `primaryOutputs`. TemplateEngine accepts unknown JSON properties but drops them from `PrimaryOutputModel` and `ICreationResult`; Func would need a second raw parser and fragile correlation. It is rejected.

**Alternative considered:** add a topology map to `func.host.json`. Authors would declare constraints, primary outputs, and a second mapping back to those outputs. It is rejected as redundant.

**Alternative considered:** include `.func/config.json` in template content. This handles paths naturally but couples packages to the CLI config schema and permits templates to author CLI-owned state. It is rejected in favor of a trusted action.

### Configuration declarations are preflighted before scaffolding

After candidate parameters are complete, init resolves active configuration actions and primary outputs during TemplateEngine effects evaluation. Preflight rejects:

- no active configuration action;
- unsupported or non-trusted action identity;
- optional or continue-on-error configuration behavior;
- missing, ambiguous, or inactive primary-output reference;
- output outside the target;
- output whose parent cannot be a project root;
- empty or non-canonical stack/language;
- duplicate resolved project roots;
- template file effects targeting `.func/config.json`;
- configuration output collisions with another planned effect.

This validation occurs before `--force` cleanup. It validates declaration and planned paths, not the future filesystem contents.

No temporary rendering is performed. The actual primary-output file is verified after scaffolding before configuration is written.

### Stack and language are whole-template filters

After active configuration actions are known:

```text
--stack S
  -> every active project.Stack equals S

--language L
  -> every active project.Language equals L
```

A heterogeneous template is valid when those singular filters are absent. Supplying a filter is an assertion about the whole generated topology, not a request to rewrite one or all action values.

For conditional projects, only active configuration actions participate after final template parameter resolution. This may require completing template parameters before a filter can be authoritatively evaluated.

Standard TemplateEngine language tags remain useful for homogeneous variants but are not required to encode a mixed topology. Configuration actions are authoritative for each generated project's canonical values.

**Alternative considered:** match when any project satisfies the filter. A user asking for a Node project could receive a Node/Python solution, making the explicit filter misleading. It is rejected.

### Mixed templates do not fabricate singular host context

The template execution context always supplies target working directory and prospective solution root. Stack and language host context are:

```text
all active projects share value -> expose common canonical value
active projects differ          -> value unavailable
```

Workload constraints do not depend on a fabricated singular stack or language. A mixed template that requires one of those singular host values in another constraint or bind symbol fails closed or receives an unavailable value according to the integration contract.

This preserves the invariant that host context reflects true resolved state.

### Required configuration is a finalization phase

Actual execution is ordered:

```text
Prepare
  validate candidate, actions, constraints, and combined effects
  clear non-git target content when --force is authorized

Template
  invoke selected project template

Finalize
  for each configuration action in declared order:
    verify resolved primary-output file exists
    compute parent project root
    validate installed stack recognizes canonical language
    atomically write .func/config.json

Post
  execute ordinary post-actions in declared order
```

Configuration actions use the current CLI serializer and are mandatory. They cannot be skipped by future ordinary-action consent policy. Ordinary post-actions never run when finalization is incomplete.

Dry-run resolves the same primary outputs and action metadata, adds planned `.func/config.json` effects, and reports ordinary actions without executing any action.

**Alternative considered:** implement configuration as an ordinary action. Optional/continue-on-error semantics and post-action consent could leave a nominally successful project without required CLI state. It is rejected.

### Finalization failure preserves generated content

All configuration declarations and expected paths are preflighted, but I/O can still fail after template creation. The command stops at the first failed configuration write, skips remaining ordinary post-actions, reports the failed project, and leaves:

- generated template files;
- any configurations already written;
- no synthetic rollback of source-controlled output.

This matches the existing planned partial-initialization boundary while extending it to multiple projects. Each individual configuration file is written atomically.

### Adoption remains a separate root-project path

Existing adoption and healing do not list or invoke templates and do not execute configuration actions. Supplying `--template` without `--force` remains an error on those paths.

This change does not recursively scan a solution for `host.json` or `.func/config.json`. New multi-project topology is explicit in template actions; inferring topology for arbitrary existing directories is a separate problem.

### Package authoring must satisfy the runtime contract

The Azure-Samples packaging pipeline must produce templates that include:

- `tags.type = project`;
- required workload constraints;
- at least one project primary output;
- one trusted configuration action per Functions project;
- canonical stack and language action values.

For a repository lacking authored template configuration, reviewed onboarding `template.projects` metadata can declare one or more static Functions project roots with canonical stack and language. The packager uses each root `host.json` as a primary output and adds the corresponding configuration action.

Parameterized or conditional project topology cannot be expressed by pipeline onboarding metadata and requires authored template configuration and project actions. Authored root `.template.config/template.json` and onboarding `template` metadata are mutually exclusive. The packaging pipeline loads and dry-runs either resulting template through the same validation path.

## Risks / Trade-offs

- **[Template-first flow revises the pending init execution design]** -> Treat this focused change as authoritative for selection order and restack/reconcile the companion specification before implementation.
- **[Configuration action metadata duplicates project facts already present in source]** -> Keep only the stable primary-output reference, stack, and language; avoid a separate topology manifest.
- **[Conditional topology delays whole-template filtering]** -> Resolve required parameters and active actions before applying the authoritative filter.
- **[Mixed templates cannot expose singular stack/language context]** -> Fail closed rather than selecting an arbitrary project.
- **[Finalization can fail after files are generated]** -> Preflight every declaration and path, use atomic writes, skip ordinary actions, and report partial initialization without destructive rollback.
- **[Restricted picker entries can overwhelm the prompt]** -> Show concise summaries in the picker and render detailed calls to action separately.
- **[Browse URL destination is not yet designed]** -> Use a Functions-owned redirect so the CLI contract remains stable.
- **[Synthesized quickstarts require reviewed project topology]** -> Require static project roots, stacks, and languages in onboarding and use authored template configuration for parameterized or conditional topology.

## Migration Plan

1. Complete the workload constraint outcome and required configuration-action contracts in their focused changes.
2. Extend template catalog and resolved-candidate projections with project configuration action metadata and unavailable summaries.
3. Reconcile `func-init-execution` by replacing stack-first selection and CLI companion configuration generation with the template-first and action-finalization contracts.
4. Migrate built-in project templates to declare primary outputs, workload requirements, and configuration actions.
5. Update Azure-Samples package synthesis and authored-template validation to require the same contract.
6. Add installed quickstart rendering, browse guidance, restricted entries, whole-template filters, and multi-project success output.
7. Exercise the flow with homogeneous, heterogeneous, conditional, restricted, dry-run, forced, and partial-failure fixtures.
8. Enable the experience only after default project templates and representative Azure-Samples packages satisfy the authoring contract.

Rollback restores the previous init selection and CLI-owned single-root configuration step. Installed template packages remain managed by the same template package lifecycle, but packages relying only on multi-project configuration actions will not be fully usable by the prior init flow.
