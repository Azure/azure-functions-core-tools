# Templating system delivery plan

**Azure Functions CLI · Draft for team discussion · 18 September 2026**

**Integration branch:** `feature/vnext-templating`. See the [branch workflow and temporary-document cleanup](README.md).

## The plan in one minute

Build **three parts in parallel**: the template runtime, package management, and command/parameter handling. Agree the small boundaries between them first. Connect working pieces as they become ready, rather than waiting for an entire development phase to finish.

**The first demonstration:** explicitly install a package, then use it through either `func new` or `func init`. These are independent scenarios. **The final outcome:** all supported templates and stacks, multi-project quickstarts, safe package updates, actions, bindings, publishing, search and migration from the old system.

This is a delivery proposal, not a new product specification or a calendar estimate. The [work-package guide](work-packages.md) contains implementation order, dependencies and acceptance checks. The [source map](work-packages.md#sources) links the authoritative designs and identifies unresolved decisions.

<a id="parallel-plan"></a>

## 1. Who builds what, at the same time?

![Three concurrent workstreams. Ahmed builds runtime and init. Contributor B builds package management and distribution. Contributor C builds shared CLI handling and templates. Small interface and metadata handoffs unblock feature-specific integration. The team does not wait at every phase boundary.](assets/templating-roadmap.svg)

These are **queues, not three people doing every listed task simultaneously**. B and C are proposed roles, not assigned people. Plan around approximately 50% Ahmed / 25% B / 25% C, including review and integration. Size the work before balancing assignments. Do not manufacture dependencies to match those percentages.

With two developers, keep the boundaries and schedule fewer work packages at once. No percentage-parallelism or duration estimate is implied.

## 2. What are we actually building?

- **Runtime:** the Functions layer around Microsoft TemplateEngine. It understands metadata, compatibility, parameters, preview and exact template invocation. It is not a new template processor written from scratch.
- **Package management:** explicit install/update/uninstall operations over one shared installed store. It validates before mutation and protects replacement with coordinated access and rollback.
- **Commands:** `func new` generates an item in an existing project. `func init` selects an installed project template and generates one or more projects. Both use the same runtime and parser.
- **Template supply:** native first-party packages, the Azure Samples producer, and discovery/search information that leads to explicit installation.

For init, generation is followed by **mandatory CLI-owned project configuration**, then permitted ordinary actions. Item generation does not require init's configuration step. Adoption and healing remain separate from template generation. Quickstarts are project templates, not another engine.

**Important boundaries:** init never implicitly installs a missing template. A mixed template cannot fabricate one stack/language for all projects. Dry-run reports effects without target writes or actions. Failure in required configuration preserves generated files and earlier successful configuration, reports partial initialization and stops subsequent actions.

[View the architecture diagram](assets/templating-architecture.svg) for the relationship between publishing, installation and execution.

<a id="phases"></a>

## 3. Four phases, without four team-wide waiting points

### Phase 1 · Agree the handoffs and start independent work

**Goal:** each developer knows exactly what they receive from the others and what they must return.

**Start together**

| Owner | First assignment | Bring back for joint review |
|---|---|---|
| **Ahmed** | Prototype basic, mixed and conditional init preparation | Demonstrated context/eligibility sequence, runtime models and minimum required-action/constraint contract |
| **B** | Verify engine replacement behavior, source rules and failure cases | Shared store/locking contract, recovery evidence and explicit default-acquisition proposal |
| **C** | Define parser cases and inventory current options/stack metadata | Parameter contract, strict parser tests and the early stack-metadata delivery plan |

Agree required project-action and workload-constraint schemas with the consumers. Assign the remaining action, binding and search design work to its implementation owners.

The default-acquisition proposal must cover the [companion-template acquisition handoff](work-packages.md#companion-acquisition). Decide the caller and package relationship in Phase 1, implement the integration in package 6's acquisition slice using package 2, and qualify existing installations in package 9. This tracks a requirement from the deferred setup work, not approval to merge its legacy implementation.

**What unlocks the next work?** Only that component's interfaces and examples need agreement. Parser work need not wait for the init context decision. Store work need not wait for search design. The [four handoffs](work-packages.md#handoffs) state the exact outputs.

### Phase 2 · Build independently, publish small usable slices

**Goal:** all three owners can make progress without a finished CLI.

| Owner | Build order inside this workstream |
|---|---|
| **Ahmed** | Host/lifetime → metadata/matching → preview/invocation, including minimum real eligibility and project-action projection |
| **B** | Classification/staging → coordinated store access/replacement → lifecycle commands, using the shared bootstrap |
| **C** | Shared parser → additive stack metadata/catalog → new-command orchestration, without premature production registration changes |

Publish the parser and metadata slices **before** the whole new command is complete. That is how init avoids waiting on C's entire workstream.

**What can start alongside this?** Authoring inventory, native template conversion, Samples onboarding/source acquisition and search schema/client work, once their own contracts are known and a developer has capacity. They do not wait for finished commands.

### Phase 3 · Integrate one feature at a time while other work continues

**Goal:** demonstrate real behavior early, then expand it. This overlaps Phase 2.

| Integration point | Real components needed | Work that does not block it |
|---|---|---|
| **Installed templates are visible and refresh after update** | Runtime catalog + package lifecycle + common store policy | Both generation commands, public publishing and search |
| **An item is created through `func new`** | Runtime + parser + new orchestration + applicable constraints + a test package | Finished init, Samples pipeline and search |
| **A basic project is created through `func init`** | Runtime + parser + stack metadata + init + workload constraints + required configuration finalization + a test package | Finished new command, ordinary actions the fixture does not use, public Samples/search |

Test packages can be staged by a test harness for command development. The integrated installation milestone must use the real lifecycle implementation. Neither case proves public package availability.

Then expand through the work packages below: advanced init and full constraints, remaining templates/actions/bindings, first-party and Samples publication, and search. Reassign self-contained adapters or template batches after sizing. Samples and search do not depend on each other's completion. A person's queue may serialize them, but that is a staffing choice.

### Phase 4 · Qualify the replacements and switch production paths

**Goal:** prove the migration, not just a new-project demo.

Each owner validates failures and platform behavior throughout development. At cutover, combine that evidence with fresh/existing-installation tests, default-template availability, artifact-level CLI checks, documentation and operational recovery.

Remove legacy providers and behavioral initializers only when their replacements and compatibility policy pass. Production init must not silently fall back to another scaffolding engine when a package is absent.

**Full-program completion includes supply, search, actions and bindings.** An earlier CLI milestone may be released separately, but does not complete this plan.

<a id="work-index"></a>

## 4. The assignable work packages

Each link opens a short card with build order, independent work and real integration requirements. A package may take several PRs. A prerequisite is a deliverable, not necessarily a whole completed package.

| Package | Primary owner | Deliverable |
|---|---|---|
| [1 · Template runtime](work-packages.md#runtime) | Ahmed | Host, metadata, groups, preview and exact invocation |
| [2 · Package lifecycle](work-packages.md#packages) | B | Safe explicit install/update/uninstall |
| [3 · Parser and new command](work-packages.md#new) | C | Shared parser first, item-generation command second |
| [4 · Init and quickstarts](work-packages.md#init) | Ahmed | Template-first single/multi-project creation and existing-project handling |
| [5 · Constraints, bindings and actions](work-packages.md#extensions) | Ahmed + named adapter owners | Required init support early, remaining supported extensions independently |
| [6 · Stacks and first-party templates](work-packages.md#templates) | C, with Ahmed/B handoffs | Stack metadata early, template content, publication and explicit acquisition |
| [7 · Azure Samples producer](work-packages.md#samples) | B | Source release to validated, approved package |
| [8 · Search and browse](work-packages.md#search) | B producer / C consumer | Searchable install references and stable browse guidance |
| [9 · Migration and release](work-packages.md#release) | All, Ahmed coordinates | Verified replacement of old paths and full-system readiness |

## 5. Immediate next step

Assign the three Phase 1 deliveries. Review their interfaces together, merge agreed slices, and connect the first real package/catalog path. Track accepted deliverables, not completed columns in the diagram.

**Planning limits:** this proposal uses the source/design baseline recorded in the [guide](work-packages.md#sources). Four focused designs remain unfinished and the separate Samples implementation has not been audited. Resolve those unknowns before promising effort or dates. The build/test checks in the guide are acceptance work, not results already achieved.