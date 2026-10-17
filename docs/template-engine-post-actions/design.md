## Context

The integration owns one selected template, effects evaluation, and creation. Init owns destructive cleanup and mandatory `.func/config.json` finalization. Template authors own generated application behavior and end-to-end coverage. This design supplies declarations and processors without moving those responsibilities into template-controlled scripts.

## Goals / Non-Goals

Goals are explicit topology, preflight before mutation, atomic CLI configuration, useful dry-run effects, and substitutable restore adapters. Non-goals are general shell execution, SDK installation, arbitrary executable/argument pass-through, transactional rollback of generated projects, a new consent system, or running template code in the central packager.

## Decisions

### Project configuration action

The project configuration action is a template-declared request for CLI-owned `.func/config.json` persistence. It is mandatory when active. A configuration plan is its validated projection, and finalization is the execution phase, not a different action. Trust applies to the CLI processor and serializer, not the untrusted declaration, which must pass preflight.

The proposed action ID is `00e33184-ffc7-43ef-bec0-b1684df8ad56`. Each generated Functions project declares one action referencing an index in the raw `primaryOutputs` array:

```json
{
  "actionId": "00e33184-ffc7-43ef-bec0-b1684df8ad56",
  "continueOnError": false,
  "args": {
    "primaryOutputIndex": "0",
    "stack": "python",
    "language": "python"
  }
}
```

The index is a nonnegative invariant-culture integer string. Stack and language are required non-empty strings that resolve to supported canonical values. Extra arguments are rejected rather than treated as process inputs. Omitted `continueOnError` or raw boolean `false` is allowed, while raw `true` or a non-boolean value is rejected. Configuration actions cannot be overridden by an ordinary-action skip policy. Action conditions do not suppress generated projects or their primary outputs. Every active declared Functions project must have exactly one active configuration action. Individual alternatives may be inactive, but leaving a declared project with no active configuration action is permitted only when its corresponding topology/output is also inactive. Project-template creation requires at least one active project and configuration action.

The raw index identifies the authored primary-output entry before condition filtering. Projection preserves that identity and binds it to the corresponding resolved output after conditions, source targets, `sourceName`, and filename renames. It must not index into the filtered resolved-output list, which could reference another project. An inactive, missing, ambiguous, or non-file reference makes an active configuration action invalid. An implementation spike against the pinned engine must demonstrate this mapping; manual string replacement is not a substitute.

Validate the raw `postActions` objects and arguments before engine normalization. The engine can stringify non-string argument values, so projected `"0"` cannot prove that the author supplied a string. Reject duplicate action/argument properties, incorrect raw types, malformed indexes, extra arguments, and invalid continue-on-error declarations in packaging and installed-template preflight. Configuration actions are project-template-only; an item template declaring one is rejected, not silently ignored.

The final project root is the parent of the resolved output, normally `host.json`. A template declares stack/language topology, but only the CLI serializes `.func/config.json`. Template content must never write or modify that file. Multiple projects can have different stacks and languages, and explicit init filters are checked against every active project.

Configuration-action templates are func-only. Require an engine `host` compatibility declaration for `func` plus the existing func package/constraint checks, so foreign hosts reject rather than silently skip CLI-owned configuration. Forcing a foreign-host bypass is unsupported and does not produce a qualified Functions project. Shared templates that intentionally support other hosts must not adopt these func-only actions.

### Initial ordinary package restore action

The proposed restore action ID is `17a32346-c721-42cd-a520-4d1cada22ba2`. It accepts exactly the required raw string arguments `manager` and `primaryOutputIndex`; missing, non-string, duplicate, and extra arguments are rejected before engine coercion. The index is a nonnegative invariant-culture integer string referring to the authored primary-output entry before condition filtering. Proposed managers are `dotnet`, `npm`, `pip`, and `maven`, each behind its own injectable adapter. The primary output is the actual manager input: a project file, `package.json`, `requirements.txt`, or `pom.xml`. Projection retains that output identity through conditions and renames under the same snapshot rules as configuration actions, and rejects inactive, missing, ambiguous, or non-file inputs.

Keep explicit manager selection rather than infer a tool from the filename. A manifest can be served by different managers, and preflight validates the approved manager/input pairing. Initial npm behavior remains `npm install`, including when a lockfile exists, rather than silently switch to `npm ci`. Yarn, pnpm, uv, poetry, and `pyproject.toml` inputs are deferred until separately reviewed adapters and schemas are qualified.

Processors construct fixed argv from a CLI-selected executable and the validated input path, without template-supplied shell text. Examples are `dotnet restore <project>`, `npm install` in the package root, the selected Python interpreter's `-m pip install -r <requirements>`, and Maven dependency resolution in the POM root. Windows npm/Maven batch launchers require a separately reviewed CLI-owned launcher path with fixed switches and tested quoting; this exception does not permit arbitrary template arguments or shell fallback. The exact adapter commands, interpreter/tool resolution, and launcher policy are review points and require contract tests. Templates cannot provide an executable, shell fragment, extra arguments, environment overrides, credentials, or a path outside the invocation output root.

The pip adapter remains blocked until project-local venv/interpreter resolution is agreed and qualified. It must never silently install into a global interpreter's site-packages. A command-verified existing or command-created venv needs an explicit tool-identity policy, including origin/link validation and protection from template-supplied executable shadowing. A generated `.venv` path or `pyvenv.cfg` is not proof of trusted tool identity. That narrowly reviewed exception is required before a project-local interpreter can pass the otherwise outside-output tool rule.

Tool resolution must produce validated absolute paths for every executable, interpreter, batch script, and launcher. Resolve through the CLI's reviewed tool boundary without searching the generated working directory, follow link/reparse targets, and reject tools supplied from the invocation output or template mount. On Windows the approved absolute launcher invokes the exact validated absolute manager-script path, not a bare `npm` or `mvn` that could select a generated shim. Inputs and tool paths are rechecked before launch. This prevents direct template executable shadowing; it does not sandbox hooks or stop an approved manager from running project code under its own policy.

Restore may execute package-manager lifecycle hooks and access feeds. It is ordinary untrusted project work, not a trusted configuration operation. Existing init policy runs supported ordinary actions after mandatory finalization by default; this proposal does not add a consent flag. That behavior, supported managers, and the security boundary must be reviewed with the command/security owners. Missing tools produce actionable failures; processors do not provision them. Restore installs dependencies already declared in the manager input; it does not add absent manifest dependencies and does not resolve the Durable item-template missing-dependency issue. Template authors must correct that manifest, or a separately reviewed package-add action must supply a typed package/version schema and mutation policy. This proposal makes no package-add coverage claim.

### Item ordinary actions are deferred

The initial ordinary-action dispatcher serves project initialization only. Item templates remain behind the existing migration path until a real supported journey, containing-project target, and authorization contract are agreed and qualified. Do not ship a new item dispatcher solely for hypothetical output-local restore. New-path item action declarations fail preflight before item creation rather than be silently ignored or run. A future containing-project target must be typed and command-owned, not reinterpret `primaryOutputIndex` or widen containment implicitly.

### Go module tidy has a separate owner

The existing Go init flow and migration tasks require `go mod tidy`. The proposed module-tidy action ID is `5080f899-da0c-404a-b80c-909df8e203b5`, with exactly one required raw string argument `primaryOutputIndex` identifying an authored `go.mod` output. Apply the same raw index validation, stable output identity, retained evaluation, containment, and launch-time checks as restore. The processor invokes the validated absolute Go tool with fixed `mod`, `tidy` arguments from the resolved module root. No template-supplied Go flags or environment overrides are allowed.

Tidy may add/remove module requirements and update `go.sum`, so it is not a restore-only action and must not be described as one. It follows ordinary-action ordering, conditions, cancellation, and continued-failure reporting, not mandatory configuration semantics. The CLI preserves `--skip-go-mod-tidy` as a command-owned target-specific control, not an unqualified template-symbol rename. The Go tool can access feeds and perform its own toolchain/module operations; fixed launch arguments do not make those effects transactional or sandboxed. Toolchain policy and this adapter require Go/command/security review.

The Go initializer's tidy behavior remains its owner until this separate action is agreed, implemented, and qualified. Preserve the default-run and explicit skip choices and qualify module/checksum effects. Legacy init currently discards tidy's returned nonzero result; this proposal intentionally reports it as an ordinary-action failure with a nonzero command outcome instead. That failure change and cancellation outcomes require explicit Go/command-owner approval and migration notes, not a claim of exact failure parity. Startup-time Go build/tidy behavior is outside this migration and must not be removed merely because an init action is added.

### Shared preflight and effects

After final parameters are known, evaluate action and primary-output conditions in the selected template's session and create func-owned action plans. Keep raw action identity, declaration order, resolved input/root, processor kind, canonical stack/language where needed, and continue-on-error policy. No raw TemplateEngine objects cross into command rendering.

Derive the active Functions project inventory independently of configuration actions from the retained resolved output/effect snapshot. Take the union of resolved primary outputs named `host.json` and planned created or modified `host.json` files, normalize their parent roots, and deduplicate them using the platform's path comparison. Any such candidate root must be finalized; a missing declaration cannot remove it from this inventory. Templates containing unrelated host-named files need an explicit separately reviewed classification/exclusion contract before acceptance, not an implicit action-derived exception. This conservative convention and its fixture cases are part of the implementation review gate.

Runtime/command and publisher owners jointly own the classification/exclusion contract. Conservative inventory can be implemented and tested on unambiguous fixtures, but acceptance or migration of samples with unrelated host-named content is blocked until a reviewed exclusion preserves omission detection. This is not permission to derive completeness from configuration declarations or silently discard a fixture/AppHost file.

Then collect raw configuration/output associations, evaluate action conditions, and require exactly one active configuration action for every independently discovered active root, with no action targeting an absent/inactive root. A false action condition cannot erase an active project. Mutually exclusive declarations may describe the same output, but their active set must configure that root exactly once. Both inactive project and inactive action are valid; active action with inactive output and active root without an action are authoring errors before mutation. A configuration reference may use another direct project-root file only when its resolved parent matches an independently discovered host root.

Before cleanup or creation, reject unsupported action IDs, invalid arguments, unresolved references, escaping roots or multiple active configuration plans for one root, stack/language conflicts, direct configuration-file effects, and collisions among planned template and configuration writes. Duplicate inventory observations are normalized and deduplicated, not confused with competing active finalization plans. Path checks use resolved paths and an injectable filesystem boundary, including symlink/reparse-point checks before writes. A required configuration target must be a Functions project root; an ordinary restore input must be the supported manager's file. Shared tests cover conditions and renamed/multi-project outputs, not only literal paths.

Preflight and creation must share the evaluated parameter, bind, and generated-symbol values that determine action conditions, primary outputs, source targets, and renames. A configuration-file fingerprint alone is insufficient: unchanged declarations can evaluate differently on a second engine call. The pinned-engine spike must prove how a retained evaluation can be used for actual creation. If that cannot be guaranteed for a template's topology, reject that template before cleanup rather than repeat nondeterministic evaluation and discover a mismatch after destructive work. This is an implementation gate, not a claim that the current engine already supplies a reusable plan.

If reusable evaluation cannot be proven, qualify an explicitly bounded stable-topology subset driven by supplied or captured deterministic values. Inspect the dependency graph for topology-affecting conditions, bindings, generated symbols, source targets, and renames, and reject any plan whose stability cannot be established before cleanup. Do not reject every generated symbol indiscriminately or assume a configuration fingerprint proves stability. The spike must publish a source/package-version-pinned inventory, actual supported/rejected counts, and fixture evidence before a command switch. Current impact counts are unknown, and unqualified journeys retain their legacy path.

Current shared .NET item templates contain standard add-reference and file-action IDs. Before switching the old provider, inventory those declarations and either implement reviewed translations/adapters or publish qualified replacement templates preserving their behavior. No unknown action is silently executed or ignored, and blanket rejection cannot be used to claim existing templates remain supported. Package addition and file-opening behavior are migration review decisions separate from the proposed restore action; the command switch stays blocked until the supported/replaced matrix covers current behavior.

Dry-run uses the same plans and validation. It reports template effects, mandatory configuration writes, and ordinary restore intent in order, but creates no files, launches no process, and does not require tool availability merely to render a plan. Force only permits command-owned file cleanup/conflict handling; it does not bypass action safety.

### Execution ordering and failures

For project creation, init first preflights all active plans, performs authorized cleanup, creates the selected template, and finalizes configuration actions in declaration order. Before each write it verifies the resolved primary output exists and the target remains inside the invocation root. Each configuration write uses an atomic temporary-sibling replace. If any configuration action fails, stop remaining finalization and all ordinary actions, report completed projects and the failure, and preserve generated files and successful writes. There is no destructive rollback.

Only after all mandatory finalization succeeds do project ordinary actions run in declaration order. Item dispatch remains deferred behind its migration qualification. Immediately before each process launch, revalidate the actual input file and working directory for expected kind, containment, and links/reparse points; creation or earlier restore hooks may have changed them since preflight. A failed recheck launches nothing and is reported through the ordinary failure policy. An ordinary action with continue-on-error false stops later actions and returns failure; true records the failed action, continues, and still produces an overall nonzero outcome rather than claiming every action succeeded. Cancellation stops before further writes/processes and passes through each adapter. Already-started process cleanup follows the existing process runner contract, not a guarantee that arbitrary descendant processes or external package-manager state are atomically rolled back.

The proposed CLI-owned `--no-post-actions` skips all ordinary project actions without bypassing project configuration, constraints, or path validation. Preserve `--skip-npm-install` and `--skip-go-mod-tidy` with their original manager-specific scopes, not as aliases that silently skip unrelated actions. Report skipped intent in results and dry-run. Initial explicit project creation still runs approved ordinary actions by default without a new consent system, subject to command/security approval. `func-new-execution` must not add item dispatch until its separate caller/target contract is qualified.

Node initializer-time npm install is currently best-effort: a nonzero result leaves generated files without failing initialization. The proposed ordinary-action outcome intentionally changes that failure behavior, not only Go tidy's ignored result. Keep the Node initializer path until Node/command owners approve the new nonzero outcome, its default/skip choice and failure/cancellation cases are qualified, and migration notes call out the change. Do not describe the dispatcher as preserving legacy Node failure parity.

### Processor and packaging ownership

Processors are DI-owned internal services. File writes, tool/interpreter resolution, and processes use existing injectable boundaries. Required finalization is invoked by init, not registered as an optional generic engine post-action runner. Ordinary actions use a func dispatcher over the projected action plan, with no implicit fallback to a shell processor.

The central publisher validates IDs, arguments, raw-to-resolved references, conditions, paths, and effects through load/dry-run tests. It never executes configuration or restore actions while packaging. Synthesized Samples templates get one trusted configuration action per declared project and no ordinary actions. Authored templates retain source-owned declarations and must pass the same validation. Author-repository tests exercise real application restore/build/start/invocation; CLI tests own processor/ordering/failure contracts and fixture journeys.

## Alternatives Considered

- Register func processors for the engine restore action `210D431B-A78B-4D2F-B762-4ED3E3EA9025`. This may preserve upstream authoring interoperability, but its manager/argument/host semantics need a pinned-engine compatibility spike. Do not assume sharing a GUID preserves the func raw schema or safety contract.
- Use a func-specific restore ID. It gives the reviewed func-only schema an explicit identity but creates permanent authoring surface. The listed ID remains a proposal until the upstream-ID alternative is qualified and owners choose.
- Keep workload initializers as restore/tidy owners. This remains the fallback while engine mapping, actions, skip/failure compatibility, and security are unqualified. It avoids an early behavior break but cannot complete the intended template-owned scaffolding switch by itself.

Migration is complete only when each supported legacy journey has a qualified replacement, default/skip/failure/cancellation behavior has an approved disposition, retained topology and safety checks are proven on the pinned engine, and CLI sanity plus author-owned application evidence names the exact versions. Item journeys and unrelated host-file classification gaps remain blockers for their affected switches or samples, not silently removed coverage.

## Risks / Trade-offs

- Holding a selected snapshot requires engine mapping and store lease cooperation; prove the mapping on the pinned engine before implementation.
- Package restore can run hooks; fixed argv prevents shell injection but is not a sandbox for project code.
- Partial configuration or restore completion is observable; users need clear retry guidance, not an unsafe automatic rollback.
- Action IDs and the first processor set are proposed API surface; get author, command, publisher, and security input before shipping them.

## Migration Plan

1. Agree identities, raw primary-output references, restore scope, and failure defaults with the owners.
2. Spike raw-to-resolved mapping and retained evaluation values for conditions/renames on the pinned engine; reject unsupported unstable topology before mutation.
3. Add func-owned action plans and preflight, then mandatory configuration finalization.
4. Implement and test approved ordinary restore adapters and the separately reviewed Go module-tidy adapter through the existing process boundary, using validated absolute tools and launchers.
5. Update packaging to author/synthesize and validate the agreed actions without executing them.
6. Inventory standard actions in currently supported shared templates and agree translations or replacement packages before switching commands under their broader package, compatibility, and author-evidence gates.

Rollback restores the prior command path before migration completion. Unsupported action IDs stay blocked rather than falling back to arbitrary execution.