# Template Discovery and Selection

PR #5872 implements the discovery and selection foundation described by the [component model](Templates-EndToEnd-Object-Model-Review.md). The downstream PR #5878 candidate integrates that foundation with a bounded .NET project and item-template path; it does not claim broader Stack or platform qualification.

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

## PR #5872 foundation evidence

The discovery and selection foundation has focused unit coverage in:

- `test/Func.Tests/Templates/Engine/TemplaterTests.cs` — one inventory, minimal projection, exact candidate retention, session ownership, and disposal.
- `test/Func.Tests/Templates/Engine/TemplateSelectionTests.cs` — identity/alias outcomes, ambiguity, the deferred Stack/language boundary, candidate-result invariants, and engine/Functions constraints.
- `test/Func.Tests/Templates/Engine/TemplateResolutionIntegrationTests.cs` — selection behavior through the command-facing facade.
- `test/Func.Tests/Templates/Engine/TemplaterFactoryTests.cs` — factory composition and lazy Functions constraint evaluation.
- `test/Func.Tests/Templates/Engine/TemplateCatalogProjectorTests.cs` — metadata projection, Functions metadata diagnostics, and immutable catalog snapshots.
- `test/Func.Tests/Templates/Engine/TemplateConstraintDeclarationReaderTests.cs` — declaration validation, including additional configuration files.
- `test/Func.Tests/Templates/Engine/TemplateResolutionResultTests.cs` — status invariants and immutable collection behavior.

On the Windows x64 PR #5872 foundation candidate based on source commit `1d3555fac9a6c08ba385dcfe1f097b9402c8a12d`, using .NET SDK `10.0.403`:

- `dotnet build Azure.Functions.Cli.slnx --configuration Release --no-restore` succeeded with 0 warnings and 0 errors.
- The two raw-constraint regressions passed: raw constraint missing from engine metadata fails closed, and no raw or engine constraints remains eligible.
- `dotnet test test/Func.Tests/Func.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Templates.Engine" --logger "console;verbosity=normal"` passed: 306 passed, 0 failed, 0 skipped.
- The focused eligibility, selection, constraint-declaration, duplicate-factory, exact-identity, resolution-integration, duplicate-identity, session-lifetime, result-invariant, and factory/DI groups all passed. Their exact commands and counts are retained with the validation evidence.
- `dotnet test test/Func.Tests/Func.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal"` completed with 2,715 passed, 1 failed, 0 skipped (2,716 total). `Common.Processes.ProcessRunnerTests.RunAsync_Timeout_IsReported` failed because `TimedOut` was false; the same test and assertion failed in one targeted run on the clean unchanged baseline at the same source commit. The full suite is **FULL SUITE PARTIAL — VERIFIED BASELINE-MATCHED FAILURE**, not a pass.
- `git diff --check` passed.

These are historical build, unit-test, and whitespace-check results for the discovery foundation, not validation of the PR #5878 integrated candidate. They do not establish production workload registration, generation, guarded file publication, a Functions Host journey, or supported-platform qualification.

## Deferred responsibilities

## PR #5878 .NET integration candidate

The integrated candidate retains the PR #5872 session, cached inventory, projection, exact-candidate selection, and eligibility responsibilities. `Templater` consumes the single command-scoped inventory and delegates matching; it no longer maintains a parallel mount scanner or reconstructs selected templates from catalog metadata.

`TemplaterFactory.CreateAsync` captures registered local NuGet archives into bounded private operation state before constructing the operation's generation session. Microsoft.TemplateEngine then uses the staged registration for inventory, invalid/duplicate checks, exact resolution, parameter evaluation, and private generation. The operation does not reopen the original registered archives. Archive validation enforces content/path limits but does not parse template semantics.

`Templater.RenderAsync` passes the exact `ResolvedTemplate` engine candidate from the same live session to Microsoft.TemplateEngine's `TemplateCreator`. Generated files and evaluated task metadata remain private until command-owned preflight authorizes effects. The .NET workload interprets supported task metadata and project state; CLI Commands orchestrate tasks and guarded Write Files to Project publication. Unsupported project/package mutations fail closed. Existing initializers remain in place for routes not yet qualified.

This candidate is limited to the representative C# isolated `net10.0` project and HTTP-item content, its explicitly registered local packages, and the previously qualified platform boundary. It does not solve incomplete provider enumeration: `NotFound` still does not prove that every provider was enumerated. Unknown compatibility remains fail-closed, and Node.js/other Stack implementation and runtime journeys remain gated.

## Integrated candidate validation

On the Windows x64 candidate with .NET SDK `10.0.403`, using an isolated `DOTNET_CLI_HOME` and NuGet package cache:

- Release solution build passed with 0 warnings and 0 errors.
- Template Engine tests passed: 348 passed, 0 failed, 0 skipped.
- .NET workload tests passed: 97 passed, 0 failed, 0 skipped.
- `dotnet test test/Func.Tests/Func.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~InitCommandTemplateTests|FullyQualifiedName~NewCommandTests|FullyQualifiedName~Projects.FileWriting" --logger "console;verbosity=minimal"` passed: 100 passed, 0 failed, 0 skipped. The representative journey installed the local .NET workload, ran `func init` and HTTP `func new`, restored and built the generated project, started the Functions Host, and successfully invoked the HTTP function. It also verified registered template-package identities remained unchanged.
- `git diff --check` passed.

The initial end-to-end rerun exposed missing legacy WorkerExtensions dependencies in the isolated local package source. Those dependencies were staged in the session-local NuGet cache and the full focused run then passed; the test's offline source behavior was not changed. The earlier SDK first-run ASP.NET Core HTTPS development-certificate side effect remains an environment-validation gap. It was not removed, modified, trusted, or repeated; subsequent CLI execution disabled certificate generation and used isolated CLI state.

This is focused validation, not a full-suite result or qualification of other platforms, Node.js, or remaining Stacks. The existing certificate state also remains outside the scope of this candidate.

No no-reference prompting API is introduced; CLI Commands retain ownership of prompting, interaction, and diagnostics.
