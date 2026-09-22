# Templating work-package guide

**Companion to the [delivery overview](implementation-plan.md) · Proposed ownership and sequencing**

Use these cards to create small implementation assignments. Do not create one giant PR per card. Each card distinguishes **work that can start independently** from **the real integrations required for acceptance**. Component tests, real-engine tests and documentation belong to the owner, not to a late testing team.

Here, a numbered **work package** is an assignable unit of engineering work. A **template package** is the installable artifact it helps produce or consume. A **slice** is a small usable part of a work package, such as the shared parser delivered before the rest of the new command.

<a id="handoffs"></a>

## The four handoffs to agree first

An interface here means both a data shape and its behavior, including errors, cancellation and lifetime. Names are conceptual unless already specified. This guide does not declare new public APIs.

| Handoff | Producer and consumer | Exact agreement needed | What it unblocks |
|---|---|---|---|
| **Runtime and parameters** | Ahmed ↔ C | Candidate identity/type/parameter metadata, canonical values, invalid versus missing input, preview/invocation outcomes and context lifetime | Parser, item-command orchestration and init selection tests without a finished runtime |
| **Installed store** | B ↔ Ahmed | Common bootstrap/settings location, shared-reader/exclusive-writer policy, lock lifetime, snapshot validity, disposal/rollback and refresh | Runtime readers and lifecycle writers implemented separately without competing stores |
| **Eligibility and project actions** | Ahmed ↔ C/B | Workload requirements and structured remediation, trusted configuration action schema, primary-output references, validation and finalization ordering | Init, native templates and producer validation |
| **Packages and discovery** | B ↔ C, Ahmed reviews acquisition | Package ownership/source/version rules, explicit default-acquisition caller, manifest shape, trust/freshness and browse destination | Package authors, publishing, search producer and search consumer |

### Decisions that must not be guessed

- **Init preparation.** Context-free metadata does not evaluate bindings/defaults. Conditional projects can depend on final parameters and effects, while execution context is immutable. Demonstrate what noninvocable preparation can evaluate and when final eligibility is established. Preserve candidate identity through revalidation. Do not silently introduce mutable sessions. Homogeneous common context is optional in the quickstart specification. Mixed context must not fabricate values.
- **Store safety.** Specify read-lease lifetime and invalidation together with replacement. On rollback, dispose the engine session before restoring the snapshot, then recreate it against the restored store before returning diagnostics. Temporary validation sessions and this recovery sequence are not reusable global execution sessions.
- **Authoring support.** Define the exact workload constraints, required configuration action and supported ordinary actions/bind sources. Minimum init support is an early dependency. Unspecified adapter behavior is not an implementation detail to invent.
- **Default acquisition and compatibility.** Complete the [companion-template acquisition handoff](#companion-acquisition) before wiring its caller. Select the explicit setup/workload/package operation, source/version policy and partial-failure behavior. It must not become implicit installation inside init. Decide how old templates, options and external workloads transition.
- **Discovery policy.** Agree the manifest and each consumer's feed/trust/freshness rules before building that slice. Publication recovery is needed before operational acceptance, not before a fixture-based reader can start. B owns the stable Functions-controlled browse URL and destination delivery, C consumes it. The destination can evolve behind a redirect. Search and init remain separate from package mutation.

Record each decision in its owning focused design, with the dependent documents updated. There is no all-designs-complete gate for unrelated implementation.

---

<a id="runtime"></a>

## 1. Template runtime

**Owner:** Ahmed. **Outcome:** Functions-owned APIs can describe, resolve, preview and invoke the exact selected template using Microsoft TemplateEngine.

**Build in order**
1. DI factory, settings policy, immutable context and disposable execution session. Correctly distinguish execution directory and project root.
2. Metadata-only descriptors for early inspection, plus context-bound eligibility, type-scoped matching and immutable candidate groups. Project parameter aliases and diagnostics once.
3. Exact invocation and native dry-run, with Functions-owned file/action results. A descriptor is not an invocation capability. No re-selection by name at invocation.

**Start independently:** host/default tests, metadata projection and matching fixtures. Publish the parameter model before the whole runtime is complete.

**Needs real integration to finish:** package 2's store policy for every live read/invocation, package 5's initial compatibility/action components, and package 3's canonical parameter results. The agreed preparation lifecycle is required before context-sensitive init APIs are finalized.

**Done when:** real templates retain identity, schema, context and diagnostics through preview/creation. Disposal, unsupported metadata, wrong type and failed eligibility cannot be bypassed. Raw engine objects do not leak into command contracts.

<a id="packages"></a>

## 2. Package lifecycle

**Owner:** B. **Outcome:** explicit install/update/uninstall operations with predictable sources and safe replacement.

**Build in order**
1. Shared template/workload package classification and source/input handling. Reuse engine package resolution rather than recreating NuGet semantics.
2. Stage and validate in temporary storage before live mutation. Require valid templates and the correct NuGet package ownership. Handle folder inputs under the specified rules.
3. Implement coordinated read/write access, replacement snapshots, rollback and metadata refresh. Integrate lifecycle commands and reciprocal workload/template guidance.

**Start independently:** classification, source policies, transaction fault injection and command request/result handling. Test against the pinned engine provider without waiting for finished new/init commands. Share its bootstrap with package 1, not a second production integration.

**Needs real integration to finish:** package 1 readers must obey the same access policy. Exercise concurrent processes, cache rebuild, session disposal and post-update catalog refresh. Temporary test storage is not proof of safe live-store replacement.

**Done when:** invalid packages never become live and failed replacement preserves the prior usable package even offline. Verify disposal before byte-for-byte restoration, session recreation against restored state before diagnostics, and cancellation/cache-rebuild failure recovery. Preserve the distinction between explicit install version replacement and newer-only update, including cross-source policy.

<a id="new"></a>

## 3. Shared parser and `func new`

**Owner:** C. **Outcome:** strict parameters shared by both commands, then complete item-template command behavior.

**Build in order**
1. **Publish the parser slice first.** Preserve raw template tokens, construct candidate-specific schemas, resolve aliases/collisions, and return canonical values. Distinguish invalid input from missing required values.
2. Implement the stable host command surface, immutable requests, candidate selection, help and result rendering. Use fixtures while the runtime is being built.
3. Connect project/bundle context, item matching, language/argument filtering, precedence, prompting, preview and exact invocation. Coordinate the small stack-metadata dependency from package 6 before final activation.

**Start independently:** parser tests, request binding and orchestration against agreed fakes. Invalid options/choices/types must fail. Unresolved required input can remain selectable for prompting but never invocable.

**Needs real integration to finish:** packages 1 and 5, a real item template, and stack metadata wherever the migrated path consumes it. Use lifecycle-installed packages for final end-to-end acceptance. Init is not a prerequisite.

**Done when:** the real CLI creates items correctly from interactive and non-interactive requests, handles lifecycle-name collisions and effective aliases, separates output path from project root, and makes no target changes on preview or validation failure. Document intentional grammar changes.

<a id="init"></a>

## 4. `func init` and quickstarts

**Owner:** Ahmed. **Outcome:** template-first project creation, including heterogeneous solutions, without changing adoption into template execution.

**Build in order**
1. State handling and selection policy using metadata descriptors and test doubles. Existing adoption/healing stays root-project scoped and does not load templates.
2. Integrate one basic real project template using the shared parser, initial workload constraints and mandatory configuration finalization. Validate effects before authorized force cleanup. Preview includes planned configuration without writes.
3. Add mixed/conditional projects, renamed anchors, all-project filters, unavailable-template presentation and multi-project output. Complete adoption/healing, cancellation and partial-failure cases.

**Start independently:** state/selection/finalizer tests using fixed metadata and resolved-effect examples. Do not implement the obsolete stack-first creation flow or assume fixtures resolve the open context question.

**Needs real integration to finish:** package 1, package 3's parser only, package 5's required support and package 6's early stack metadata. The complete new command, public Samples feed and search are not prerequisites. Production activation also requires default-package availability and compatibility decisions.

**Done when:** project declarations are validated before writes/deletion, required configuration is atomic per project, a failure preserves generated files and earlier configurations, and ordinary actions never run after incomplete finalization. Missing templates yield browse/explicit-install guidance, not an automatic download or initializer fallback.

<a id="extensions"></a>

## 5. Constraints, bindings and actions

**Owner:** Ahmed owns early contracts and engine-side integration. C or another explicitly assigned owner takes independent adapters as capacity becomes available. **This is not a fourth simultaneous team.**

**Deliver in two groups**
1. **Early init support:** workload-constraint outcomes/remediation, applicable existing-project compatibility checks, trusted configuration-action schema, metadata/effect projection and real required-finalization behavior. Package 4 owns command ordering and the finalizer. Package 1 owns the engine-side projection. Agree this seam before either is completed.
2. **Independent adapters:** each agreed ordinary action and ecosystem bind source is a separate assignment with its own input, failure, cancellation, safety and preview tests. Examples in scope planning include restore/install/package addition and MSBuild/npm-derived values. The exact supported set requires the focused designs.

**Start independently:** policy/design work and adapters behind agreed context/effect interfaces. No need to wait for all init scenarios. Tests can use fixed inputs before full engine registration is available.

**Needs real integration to finish:** registration in the real session, correct defaults/binding semantics, diagnostics visible through commands and real templates exercising each adapter. These are implementations, not dispatcher placeholders.

**Done when:** all agreed constraints, actions and bind sources work, fail closed where required and respect unavailable context. Keep required configuration distinct from ordinary action consent and continue-on-error policy. Catalog summaries never substitute for authoritative final eligibility.

<a id="templates"></a>

## 6. Stack metadata and first-party templates

**Owners:** C leads metadata/content. Ahmed owns init composition and acquisition policy. B owns package build/publication and acquisition services. Name an owner for each batch rather than assigning one undivided migration.

**Publish three separate slices**
1. **Early metadata slice:** canonical stacks/languages/aliases and the metadata-only catalog for .NET, Node, Python, Go, Java and PowerShell. Schedule this after the parser slice, before C finishes the new command. Develop additively or with test stacks. Do not remove production initializer registrations yet.
2. **Content batches:** port item and project templates, parameters, primary outputs, requirements and actions. Work per stack/language against fixed authoring contracts. Java uses the shared engine, not a new Java-specific provider. Record old-option/content compatibility decisions.
3. **Publication and explicit acquisition:** build/version/release first-party `FuncTemplate` artifacts independently of the Samples producer. Wire the chosen explicit setup/workload/package caller, including source/version selection and partial failure. Define handling for external workloads that still implement the old contract.

**Start independently:** inventories immediately, metadata once its contract is agreed, content once its authoring schemas are agreed. None needs finished init orchestration to begin. Real runtime validation and default acquisition are acceptance dependencies, not reasons to postpone authoring.

**Needs real integration to finish:** packages 1/5 validate every template, 2 installs artifacts from the intended feed, and 3/4 exercise generated output. Derive supported language/platform cases from registrations and build policy, not this six-stack list alone.

**Done when:** every supported combination has usable published content and deliberate upgrade behavior. Coordinate all production stack registrations and initializer removal at cutover. Never vary scaffolding engines based on whether template packages happen to be installed.

<a id="companion-acquisition"></a>

### Companion-template acquisition handoff

This deliverable carries forward the acquisition requirement exposed by [deferred PR #5595](https://github.com/Azure/azure-functions-core-tools/pull/5595), not its legacy content-workload ownership implementation. [Issue #5451](https://github.com/Azure/azure-functions-core-tools/issues/5451) tracks setup decomposition. [Issue #5384](https://github.com/Azure/azure-functions-core-tools/issues/5384) tracks remote stack package identity and meta-package dependency recipes. That remote lookup is distinct from this package's installed stack-capability catalog and package 8's template-search manifest.

**Phase 1 decision, led by Ahmed with B/C input:** record where the stack-to-companion `FuncTemplate` relationship is declared, the explicit acquisition caller, source/version selection, cancellation and partial-failure behavior, and compatibility with old templates and external workloads. Update the owning focused designs, including init's currently deferred acquisition section and any affected lifecycle contract. These policies remain undecided until that design is reviewed. Neither implicit installation inside `func init` nor automatic owner migration is authorized by this handoff.

**Package 6 acquisition slice:** B supplies publication/acquisition services, C supplies package metadata/content, and Ahmed integrates the chosen caller. Use package 2's engine-managed lifecycle rather than another template registry or legacy alias-owner selector. Integrate once that contract and lifecycle slice are ready, without waiting for the entire templating program.

Acceptance evidence, shared with packages 2, 3/4 and 9:

- [ ] An explicit caller resolves the declared companion package and installs it through the real lifecycle path. An unknown template reference does not trigger implicit installation.
- [ ] The installed package is visible through the runtime catalog and usable by the applicable `func new`/`func init` path. A registry row alone is not readiness evidence.
- [ ] Source/version selection, missing packages, offline behavior, cancellation and partial failure follow the reviewed contract. Repeat execution does not duplicate the installation.
- [ ] Existing installations, replacement failures and cross-channel/package transitions have an explicit compatibility decision and regression coverage. Package 9 qualifies legacy retirement before cutover.

Keep #5595's feed-validation and owner/channel findings as regression input when relevant, not as a requirement to reproduce its current algorithm. Setup stack-package discovery stays with #5384. [PR #5605](https://github.com/Azure/azure-functions-core-tools/pull/5605) concerns workload-search error handling and is separate from package 8's template search.

<a id="samples"></a>

## 7. Azure Samples producer

**Owner:** B. **Outcome:** a repeatable, approved path from an eligible immutable source release to a valid template package.

**Build in order**
1. Audit the separate producer repository. Implement missing onboarding validation, release discovery, exact source acquisition, license/provenance and content-safety checks.
2. Preserve authored templates or synthesize supported static project roots. Validate required workload constraints, trusted actions and resolved outputs through the real template integration. Static multiple roots are allowed. Conditional topology requires authored configuration.
3. Build packages, stage, approve, revalidate and promote identical bytes. Implement retry/resume behavior, identities, mutation coordination and operational diagnostics.

**Start independently:** onboarding, release discovery and safe acquisition once source contracts are agreed. Producer fixtures do not require finished command UX. Pull these tasks forward if lifecycle work is waiting on a reviewed handoff.

**Needs real integration to finish:** packages 1/5 validate output, 2 installs it and 4 runs representative packages. Publishing identities/feed/approval infrastructure must be available. Search is not a prerequisite for packaging or explicit installation.

**Done when:** a source release produces a traceable, installable package that the real CLI can use, and interrupted staging/promotion has tested recovery. Do not claim this implementation is absent or complete before auditing its repository.

<a id="search"></a>

## 8. Search and browse

**Owners:** B produces/publishes discovery data and owns the stable Functions browse URL/destination delivery. C consumes them in the CLI. Agree one manifest contract. A maintained redirect can keep the CLI URL stable while the destination changes.

**Build in order**
1. Agree the manifest and required feed/trust/freshness rules for each implementation slice. Plan publication recovery in parallel. Separate finding a package from installing it.
2. Build the feed scanner/manifest publisher and CLI reader/search UX independently against shared fixtures.
3. Integrate published data, real install references, stale/unavailable data behavior and browse/install guidance.

**Start independently:** as soon as the package/manifest contracts and relevant policies are agreed. Fixture-based clients do not wait for a live browse destination, finished init or all Samples releases. Later scheduling under B/C is capacity allocation, not a dependency on package 7.

**Needs real integration to finish:** a published manifest, compatible real package references and explicit install through package 2. Init should remain usable with already installed templates when remote discovery is unavailable.

**Done when:** users can find and intentionally install a valid package, understand unavailable/stale results and reach a maintained browse destination. No implicit install flow is introduced.

<a id="release"></a>

## 9. Migration and release

**Owners:** each package owner supplies evidence, Ahmed coordinates the combined decision. Start tests and documentation with each delivery, not after implementation ends.

**Join the system in small checks**
1. Runtime + lifecycle: install, list, update, refresh and concurrent access.
2. Runtime + parser + commands: independent item/basic-project creation using a real package and required extensions.
3. Runtime + producers: first-party and Samples artifacts install and execute, with shared authoring validation.
4. Full readiness: supported stacks/languages/platforms, advanced templates, actions/bindings, search, existing installations and operational recovery.

**Before each production replacement:** prove the affected compatibility policy, default availability, help/errors, cancellation and no-write failure/preview behavior. Preserve `.git` under the specified forced-init policy. Test atomic configuration, partial failure and rollback under fault injection. Avoid releasing a test-only mixed registration state.

**Done when:** all ten focused areas below have implementation and acceptance evidence, old paths can be removed without silent fallback, and actual release artifacts pass strict CI-policy Release builds, relevant full suites and supported-platform CLI checks. Include authoring/troubleshooting documentation and measured startup regressions. Record limits and exceptions explicitly. A partial CLI release does not finish the full program.

<a id="sources"></a>

## Source of truth and completeness check

**Reviewed baseline:** upstream [31e0df32](https://github.com/Azure/azure-functions-core-tools/commit/31e0df3259a2a018b7abc5354ff6ebda5826ae54), 17 September 2026. These are inspection facts, not a live progress dashboard. Refresh before estimating or assigning implementation.

- Legacy V2/.NET providers and initializers still serve command execution. The new `Templater` is foundation code. Separate content workloads are not proof of independent engine-code delivery.
- Six focused design sets exist. Constraints, post-actions, bind sources and search need their focused designs.
- Merged quickstarts makes template-first init authoritative. Reconcile the older init sequence, type-before-ambiguity, invalid-versus-missing input, omitted Java tasks, producer workload requirements and stale synthesis restrictions in their owning documents.
- The producer repository has not been inspected. No staffing/date estimate assumes it starts from zero.
- This plan does not add independent engine-plugin delivery. If retained from #5330, resolve packaging/loading/version compatibility separately and update dependencies explicitly.

| Focused area | Delivery owner/package | Authoritative design |
|---|---|---|
| Engine integration | 1 | [Integration](../template-engine-integration/design.md) |
| Package lifecycle | 2 | [Lifecycle](../template-package-install/design.md) |
| New execution | 3 | [New](../func-new-execution/design.md) |
| Init execution | 4 + 6 | [Init](../func-init-execution/design.md) |
| Constraints | 5, early runtime/init consumers | [Scope awaiting focused design](design.md#planned-changes-have-narrow-responsibility-boundaries) |
| Post-actions | 5 + 4 required finalization | [Scope awaiting focused design](design.md#planned-changes-have-narrow-responsibility-boundaries) |
| Bind sources | 5 | [Scope awaiting focused design](design.md#planned-changes-have-narrow-responsibility-boundaries) |
| Search | 8 | [Scope awaiting focused design](design.md#planned-changes-have-narrow-responsibility-boundaries) |
| Azure Samples pipeline | 7 | [Samples](../azure-samples-template-pipeline/design.md) |
| Init quickstarts | 4, 5, 6 and producer validation | [Merged design #5556](https://github.com/Azure/azure-functions-core-tools/pull/5556) |

Use one traceability record per requirement: **source requirement → implementation/PR → acceptance result → remaining limit**. The [coordination tasks](tasks.md) remain the scope inventory. All acceptance checks above are work to perform, not test results produced while writing this plan.