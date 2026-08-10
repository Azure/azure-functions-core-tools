## Context

See `proposal.md` for motivation and `specs/templating-system/spec.md` for the coordination contract.

Four focused templating changes currently have complete planning artifacts:

| Change | Responsibility | Artifact status |
|---|---|---|
| `template-engine-integration` | Host, context, engine lifetime, catalog, resolution, and invocation boundary | Complete |
| `template-package-install` | Template package install, update, uninstall, source, ownership, and replacement safety | Complete |
| `func-new-execution` | Item-template selection, parsing, prompting, dry-run, and invocation | Complete |
| `func-init-execution` | Stack/language/project-template selection, initialization, adoption boundaries, and configuration effects | Complete |

Six identified areas do not yet have focused changes:

| Planned change | Responsibility | Artifact status |
|---|---|---|
| `template-engine-constraints` | Func-specific compatibility constraints such as extension bundles and project stacks | Planned |
| `template-engine-post-actions` | Func-supported actions such as npm install, NuGet restore, pip install, and package addition | Planned |
| `template-engine-bind-sources` | Func-supported value sources such as MSBuild properties and npm package properties | Planned |
| `func-new-search` | NuGet feed scanning, `FuncTemplate` discovery manifests, CDN publication, and `func new search` consumption | Planned |
| `azure-samples-template-pipeline` | Seamless conversion and release of Azure-Samples repositories as packages accepted by `func new install` | Planned |
| `func-init-quickstarts` | First-class `func init` integration with available Azure-Samples quickstart templates | Planned |

OpenSpec does not provide parent-child change semantics. This umbrella change therefore coordinates stable focused-change identities and responsibilities through documentation and tracking tasks; every focused change remains independently valid and implementable.

## Goals / Non-Goals

**Goals:**

- Maintain one discoverable inventory of the complete templating program.
- Give every cross-cutting concern one authoritative focused change.
- Separate planning-artifact readiness from product implementation progress.
- Make dependencies explicit without copying child requirements.
- Reserve stable names and initial scopes for the six planned changes.

**Non-Goals:**

- Restate detailed requirements from focused changes.
- Implement product behavior directly from the umbrella task list.
- Force all focused changes into one implementation commit or pull request.
- Decide the detailed constraint, post-action, or bind-source contracts before their focused design work.

## Decisions

### Focused changes remain authoritative

The umbrella owns the map, not the territory. Detailed scenarios, API boundaries, failure behavior, and implementation tasks remain in the focused change responsible for that capability.

```text
templating-system
  |
  +-- foundation
  |   `-- template-engine-integration
  |
  +-- package lifecycle
  |   `-- template-package-install
  |
  +-- command execution
  |   +-- func-new-execution
  |   `-- func-init-execution
  |
  +-- extensibility
  |   +-- template-engine-constraints
  |   +-- template-engine-post-actions
  |   `-- template-engine-bind-sources
  |
  +-- discovery and supply
  |   +-- func-new-search
  |   `-- azure-samples-template-pipeline
  |
  `-- curated project creation
      `-- func-init-quickstarts
```

**Alternative considered:** copy the requirements from every focused change into one large specification. This would create two sources of truth and make later revisions drift. It is rejected.

### Planned changes have narrow responsibility boundaries

`template-engine-constraints` owns func-specific compatibility checks and their authoring, evaluation, and diagnostic contracts. It extends, but does not redefine, the generic constraint evaluation and eligibility model in `template-engine-integration`.

`template-engine-post-actions` owns the supported func action vocabulary, execution context, ordering, cancellation, dry-run representation, safety policy, and diagnostics. Command changes decide when post-actions run; the focused post-action change decides how each supported action behaves.

`template-engine-bind-sources` owns how templates read values from project ecosystems and expose them to TemplateEngine binding. It does not own command-line parameters or host defaults already assigned to the integration and command execution changes.

`func-new-search` owns generation, publication, retrieval, and querying of a discovery manifest built by scanning NuGet feeds for `FuncTemplate` packages. It follows the role of .NET's TemplateEngineDiscovery while keeping the manifest format, CDN lifecycle, feed policy, trust model, freshness, and CLI search experience specific to func.

`azure-samples-template-pipeline` owns the release path that transforms eligible Azure-Samples repositories into valid, versioned `FuncTemplate` packages consumable by `func new install`. It includes repository conventions, validation, packaging, release automation, provenance, and failure reporting, but does not own CLI package-install semantics.

`func-init-quickstarts` owns the first-class init experience for discovering, presenting, acquiring, and invoking Azure-Samples quickstart project templates. It builds on `func-init-execution` for project-template selection and invocation and on `azure-samples-template-pipeline` for the packaged quickstart supply.

**Alternative considered:** place all extensibility behavior in `template-engine-integration`. The resulting change would combine the stable integration boundary with independently evolving ecosystem integrations. It is rejected.

### Dependency direction follows stable contracts

The integration change is the common foundation. Package lifecycle shares its hive and engine bootstrap boundary. Command execution consumes catalog, resolution, parameter, and invocation contracts. Constraints and bind sources extend engine registration and context. Post-actions consume invocation results and command execution policy.

Search depends on the `FuncTemplate` package contract and produces installable package references rather than creating a second installer. The Azure-Samples pipeline produces packages conforming to that same contract. Init quickstarts depend on the Azure-Samples pipeline and existing init execution behavior; whether they also consume the general search manifest is left to the focused quickstart design rather than assumed here.

Focused changes can progress independently when their required contracts are settled. The umbrella does not impose a total implementation order where no technical dependency exists.

**Alternative considered:** sequence all seven changes linearly. This would block independent package and extensibility work and misrepresent the architecture. It is rejected.

### Artifact and implementation status are tracked separately

The umbrella task list marks the four existing specification sets complete and leaves the six planned specification sets open. Product implementation continues to be tracked only in each focused change's own tasks.

This prevents a completed proposal from being mistaken for shipped behavior and keeps the umbrella stable as child implementation is split across branches or releases.

## Risks / Trade-offs

- **[The inventory can become stale]** -> Update this design and task list whenever a focused templating change is added, renamed, split, or removed.
- **[Cross-change behavior can conflict]** -> Resolve overlap in the focused change that owns the responsibility and update dependent references.
- **[The umbrella can become a duplicate backlog]** -> Track only specification readiness and cross-change coordination here; keep implementation checklists in focused changes.
- **[Planned names may prove too broad]** -> Rename or split a planned change before creating its artifacts, then update the umbrella inventory atomically.

## Migration Plan

1. Retain the four existing focused changes as authoritative.
2. Create `template-engine-constraints`, `template-engine-post-actions`, `template-engine-bind-sources`, `func-new-search`, `azure-samples-template-pipeline`, and `func-init-quickstarts` as separate OpenSpec changes.
3. Reconcile their declared dependencies with the existing four changes.
4. Mark the templating system fully specified only after all ten changes pass strict validation.

There is no product rollback for this coordination change. Reverting it removes the umbrella inventory without altering focused specifications or implementation.
