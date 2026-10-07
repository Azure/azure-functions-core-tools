## Context

See `proposal.md` for motivation and `specs/func-init-quickstarts/spec.md` for the behavior contract.

The `func-init-execution` design originally assumed stack-first selection, one prospective stack and language, one generated Functions project, and one CLI-generated `.func/config.json` at the init target. Azure-Samples quickstarts can contain multiple independently configured Functions projects plus non-Functions projects, so those assumptions cannot describe the required experience.

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

### The installed-template experience replaces func quickstart

The existing v5 `func quickstart` command is replaced by `func init`, not retained as a second scaffolding engine or introduced as an init subcommand. A basic project and a curated sample both use `func init --template <name>` or the installed project-template picker. Individual function templates remain under `func new`.

This follows the [original quickstart proposal's handoff to the template designs](https://github.com/Azure/azure-functions-core-tools/pull/5026#issuecomment-5298569474). Consolidation changes command syntax, but must not introduce a separate manual template-install step for the curated content users already receive through their installed stack.

Today, a supported stack registers its quickstart provider, the CLI reads the CDN manifest, and scaffolding fetches the selected tagged repository snapshot. The [current manifest](https://github.com/Azure/azure-functions-templates/blob/dev/Functions.Templates/Template-Manifest/manifest.json) can register independent samples from different folders of one repository. For example, the Python connectors repository exposes [Office 365](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/office365App) and [SharePoint](https://github.com/Azure-Samples/functions-connectors-python/tree/v1.0.1/sharepointApp) as separate choices. Their replacements must remain independently selectable and generate only their intended content, not become one combined solution.

An explicit stack installation, whether through setup or the stack install command, must acquire the approved basic-template companions and acquire curated quickstart companions by default. A quickstart opt-out skips that acquisition without removing basic templates or packages already installed. Companion identification, package grouping, sources, versions, upgrades, ownership, cancellation, and partial failure belong to the companion-template acquisition design. The publishing design must cover independently selectable templates and repository subfolder scopes before the command switch.

The migration inventory pins the existing test manifest and maps every entry supported by the current CLI to its source revision and content scope, replacement package, template identity, and short name. It records intentional naming changes and source releases or authoring metadata that still need onboarding work. Branch-tracking entries currently filtered out of the CLI are not treated as working scenarios to preserve. A singular manifest language is not proof of single-stack source topology; declared Functions project requirements remain authoritative in the replacement.

The replacement must also cover discovery, not just file generation. Installed selection and functioning browse or search must let users find samples, inspect their purpose and included content, and narrow by the relevant language, resource, and infrastructure-as-code metadata without guessing package IDs. The discovery design owns the interface and its catalog. It may use a browse experience rather than reproduce every old CLI flag, but a placeholder URL or parameter help alone does not satisfy this gate.

Manifest caching currently gives curated entries periodic refresh, whereas installed packages execute their installed versions. Package update notifications and automatic refresh are outside this migration decision. No equivalent freshness guarantee or silent workload upgrade is implied.

**Alternative considered:** retain `func quickstart` permanently as a shortcut. That leaves two public creation workflows and a separate repository-fetch contract after the template runtime covers their intended content. The old command remains during migration, but is removed with the final switch rather than becoming a permanent alias.

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
load installed project-template groups with eligibility and declared projects
  -> narrow groups by stack/language filters
  -> display picker, unavailable groups included, and browse URL
  -> select one template group
  -> resolve template variant and parameters
  -> resolve active project configuration actions
  -> apply whole-template stack/language filters
  -> preflight effects
```

Template selection comes first because no singular stack or language can represent a heterogeneous template. Basic template groups can still contain stack/language variants and expose parameters after selection.

The catalog comes from the command's own session. It projects enough trusted configuration-action metadata to describe each template's potential stacks and languages, and its constraint results give the unavailable state, without invoking the template. Authoritative active actions are resolved with the selected candidate and final parameters before scaffolding.

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

When one eligible group remains beside unavailable ones, the picker is still shown so the unavailable ones stay visible. When no eligible group remains, init skips the picker. It reports that no installed template can be used, shows each distinct call to action once, adds the browse URL, and exits without modifying the target. The missing piece is usually a workload rather than a template, so the calls to action matter more than generic install guidance.

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

Before a template is selected, the filters narrow the picker to templates that can still match. Every unconditional project must match all supplied filters, and at least one project must match them all. A conditional project that uses another value does not remove the template, because parameters can still turn it off. If it stays on, the check after parameters rejects the template and names that project. A group remains in the picker when any of its variants can still match, and the same rule narrows the variants of the selected group.

Standard TemplateEngine language tags remain useful for homogeneous variants but are not required to encode a mixed topology. Configuration actions are authoritative for each generated project's canonical values.

**Alternative considered:** match when any project satisfies the filter. A user asking for a Node project could receive a Node/Python solution, making the explicit filter misleading. It is rejected.

### Init's context has no stack or language

The template execution context supplies the target working directory and solution root. It never supplies a stack or language, whether a template's projects share one or not. Configuration actions already declare each project's values, and the context is fixed before a template is selected, when those values are not known.

Workload constraints do not depend on a singular stack or language. A template that binds `func:stack` or `func:language` receives its bind default.

This preserves the invariant that host context reflects true resolved state.

### Required configuration is a finalization phase

Actual execution is ordered:

```text
Prepare
  validate candidate, actions, constraints, and combined effects
  validate each action's installed stack recognizes its canonical language
  clear non-git target content when --force is authorized

Template
  invoke selected project template

Finalize
  for each configuration action in declared order:
    verify resolved primary-output file exists
    compute parent project root
    atomically write .func/config.json

Post
  execute ordinary post-actions in declared order
```

Stack and language support is checked before cleanup, so `--force` never clears a target for a project init cannot configure.

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

For a repository lacking authored template configuration, the repository-owned `.github/azure-functions-template.yaml` synthesis descriptor can declare one or more static Functions project roots with canonical stack and language. The packager uses each root `host.json` as a primary output and adds the corresponding configuration action.

Parameterized or conditional project topology cannot be expressed by the synthesis descriptor and requires authored template configuration and project actions. Authored root `.template.config/template.json` and `.github/azure-functions-template.yaml` are mutually exclusive. The packaging pipeline loads and dry-runs either resulting template through the same validation path.

## Risks / Trade-offs

- **[Template-first flow revises the pending init execution design]** -> Treat this focused change as authoritative for selection order and restack/reconcile the companion specification before implementation.
- **[Configuration action metadata duplicates project facts already present in source]** -> Keep only the stable primary-output reference, stack, and language; avoid a separate topology manifest.
- **[Conditional topology delays whole-template filtering]** -> Resolve required parameters and active actions before applying the authoritative filter.
- **[Project templates cannot read a stack or language from the host]** -> Configuration actions declare both for each project, and a template that binds them sets a default.
- **[Finalization can fail after files are generated]** -> Preflight every declaration and path, use atomic writes, skip ordinary actions, and report partial initialization without destructive rollback.
- **[Restricted picker entries can overwhelm the prompt]** -> Show concise summaries in the picker and render detailed calls to action separately.
- **[Browse URL destination is not yet designed]** -> Use a Functions-owned redirect so the CLI contract remains stable.
- **[Synthesized quickstarts require reviewed project topology]** -> Require static project roots, stacks, and languages in the repository-owned synthesis descriptor and use authored template configuration for parameterized or conditional topology.

## Migration Plan

1. Complete the workload constraint outcome and required configuration-action contracts in their focused changes.
2. Extend template catalog and resolved-candidate projections with project configuration action metadata and unavailable summaries.
3. Reconcile `func-init-execution` by replacing stack-first selection and CLI companion configuration generation with the template-first and action-finalization contracts.
4. Migrate built-in project templates to declare primary outputs, workload requirements, and configuration actions.
5. Update Azure-Samples package synthesis and authored-template validation to require the same contract.
6. Add installed quickstart rendering, browse guidance, restricted entries, whole-template filters, and multi-project success output.
7. Exercise the flow with homogeneous, heterogeneous, conditional, restricted, dry-run, forced, and partial-failure fixtures.
8. Enable the experience only after default project templates and representative Azure-Samples packages satisfy the authoring contract.

Keep the existing `func quickstart` command until the supported-entry inventory is covered, reviewed packages are published, default companion acquisition works on fresh and upgraded installs, discovery and migration guidance are usable, and replacement regression tests pass. Agree the content and publication handoff with the existing quickstart and publishing owners before removal. The command-switch change removes the old command and its unused provider and fetch dispatch in the same change that enables the qualified replacement. It does not remove the shared catalog from other consumers such as editor galleries.

Rollback restores the previous init selection and CLI-owned single-root configuration step. Installed template packages remain managed by the same template package lifecycle, but packages relying only on multi-project configuration actions will not be fully usable by the prior init flow.
