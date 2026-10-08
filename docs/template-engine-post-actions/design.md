## Context

The integration owns one selected template, effects evaluation, and creation. Init owns destructive cleanup and mandatory `.func/config.json` finalization. Template authors own generated application behavior and end-to-end coverage. This design supplies declarations and processors without moving those responsibilities into template-controlled scripts.

## Goals / Non-Goals

Goals are explicit topology, preflight before mutation, atomic CLI configuration, useful dry-run effects, and substitutable restore adapters. Non-goals are general shell execution, SDK installation, arbitrary executable/argument pass-through, transactional rollback of generated projects, a new consent system, or running template code in the central packager.

## Decisions

### Trusted project configuration action

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

The index is a nonnegative invariant-culture integer string. Stack and language are required non-empty strings that resolve to supported canonical values. Extra arguments are rejected rather than treated as process inputs. Configuration actions cannot opt into continue-on-error or be overridden by an ordinary-action skip policy. Their conditions may suppress a project, but at least one configuration action must be active for project-template creation.

The raw index identifies the authored primary-output entry before condition filtering. Projection preserves that identity and binds it to the corresponding resolved output after conditions, source targets, `sourceName`, and filename renames. It must not index into the filtered resolved-output list, which could reference another project. An inactive, missing, ambiguous, or non-file reference makes an active configuration action invalid. An implementation spike against the pinned engine must demonstrate this mapping; manual string replacement is not a substitute.

Validate the raw `postActions` objects and arguments before engine normalization. The engine can stringify non-string argument values, so projected `"0"` cannot prove that the author supplied a string. Reject duplicate action/argument properties, incorrect raw types, malformed indexes, extra arguments, and invalid continue-on-error declarations in packaging and installed-template preflight. Configuration actions are project-template-only; an item template declaring one is rejected, not silently ignored.

The final project root is the parent of the resolved output, normally `host.json`. A template declares stack/language topology, but only the CLI serializes `.func/config.json`. Template content must never write or modify that file. Multiple projects can have different stacks and languages, and explicit init filters are checked against every active project.

### Initial ordinary package restore action

The proposed restore action ID is `17a32346-c721-42cd-a520-4d1cada22ba2`. Its arguments are `manager` and `primaryOutputIndex`. Proposed managers are `dotnet`, `npm`, `pip`, and `maven`, each behind its own injectable adapter. The primary output is the actual manager input: a project file, `package.json`, `requirements.txt`, or `pom.xml`. Root/output resolution follows the same snapshot rules as configuration actions.

Processors construct fixed argv from a CLI-selected executable and the validated input path, without template-supplied shell text. Examples are `dotnet restore <project>`, `npm install` in the package root, the selected Python interpreter's `-m pip install -r <requirements>`, and Maven dependency resolution in the POM root. Windows npm/Maven batch launchers require a separately reviewed CLI-owned launcher path with fixed switches and tested quoting; this exception does not permit arbitrary template arguments or shell fallback. The exact adapter commands, interpreter/tool resolution, and launcher policy are review points and require contract tests. Templates cannot provide an executable, shell fragment, extra arguments, environment overrides, credentials, or a path outside the invocation output root.

Restore may execute package-manager lifecycle hooks and access feeds. It is ordinary untrusted project work, not a trusted configuration operation. Existing init policy runs supported ordinary actions after mandatory finalization by default; this proposal does not add a consent flag. That behavior, supported managers, and the security boundary must be reviewed with the command/security owners. Missing tools produce actionable failures; processors do not provision them. Further processors, including package addition, need a separately reviewed adapter and schema.

### Shared preflight and effects

After final parameters are known, evaluate action and primary-output conditions in the selected template's session and create func-owned action plans. Keep raw action identity, declaration order, resolved input/root, processor kind, canonical stack/language where needed, and continue-on-error policy. No raw TemplateEngine objects cross into command rendering.

Before cleanup or creation, reject unsupported action IDs, invalid arguments, unresolved references, escaping or duplicate project roots, stack/language conflicts, direct configuration-file effects, and collisions among planned template and configuration writes. Path checks use resolved paths and an injectable filesystem boundary, including symlink/reparse-point checks before writes. A required configuration target must be a Functions project root; an ordinary restore input must be the supported manager's file. Shared tests cover conditions and renamed/multi-project outputs, not only literal paths.

Preflight and creation must share the evaluated parameter, bind, and generated-symbol values that determine action conditions, primary outputs, source targets, and renames. A configuration-file fingerprint alone is insufficient: unchanged declarations can evaluate differently on a second engine call. The pinned-engine spike must prove how a retained evaluation can be used for actual creation. If that cannot be guaranteed for a template's topology, reject that template before cleanup rather than repeat nondeterministic evaluation and discover a mismatch after destructive work. This is an implementation gate, not a claim that the current engine already supplies a reusable plan.

Current shared .NET item templates contain standard add-reference and file-action IDs. Before switching the old provider, inventory those declarations and either implement reviewed translations/adapters or publish qualified replacement templates preserving their behavior. No unknown action is silently executed or ignored, and blanket rejection cannot be used to claim existing templates remain supported. Package addition and file-opening behavior are migration review decisions separate from the proposed restore action; the command switch stays blocked until the supported/replaced matrix covers current behavior.

Dry-run uses the same plans and validation. It reports template effects, mandatory configuration writes, and ordinary restore intent in order, but creates no files, launches no process, and does not require tool availability merely to render a plan. Force only permits command-owned file cleanup/conflict handling; it does not bypass action safety.

### Execution ordering and failures

For project creation, init first preflights all active plans, performs authorized cleanup, creates the selected template, and finalizes configuration actions in declaration order. Before each write it verifies the resolved primary output exists and the target remains inside the invocation root. Each configuration write uses an atomic temporary-sibling replace. If any configuration action fails, stop remaining finalization and all ordinary actions, report completed projects and the failure, and preserve generated files and successful writes. There is no destructive rollback.

Only after all mandatory finalization succeeds do ordinary actions run in declaration order. Item creation has no project-configuration phase but uses the same ordinary action planner and safety rules. Immediately before each process launch, revalidate the actual input file and working directory for expected kind, containment, and links/reparse points; creation or earlier restore hooks may have changed them since preflight. A failed recheck launches nothing and is reported through the ordinary failure policy. An ordinary action with continue-on-error false stops later actions and returns failure; true records the failed action, continues, and still produces an overall nonzero outcome rather than claiming every action succeeded. Cancellation stops before further writes/processes and passes through each adapter. Already-started process cleanup follows the existing process runner contract, not a guarantee that arbitrary descendant processes or external package-manager state are atomically rolled back.

### Processor and packaging ownership

Processors are DI-owned internal services. File writes, tool/interpreter resolution, and processes use existing injectable boundaries. Required finalization is invoked by init, not registered as an optional generic engine post-action runner. Ordinary actions use a func dispatcher over the projected action plan, with no implicit fallback to a shell processor.

The central publisher validates IDs, arguments, raw-to-resolved references, conditions, paths, and effects through load/dry-run tests. It never executes configuration or restore actions while packaging. Synthesized Samples templates get one trusted configuration action per declared project and no ordinary actions. Authored templates retain source-owned declarations and must pass the same validation. Author-repository tests exercise real application restore/build/start/invocation; CLI tests own processor/ordering/failure contracts and fixture journeys.

## Risks / Trade-offs

- Holding a selected snapshot requires engine mapping and store lease cooperation; prove the mapping on the pinned engine before implementation.
- Package restore can run hooks; fixed argv prevents shell injection but is not a sandbox for project code.
- Partial configuration or restore completion is observable; users need clear retry guidance, not an unsafe automatic rollback.
- Action IDs and the first processor set are proposed API surface; get author, command, publisher, and security input before shipping them.

## Migration Plan

1. Agree identities, raw primary-output references, restore scope, and failure defaults with the owners.
2. Spike raw-to-resolved mapping and retained evaluation values for conditions/renames on the pinned engine; reject unsupported unstable topology before mutation.
3. Add func-owned action plans and preflight, then mandatory configuration finalization.
4. Implement and test approved ordinary restore adapters through the existing process boundary.
5. Update packaging to author/synthesize and validate the agreed actions without executing them.
6. Inventory standard actions in currently supported shared templates and agree translations or replacement packages before switching commands under their broader package, compatibility, and author-evidence gates.

Rollback restores the prior command path before migration completion. Unsupported action IDs stay blocked rather than falling back to arbitrary execution.