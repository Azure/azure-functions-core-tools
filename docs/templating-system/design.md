## Templates scope and contract navigation

P0 aims to provide one reusable, package-backed Templates capability for both
`func init` (project creation) and `func new` (item creation). Representative
project and HTTP-item checks qualify that capability; they are not a complete
language-stack or content catalog. There is no independent Quickstart P0 workstream.

The Foundation provides a read-only project-compatibility boundary:

```text
language-stack-specific project inspection
  -> FunctionsProject (immutable capability reports)
template publisher -> ItemTemplateRequirements
FunctionsProject + ItemTemplateRequirements
  -> project compatibility matcher
  -> ItemTemplateCompatibilityResult (inputs, outcome, diagnostics)
```

The [constraint design](../template-engine-constraints/design.md) owns the
shared API, capability-report definitions, declaration syntax, matching rules,
and immutable-project lifetime. Its
[acceptance scenarios](../template-engine-constraints/specs/template-constraints/spec.md)
exercise that boundary. This overview does not repeat those requirements.

The public resolved-project contracts allow separately shipped language stacks
to supply captured capabilities without exposing their project models.
The matcher and its result stay internal to the CLI. The focused contract's
[vocabulary](../template-engine-constraints/design.md#vocabulary) distinguishes
these roles from the Templating Engine and the Azure Functions Runtime.

Compatibility evaluation does not perform template preparation or authorize
execution. Package intake, private engine adaptation, output preparation,
publication, and command wiring are separate responsibilities. This Foundation
change does not alter public `func init` or `func new` behavior.

See [PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
for Foundation implementation progress and revision-specific validation evidence.
Track subsequent work in issues, not this design. The older program inventory
below records historical design decisions, not delivery status.

### Historical and deferred documentation boundaries

The detailed integration, init, and new designs below remain useful inventories
of parsing, metadata, and diagnostic mechanisms. They do not establish the
contracts of later Integration work. Unchecked historical task lists are not
evidence that a helper is absent from the source.

The [stack inventory](../func-init-execution/stack-inventory.md) still describes
the additive metadata/initializer step. Its reference to multi-project
Quickstarts as future authority does not apply to current P0. The older
`func new` flow in [CLI architecture](../cli-architecture.md) also needs
reconciliation with the actual command integration; it is not the compatibility
contract.

The [package-install design](../template-package-install/design.md) and its
requirements describe a persistent lifecycle, not the current exact-content
intake contract. The [init Quickstarts design](../func-init-quickstarts/design.md)
does not define P0. Persistent installed membership, shared stores/hives,
install/update/remove UX, acquisition/repair transactions, Quickstart migration,
repository fetching, browse/search/gallery, multi-root topology, broad content
catalogs and general executable actions are deferred. Producer documentation
under `azure-samples-template-pipeline` remains a separate publication proposal;
ordinary qualified project content may be reused without importing that workflow.

The proposals under `proposed/` retain earlier workload/provider design
provenance. They must not be used to infer that legacy dispatch is the approved
P0 path, that this foundation has replaced it, or that workload/version matching
establishes project compatibility.

## Historical program context

See `proposal.md` for the original motivation and `specs/templating-system/spec.md` for the historical coordination contract.

Seven focused templating changes currently have complete planning artifacts:

| Change | Responsibility | Artifact status |
|---|---|---|
| `template-engine-integration` | Host, context, engine lifetime, catalog, resolution, and invocation boundary | Complete |
| `template-package-install` | Template package install, update, uninstall, source, ownership, and replacement safety | Complete |
| `func-new-execution` | Item-template selection, parsing, prompting, dry-run, and invocation | Complete |
| `func-init-execution` | Project-template initialization, adoption boundaries, and configuration effects | Complete |
| `azure-samples-template-pipeline` | Seamless conversion and release of Azure-Samples repositories as packages accepted by `func new install` | Complete |
| `func-init-quickstarts` | Template-first `func init` selection, multi-project topology, and installed Azure-Samples quickstarts | Complete |
| `template-engine-constraints` | Func workload and extension bundle constraints, eligibility results, and restriction guidance | Complete |

Three identified areas do not yet have focused changes:

| Planned change | Responsibility | Artifact status |
|---|---|---|
| `template-engine-post-actions` | Func-supported actions such as npm install, NuGet restore, pip install, and package addition | Planned |
| `template-engine-bind-sources` | Func-supported value sources such as MSBuild properties and npm package properties | Planned |
| `func-new-search` | NuGet feed scanning, `FuncTemplate` discovery manifests, CDN publication, and `func new search` consumption | Planned |

One cross-cutting decision has no owner yet. `func-init-execution` defers how a stack workload's companion template packages are identified, acquired, and installed. The outcome updates `func-init-execution` and `template-package-install`.

OpenSpec does not provide parent-child change semantics. This umbrella change therefore coordinates stable focused-change identities and responsibilities through documentation and tracking tasks; every focused change remains independently valid and implementable.

## Goals / Non-Goals

**Goals:**

- Maintain one discoverable inventory of the complete templating program.
- Give every cross-cutting concern one authoritative focused change.
- Separate planning-artifact readiness from product implementation progress.
- Make dependencies explicit without copying child requirements.
- Reserve stable names and initial scopes for the planned changes.

**Non-Goals:**

- Restate detailed requirements from focused changes.
- Implement product behavior directly from the umbrella task list.
- Force all focused changes into one implementation commit or pull request.
- Decide the detailed constraint, post-action, or bind-source contracts before their focused design work.

## Decisions

### Focused changes remain authoritative

Each focused change owns its detailed scenarios, API boundaries and failure behavior. This overview records how those responsibilities relate.

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

`template-engine-constraints` owns func-specific compatibility checks and their authoring, evaluation, and diagnostic contracts. It extends, but does not redefine, the shared constraint evaluation and eligibility model in `template-engine-integration`.

`template-engine-post-actions` owns the supported func action vocabulary, execution context, ordering, cancellation, dry-run representation, safety policy, and diagnostics. Command changes decide when post-actions run; the focused post-action change decides how each supported action behaves.

`template-engine-bind-sources` owns how templates read values from project ecosystems and expose them to TemplateEngine binding. It does not own command-line parameters or host defaults already assigned to the integration and command execution changes.

`func-new-search` owns generation, publication, retrieval, and querying of a discovery manifest built by scanning NuGet feeds for `FuncTemplate` packages. It follows the role of .NET's TemplateEngineDiscovery while keeping the manifest format, CDN lifecycle, feed policy, trust model, freshness, and CLI search experience specific to func.

`azure-samples-template-pipeline` owns the release path that transforms eligible Azure-Samples repositories into valid, versioned `FuncTemplate` packages consumable by `func new install`. It includes repository conventions, validation, packaging, release automation, provenance, and failure reporting, but does not own CLI package-install semantics.

`func-init-quickstarts` owns template-first `func init` selection, multi-project topology, and the installed quickstart experience. It builds on `func-init-execution` for invocation and finalization, and on `azure-samples-template-pipeline` for the packaged quickstart supply.

**Alternative considered:** place all extensibility behavior in `template-engine-integration`. The resulting change would combine the stable integration boundary with independently evolving ecosystem integrations. It is rejected.

### Dependency direction follows stable contracts

The integration change is the common foundation. Package lifecycle shares its hive and engine bootstrap boundary. Command execution consumes catalog, resolution, parameter, and invocation contracts. Constraints and bind sources extend engine registration and context. Post-actions consume invocation results and command execution policy.

Search depends on the `FuncTemplate` package contract and produces installable package references rather than creating a second installer. The Azure-Samples pipeline produces packages conforming to that same contract. Init quickstarts depend on the Azure-Samples pipeline and init execution behavior. They do not consume the search manifest. An unknown template points to a Functions-owned browse URL instead.

Focused changes can progress independently when their required contracts are settled. The umbrella does not impose a total implementation order where no technical dependency exists.

**Alternative considered:** sequence all ten changes linearly. This would block independent package and extensibility work and misrepresent the architecture. It is rejected.

### Artifact and implementation status are tracked separately

The umbrella task list marks the seven existing specification sets complete and leaves the three planned specification sets open. Product implementation continues to be tracked only in each focused change's own tasks.

This prevents a completed proposal from being mistaken for shipped behavior and keeps the umbrella stable as child implementation is split across branches or releases.

## Risks / Trade-offs

Template authors own production-template end-to-end coverage, normally beside the authored templates. The CLI owns its engine/command contracts, fixture-based journeys, and representative compatibility sanity checks. The publisher owns artifact structure, provenance, and safety validation. These are independent signals: passing one does not waive another, and migration qualification must identify the exact source revision, package version, and CLI version covered by its evidence. The shared evidence format, storage location, and release-report integration remain proposals to agree with the CLI, publisher, and template owners.

- **[The inventory can become stale]** -> Update this design and task list whenever a focused templating change is added, renamed, split, or removed.
- **[Cross-change behavior can conflict]** -> Resolve overlap in the focused change that owns the responsibility and update dependent references.
- **[The umbrella can become a duplicate backlog]** -> Track only specification readiness and cross-change coordination here; keep implementation checklists in focused changes.
- **[Planned names may prove too broad]** -> Rename or split a planned change before creating its artifacts, then update the umbrella inventory atomically.

## Migration Plan

1. Retain the seven existing focused changes as authoritative.
2. Create `template-engine-post-actions`, `template-engine-bind-sources`, and `func-new-search` as separate OpenSpec changes.
3. Reconcile their declared dependencies with the existing seven changes.
4. Mark the templating system fully specified only after all ten changes pass strict validation.

There is no product rollback for this coordination change. Reverting it removes the umbrella inventory without altering focused specifications or implementation.
