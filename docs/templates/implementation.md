# Template Discovery and Selection

PR #5872 implements the discovery and selection foundation described by the [component model](Templates-EndToEnd-Object-Model-Review.md). It does not create templates, write generated files, or integrate `func init` or `func new`.

## Composition and ownership

`ITemplaterFactory` creates one `TemplateEngineSession`, one internal `TemplateSelection`, and the command-scoped `Templater` from the existing `TemplateEngineContext`. The session owns the Microsoft.TemplateEngine environment, one `TemplatePackageManager`, the cached command inventory, and disposal. `Templater` owns the session, minimal CLI projection, exact candidate mapping, and candidate lifetime; it delegates matching to `TemplateSelection`, which delegates candidate eligibility to `TemplateEligibilityEvaluator`.

The session loads the installed package and template inventory once, then captures read-only collection snapshots while preserving exact engine object references. `TemplateSelection` consumes that candidate list and projection; it never reads candidate source files, creates another inventory, or refreshes aliases. `TemplateEligibilityEvaluator` performs candidate source reads and constraint evaluation through the same session. Selection accepts only an entry issued by that same live `Templater`; it does not substitute changed content. Installed registered content is immutable during the command session.

`TemplateCatalogProjector` maps engine metadata to minimum CLI fields and reads Functions host metadata. Unusable Functions metadata remains visible in listing with its diagnostic and original cause; it cannot be selected. Project and item classification uses the common `ITemplateMetadata` contract and `tags.type`. The projection filters empty aliases and marks a candidate without any non-empty `shortName` with a metadata diagnostic so it cannot be selected.

`TemplateSelection` is justified by the concrete requirement to keep matching and result policy separate from session/inventory ownership. `TemplateEligibilityEvaluator` is justified by the separate source-backed eligibility responsibility; its immediate caller is `TemplateSelection`. The evaluator's result enforces candidate diagnostic identity and cannot represent eligible-with-diagnostics or rejected-without-a-diagnostic. `Templater` directly consumes the selection operation, and both are composed with the same command-scoped session.

## Resolution behavior

`Templater` lists all projected entries of the requested `TemplateType`, including hidden entries. `TemplateSelection` applies resolution policy as follows:

- Full identity matches precede aliases. If multiple entries with one identity are exposed in the inventory, resolution fails; wrong-kind and ineligible exact identities fail without sibling substitution.
- Alias matches are checked for kind and candidate eligibility. No eligible candidates yield `Failed` with diagnostics; multiple eligible matches yield `Ambiguous`; one eligible match yields `Selected`.
- `Selected` retains the exact engine candidate in `ResolvedTemplate`, has no ambiguity candidates, and preserves non-fatal candidate diagnostics.
- A copied entry, an entry from another session, or a selection after session disposal is not a valid engine selection.

`TemplateResolutionResult` validates status/payload relationships, copies candidate and diagnostic inputs, and exposes runtime read-only collections. `TemplateCatalogEntry` also snapshots aliases, parameters, and nested choices. `NotFound` means no match appeared in the available inventory; it is not proof that every provider was enumerated successfully.

## Constraints and diagnostics

Raw declarations are read to detect malformed or duplicate engine constraints, verify correspondence with normalized engine metadata, and reject constraints that rely on additional configuration files when their merged declarations cannot be verified. Engine constraints are evaluated by `TemplateConstraintManager`; the CLI also verifies exactly one matching factory. Unavailable or restricted constraint providers fail only the candidate that needs them, with a structured diagnostic. Cancellation propagates.

`TemplateEligibilityEvaluator` uses `TemplateConstraintManager` for engine constraints and the lazy `FuncTemplateConstraintEvaluator` for the narrow `func-bundle` declaration. `func-workload` fails closed because this context has no workload inventory. PR #5872 does not interpret project Stack or language context: no production command consumes `ITemplaterFactory` in this change, so compatibility remains with the owning Stack and later command integration (PR #5878).

When an inventory exception is surfaced, listing propagates it and resolution returns it as the cause of an `inventory-unavailable` diagnostic. The inventory may not expose every conflicting registration as a distinct candidate; this layer does not scan mounts again to find duplicates. Immutable, versioned workload content must avoid identity conflicts. Template Engine may also log or suppress some provider enumeration errors and return a partial inventory. There is no supported typed completeness result for those suppressed errors; this layer does not parse logs or run another scanner to infer completeness.

## Tests and evidence boundaries

The focused unit coverage is in:

- `test/Func.Tests/Templates/Engine/TemplaterTests.cs` — one inventory, minimal projection, exact candidate retention, session ownership, and disposal.
- `test/Func.Tests/Templates/Engine/TemplateSelectionTests.cs` — identity/alias outcomes, ambiguity, the deferred Stack/language boundary, candidate-result invariants, and engine/Functions constraints.
- `test/Func.Tests/Templates/Engine/TemplateResolutionIntegrationTests.cs` — selection behavior through the command-facing facade.
- `test/Func.Tests/Templates/Engine/TemplaterFactoryTests.cs` — factory composition and lazy Functions constraint evaluation.
- `test/Func.Tests/Templates/Engine/TemplateCatalogProjectorTests.cs` — metadata projection, Functions metadata diagnostics, and immutable catalog snapshots.
- `test/Func.Tests/Templates/Engine/TemplateConstraintDeclarationReaderTests.cs` — declaration validation, including additional configuration files.
- `test/Func.Tests/Templates/Engine/TemplateResolutionResultTests.cs` — status invariants and immutable collection behavior.

On the Windows x64 candidate based on source commit `1d3555fac9a6c08ba385dcfe1f097b9402c8a12d`, using .NET SDK `10.0.403`:

- `dotnet build Azure.Functions.Cli.slnx --configuration Release --no-restore` succeeded with 0 warnings and 0 errors.
- The two raw-constraint regressions passed: raw constraint missing from engine metadata fails closed, and no raw or engine constraints remains eligible.
- `dotnet test test/Func.Tests/Func.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Templates.Engine" --logger "console;verbosity=normal"` passed: 306 passed, 0 failed, 0 skipped.
- The focused eligibility, selection, constraint-declaration, duplicate-factory, exact-identity, resolution-integration, duplicate-identity, session-lifetime, result-invariant, and factory/DI groups all passed. Their exact commands and counts are retained with the validation evidence.
- `dotnet test test/Func.Tests/Func.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal"` completed with 2,715 passed, 1 failed, 0 skipped (2,716 total). `Common.Processes.ProcessRunnerTests.RunAsync_Timeout_IsReported` failed because `TimedOut` was false; the same test and assertion failed in one targeted run on the clean unchanged baseline at the same source commit. The full suite is **FULL SUITE PARTIAL — VERIFIED BASELINE-MATCHED FAILURE**, not a pass.
- `git diff --check` passed.

These are build, unit-test, and whitespace-check evidence only. They do not establish production workload registration, a `func init` or `func new` journey, template creation, generated-file safety, Functions Host discovery, or supported-platform qualification. Host, Azurite, workload installation, `func init`, and `func new` were not run. Node.js is the first executable Host E2E journey; .NET is the second representative architecture and behavior check. Both belong to later integration work.

## Deferred responsibilities

No no-reference prompting API is introduced because no authorized command consumes it yet. A future command owns no-reference prompting, interactive choice, input interpretation, and diagnostics.

Template creation, parameter prompting or binding, task execution, and writing files are later integration work. This change defines no generation or file-writing mechanics. The existing command paths and Stack implementations remain unchanged until replacements qualify.
