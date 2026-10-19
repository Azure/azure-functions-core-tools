# Engine-first architecture acceptance

This is a blocking acceptance gate alongside, not a replacement for, original-to-current public template parity. Calling `TemplateCreator`, reducing file counts, or passing tests does not establish that the surrounding integration is minimal.

**Audit baseline:** `5a1dcc189df912ff99c56fe5c0253bfb15b24d9b`, tree `5bfd56d45be4d7bb77119684c2504fb021753f3e`. The separate original, already-native migration, and hardening epochs retain their existing identities. The findings below do not establish original public parity.

**Current disposition: open, with an additive owned-native path qualified.** Explicit native, effect-free Functions definitions can use the selected physical owner's verified archive without a new CLI template identity list. Markerless historical packages keep their existing compatibility path. The remaining legacy conversion and effectful/language-adapter migrations are not accepted waivers; their disposition remains part of this gate. The separately frozen selector increment is `99a97e64`.

## Acceptance conditions

1. Map each custom responsibility to a native definition, a reusable pinned engine/host facility, shared CLI policy, or a justified language adapter. Identify duplicated semantic knowledge explicitly.
2. Keep supported rendering transformations in template definitions. Legacy conversion may run at pack time; it must not require a synchronized runtime implementation of the same transformation.
3. Keep context, ownership, source authority, recovery, path safety, cancellation, preview, effect authorization, and Functions configuration shared. Keep Python append and F# project semantics in narrow adapters.
4. Qualify reusable facilities against the actual package version, distinguishing engine libraries from the `dotnet new` host. Do not enable scripts to reduce glue.
5. Exercise an ordinary added template through real workload installation and public invocation. A successful catalog listing is not successful generation. Record the precise remaining limitation if invocation fails.
6. Associate each bounded change with a falsifying before case, unchanged compatibility controls, a pinned successor, and separate read-only review. Original public parity remains independently blocking.

## Responsibility matrix

Paths are repository-relative. **N** = native definition; **E** = reusable pinned engine facility; **H** = shared CLI policy; **L** = language-specific adapter. Mixed rows separate legitimate policy from incidental coupling rather than declaring all current glue necessary.

| Responsibility | Controlling implementation or definition | Classification and disposition |
|---|---|---|
| File rendering and native effects | `src/Func/Templates/Engine/TemplateEngineSession.cs`: `InstantiateAsync`, `CaptureNativePreparationAsync`, `InvokeAsync`; pinned `TemplateCreator.InstantiateAsync` | **E**. One shared generator path; private expansion also invokes the engine, not a stack renderer. Repeated preview/private expansion/actual creation are distinct authorization phases, not a reusable frozen engine plan. |
| Node output placement | `src/Workloads/Stacks/Node/NativeTemplates/content/http-js/.template.config/template.json`, `content/http-ts/.template.config/template.json`; `Convert-NodeItemTemplates.ps1`: authored `sources` | **N**. Conditioned source targets own legacy `src/functions` versus literal-output placement. `FuncTemplateEngineHost.cs` binds immutable `func:item-output-intent`. Do not move placement into the provider. |
| Output stability and paths | `src/Func/Templates/Engine/TemplateOutputPathValidator.cs`: `GetDiagnostic`; `TemplateEngineSession.cs`: `ValidateOutputPaths`, `MatchesOutputSources` | **H**, using **E** config models, value forms and expression dependency discovery. The current integration rejects non-parameter topology because separate native phases reevaluate symbols. This is a conservative integration restriction, not absence of native derived/computed/generated/bind capabilities. |
| Names and authored defaults | Native `sourceName`, `defaultName`, `preferDefaultName`, symbols/forms; `NewExecutionRunner.cs`; `NativeItemCompatibility.cs`; `Node/NodeItemCompatibility.cs`; `Python/PythonTemplateSourceReader.cs` | **N/E** for naming/rendering; **H/L** for CLI selection and preserved legacy validators/default contracts. Node/Python reproject original sources and compare defaults/choices against native metadata. This is duplicated contract knowledge, not another output renderer; removing it requires a replacement source-authority and compatibility proof. |
| Parameters, choices and required values | `Engine/TemplateParameterProjector.cs`; `TemplateArgumentParser.cs`; `TemplateExecutionInteraction.cs`; `TemplateEngineSession.cs`: `CreateInputData`, `GetPopulatedParameters` | **E** mounted parameter definitions and native conversion; **H** strict argv provenance, aliases, prompting, pre-write validation. The topology validator's parameter-population projection is custom glue to audit on engine upgrades, not a general parameter evaluator. Original regex validation remains in the legacy compatibility adapter; no equivalent replacement has been qualified here. |
| Availability and constraints | Native `constraints`; `Engine/FuncTemplateConstraintFactory.cs`, `FuncTemplateConstraintEvaluator.cs`, `TemplateConstraintDeclarationReader.cs`, `TemplateConstraintEvaluationSnapshot.cs` | **N/E** constraint declarations and engine dispatch; **H** actual captured installed workload/bundle/binding facts. Do not replace resolved ownership/bundle context with user template values. |
| Node legacy conversion | `src/Workloads/Stacks/Node/NativeTemplates/Convert-NodeItemTemplates.ps1`; `src/Func/Templates/Node/NodeItemSourceQualifier.cs`: `Validate`, `QualifyParameters`, `AuthorDaprHelpers`, `PropertyMapping`, `ValidateReviewedBody` | **N/L** at pack time; runtime source qualification is **H/L**. **Open duplication:** both sides know property/literal associations, Dapr transformations and reviewed body hashes. Runtime reconstructs converted bytes to verify source association even though it does not write them. This still violates the desired single-authoring direction; retain guards until an approved authoritative representation replaces this two-implementation proof. |
| Python conversion and metadata | `src/Workloads/Stacks/Python/NativeTemplates/Convert-PythonTemplates.ps1`; `src/Func/Templates/Python/PythonTemplateSourceReader.cs`: `ReadAsync`, `ResolveTarget`, `ValidateChannelsAndBindings` | **N/L** pack conversion; **H/L** runtime provenance/legacy qualification. **Open coupling:** runtime requires the fixed 25-original/50-original-helper inventory, corrected/hidden identities, target conventions, symbols/forms equality, channels and source inventory. Definitions are not yet independently extensible. |
| .NET source authoring and qualification | `src/Workloads/Stacks/DotNet/NativeTemplates/stage-templates.ps1`, `stage-qualified-source.ps1`, `stage-binding-literals.ps1`; `src/Func/Templates/DotNet/DotNetTemplateSources.cs`, `.Auxiliary.cs`; `src/Func/Func.csproj` embedded resources | **N/L** pack-time authoring; **H/L** old/current source authorization. Native derived forms encode literals; no runtime escaping renderer is introduced. **Open coupling:** embedded `upstream-configurations.json`, `binding-literals*.json`, `auxiliary-identities.json`, `fsharp-control-sources.json` duplicate package inventories and require CLI rebuilds for new qualified identities/source cohorts. Reusing the same JSON as build/runtime input avoids a second data list, but does not make the package independently authoritative. |
| Auxiliary function identities | `DotNet/DotNetAuxiliaryNameValidation.cs`: `Validate`; `Node/NodeItemSourceQualifier.cs`: `ValidateDaprInvocation`; pack-time auxiliary/Dapr authoring | **N** owns emitted function IDs/routes; **H/L** validates Functions naming and bounded language source shapes. **Open coupling:** six .NET identity-specific declaration/attribute/suffix patterns and two Node helper identities mirror template semantics. These are output validators, not writers, but adding another shape can still require CLI code. A native output path is not the generated Functions function ID, so replacing this with filename validation would weaken the contract. |
| Evaluated post-actions | Native `postActions`; `NativeTemplateEffectPlanner.cs`: `PlanDependencies`; `NativeTemplateEffectWorkflow.cs`: `ExecuteAsync` | **N/E** for declaration, conditions and returned action arguments; **H** for typed allowlisting, preflight, confirmation, cancellation and partial outcomes. The referenced engine libraries do not execute these actions. Unknown actions remain refusals. |
| .NET references and restore | `src/Func/Templates/DotNet/DotNetTemplateEffectCapability.cs`; `src/Workloads/Stacks/DotNet/Templates/DotNetTemplateDependencyExecutor.cs` | **L**, invoked through shared typed effects. Exact project, source identity/hash, package/version and extension are checked. The `dotnet new` add/restore processors are SDK-host facilities, not processors supplied by the pinned engine references. No arbitrary command dispatcher is justified. |
| Python append | `Python/PythonSelectedItemEffects.cs`, `PythonAppendCapability.cs`, `PythonAppendTargetGuard.cs`; `NativeSelectedItemAppendSupport.cs`; `src/Workloads/Stacks/Python/PythonTemplateAppendExecutor.cs`, `PythonFunctionNameGuard.cs` | **L**, with shared **H/E** preview and native snippet expansion. Existing app bytes, expected target digest, duplicate function analysis, newline boundary, path and partial-failure semantics require an adapter. The native generator's overwrite model is not a qualified append transaction. Do not put Python semantics in a generic renderer. |
| F# compile ordering | `DotNet/DotNetTemplateWorkflowProvider.cs`; `NativeTemplateEffectPlanner.cs`: `PlanFSharpItemDependencies`; `TemplateEngineSession.cs`: `ValidateFSharpTargetsAsync`; `src/Workloads/Stacks/DotNet/Templates/FSharpCompileUpdater.cs`, `FSharpProjectFiles.cs` | **L**, mediated by shared typed effects. Exact evaluated direct compile order, terminal `Program.fs`, before/after source/project hashes and forced replacement position are qualified. Generic add-file-to-project behavior is not proof of these semantics. Some F# boundary checks remain in the shared session; extracting them alone would not remove the inventory coupling or qualify a broader grammar. |
| Stack preparation | `Node/NodeProjectPreparation.cs`; `Go/GoNativeProjectPreparation.cs`; `PowerShell/PowerShellNativeProjectPreparation.cs`; `Java/JavaProjectWorkflowProvider.cs`; `Projects/OwnedProjectPreparationSource.cs` | **L** npm, Go tidy, PowerShell optional managed dependency preparation; Java has no new Maven execution. **H** shared source checks. Exact identities/source inventories and repeated bound preview plans constrain reuse. Preserve the distinct effect order and source rechecks; do not replace these with unrestricted scripts or a giant generic preparation class. |
| Functions project configuration | `Engine/TemplateProjectConfigurationPlanner.cs`, `TemplateProjectConfigurationWriter.cs`; native action `00e33184-ffc7-43ef-bec0-b1684df8ad56` | **H**, declared via **N** post-actions. Canonical installed stack/language, independently generated host root and create-only `.func/config.json` are host-owned. Current project topology subset and special qualified .NET layout are integration restrictions, not native engine limits. |
| Selection and workflow composition | `InitExecutionRunner.cs`, `NewExecutionRunner.cs`, `NativeTemplateWorkflowRegistry.cs`, `NativeTemplateWorkflowServiceCollectionExtensions.cs`; `NativeEntryCommandBinding.cs` | **H**, with **L** adapters. Public project selection previously discarded a template reference entirely. The bounded correction wires `init --template` into existing shared selection and reserves it from template parameters. Provider/source policies remain unchanged. |
| Acquisition, ownership and recovery | `NativeTemplateWorkloadCapture.cs`; `Engine/NativeTemplatePackageScope.cs`, `TemplateStore.cs`, `TemplateStoreWriteSession.cs`; `src/Func/Workloads/Install/BundledTemplateInstaller.cs` | **H**. Genuine package installation/registration receipts, active physical owner, exact archive hashes, store leases/recovery and no implicit acquisition remain authoritative. They are not template rendering responsibilities. |

## Pinned capability evidence

`eng/build/Packages.props` pins `Microsoft.TemplateEngine.Abstractions`, `Edge` and `Orchestrator.RunnableProjects` to **10.0.301**. Executed test assemblies report product version `10.0.301+96856fd726ffd058fb3dfef0851dbafc7ef3b011`. The installed SDK is 10.0.402 and supplies a different engine build; running `dotnet new` alone would not qualify the pinned libraries.

The cached package nuspec repository commit is VMR `96856fd726ffd058fb3dfef0851dbafc7ef3b011`. Its [source manifest](https://github.com/dotnet/dotnet/blob/96856fd726ffd058fb3dfef0851dbafc7ef3b011/src/source-manifest.json) identifies:

- Engine: [`dotnet/templating@a397da1b5747f31a7d91d024609c6e529679b694`](https://github.com/dotnet/templating/tree/a397da1b5747f31a7d91d024609c6e529679b694).
- SDK host: [`dotnet/sdk@8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5`](https://github.com/dotnet/sdk/tree/8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5).

| Capability | Pinned evidence and qualification |
|---|---|
| Native source mapping, parameters, derived forms and source conditions | `TemplateDefinitionCapabilityTests.InvokeAsync_NativeDefinition_OwnsDefaultsDerivedContentPlacementAndConditionalFiles` executes the referenced 10.0.301 libraries. Independently asserted default and explicit content, authored filename/default name, target directory, conditional file set, read-only preview and actual file effects require no identity-specific renderer. This is direct-engine integration evidence, not a public workload admission claim. |
| Post-action declarations | Engine [`IPostAction`](https://github.com/dotnet/templating/blob/a397da1b5747f31a7d91d024609c6e529679b694/src/Microsoft.TemplateEngine.Abstractions/IPostAction.cs) and [`PostAction.Evaluate`](https://github.com/dotnet/templating/blob/a397da1b5747f31a7d91d024609c6e529679b694/src/Microsoft.TemplateEngine.Orchestrator.RunnableProjects/PostAction.cs) return evaluated declarations. `IPostAction` explicitly distinguishes a host that lacks the associated processor. |
| Dispatch and generic processors | SDK [`Microsoft.TemplateEngine.Cli/Components.cs`](https://github.com/dotnet/sdk/blob/8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5/src/Cli/Microsoft.TemplateEngine.Cli/Components.cs) registers chmod, instruction display, process-start and JSON-property processors; [`PostActionDispatcher.cs`](https://github.com/dotnet/sdk/blob/8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5/src/Cli/Microsoft.TemplateEngine.Cli/PostActionDispatcher.cs) executes them. This CLI package is not referenced by func. Adding it would not grant source/effect authorization; process-start execution is not enabled. |
| Add reference and restore | SDK-host [`DotnetAddPostActionProcessor`](https://github.com/dotnet/sdk/blob/8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5/src/Cli/dotnet/Commands/New/PostActions/DotnetAddPostActionProcessor.cs) (`B17581D1-C5C9-4489-8F0A-004BE667B814`) and [`DotnetRestorePostActionProcessor`](https://github.com/dotnet/sdk/blob/8ece2d6b49edf91f2eed52867ab8a8f48ac8a2e5/src/Cli/dotnet/Commands/New/PostActions/DotnetRestorePostActionProcessor.cs) (`210D431B-A78B-4D2F-B762-4ED3E3EA9025`) are internal host implementations, not part of func's pinned engine package surface. Their existence does not justify removing func's exact-project capability checks. |
| Editor and add-file-to-project behavior | No editor processor was found in the two pinned-era processor registration directories examined. This is not an exhaustive claim about other hosts. No reusable pinned processor was qualified for exact Python append or F# compile-order/byte-preservation semantics. Keep those adapters; absence of a qualified replacement is narrower than claiming the engine cannot represent any such action. |

## Public package-only falsifier and bounded correction

The retained local probe derives a Node workload from the genuine hardening package. It adds an ordinary item and project using native capabilities, updates the bundled template inventory and archive digest, and installs through **actual `func workload install`** in isolated homes. It does not fabricate a legacy source map, ownership receipt, public original catalog or producer identity. Existing workload assemblies and legacy provenance remain unmodified; the derivative archive is explicitly not the original package.

Observed before/after:

- Real installation and public item listing succeed. The added item's preview and apply both fail `invalidLegacyInput`: no unique original Node source mapping. Project files are preserved. Native capability and catalog admission therefore do **not** prove public package-only generation.
- Adding the project creates a genuine second project family. Hardening 5a reports `ambiguousGroups` and instructs use of `--template`, but that option was not exposed or forwarded by its public init binding. The same exact-identity and alias commands succeed after the shared selector correction.
- Selecting the new project after the correction reaches `workflowUnavailable`: no CLI-owned project provider qualifies its identity. No target directory is created. This is a demonstrated provider restriction.
- An ordinary Node initialization using automatic template selection, the original package, and the same explicit project name/options succeeds before and after with identical generated file bytes. This controls the selector's omitted-reference path, not every default or option. These are hardening-to-successor compatibility controls, not original-to-current public parity.

**Intentional public behavior change:** `func init --template <installed-identity-or-short-name>` now selects through the existing native group resolver. It does not install templates, override stack/language/constraints, invoke internal helpers, bypass source qualification, or authorize a new provider. Unknown and item-type references fail before target creation. The compatibility adapter uses the same host option rather than a second selector.

**Precise remaining limitation:** a package author cannot add the demonstrated ordinary Node item or project and have it execute without satisfying the CLI's closed legacy/source/provider contracts. Python and .NET have additional traced inventory constraints listed above; equivalent public extension probes for those stacks were not run. Declaring the native engine unable to render them would be incorrect.

## Additive owned-native source contract

The original falsifier above records the selector-only boundary, not the final
native capability. A definition can now explicitly declare:

```json
{
  "tags": {
    "type": "item",
    "language": "JavaScript",
    "func-generation": "native"
  },
  "constraints": {
    "host": { "type": "host", "args": [{ "hostname": "func" }] },
    "stack": { "type": "func-workload", "args": "node" }
  }
}
```

This one classification is not a rendering or effect DSL. The actual sources,
symbols, forms, conditions and output paths remain native definitions. The
classification alone is insufficient:

- `NativeTemplatePackageScope.ReceiptBoundPin` and
  `TemplateEngineSession.VerifiedNativePackage` expose only an active physical
  asset whose existing registration receipt and entire archive hash were
  validated. No folder, unowned package, inferred owner or inactive cohort gains
  this authority.
- `Engine/NativeTemplateGenerationContract.cs` binds the mounted identity,
  canonical type/language, exclusive `FuncTemplate` package, func-only host and
  matching stack declaration to that proof. `NativeTemplateWorkflowRegistry`
  independently matches the asset to the actual installed stack's captured
  physical owner and supported language.
- The existing engine session revalidates the same whole archive, configuration
  and host content before invocation. Whole-archive equality covers every native
  source; a second runtime list of expected rendered bodies is not its content
  authority. Native items cannot write the reserved `.func` namespace, including
  case and trailing-dot/space aliases, even with force.
- Native item declarations cannot grant post-action dispatch, Python append,
  restore, arbitrary scripts or F# compile integration. F# effect-free items and
  additional configuration files remain explicit refusals. Native projects still
  require the existing mandatory configuration declaration, host-root closure
  and create-only host writer; the classification does not request npm or other
  implicit preparation.

`NewExecutionRunner` uses the common parser, name policy, preview and invocation
for this contract. It does not call a per-template renderer. Contextual help uses
the same authored native parameters rather than an unrelated legacy default
projection. A qualified native definition does not require a stack-specific item
provider. When no such provider exists, only qualified native definitions enter
the item catalog/selection path; a markerless stack without that capability keeps
its previous unavailable-workflow result. This is covered by controlled
composition, not a claim that new public Go/Java/Python catalogs were exercised.

For a Node native definition that retains an original mapping,
`NodeItemCompatibility` still verifies its package/version, configuration, host,
content and original metadata descriptors and applies source-derived original
defaults/validators. It no longer reconstructs the pack-time body transformation
for that explicitly native definition. The separately registered
`INativeItemValidationProvider` carries only this validation, never output
placement or rendering. Removing an optional generation adapter does not remove
the required validator; both native-only success and mapped/helper refusals are
checked in that composition. A native-only
definition need not invent historical provenance; malformed or duplicate claimed
mappings are failures, not fallbacks. Markerless packages retain the entire
previous conversion qualifier. Mapping detection and mapped-source reading share
strict recursive provenance parsing, so duplicate members cannot discard a
claimed validator. Mapped Dapr helpers retain their complete existing
source-shape qualification, not merely comparisons with known unsafe bodies;
their actual generated helper identities/routes remain a narrow language/legacy
exception.

This is an additive pack contract, not a blanket relabeling of the existing
catalog. A mapped template opting in must retain its original validation mapping
and pass the independent compatibility gate. Existing shipped converters and
projects are not automatically switched, and effects requiring language adapters
are not silently omitted. Existing direct-engine controls without owner receipts
must not be made green by fabricating owner proof.

The new public probe uses legitimate Node v4 `@azure/functions` / `app.http`
content, native name/auth/string symbols, a conditional file and a real
host/runtime/package scaffold with the mandatory Functions configuration action.
It adds different identities than the repository tests, through a genuine
workload install with real ownership receipts. The same package is exercised
against frozen H2 and the additive candidate: H2's item/source and project/provider
refusals become native preview/create success. Authored defaults, explicit
authorization/string values, template-owned placement and conditional files are
checked, with local Node syntax checks rather than execution of generated code.
A reclassified original HTTP definition retains its original name validator and
unchanged generated body; an altered content descriptor, changed retained
archive, reserved configuration target and unauthorized action remain refusals.
These are bounded H2-to-successor/capability controls, not original public parity
or live Functions host/indexing proof.

## Remaining work and evidence boundaries

Removing the duplicate Node conversion proof, fixed Python inventory, or embedded .NET qualification inventories is not a mechanical deletion. A successor design must identify the authoritative package representation and maintain exact source association, old-cohort compatibility, owner selection, effect authorization and genuine public parity without implementing a second runtime converter. This audit does not silently replace those guarantees with trust in self-declared metadata.

The selector and additive native source contract are deliberately bounded. No broad renderer/provider rewrite, dependency upgrade, arbitrary script dispatch, deletion of historical source guards, CI retry, permission/network change or protected PR/ref/review/Ready/merge operation is included.

The local evidence packet records exact CLI/package hashes, argv, isolated homes, stdout/stderr, ownership receipt hashes and source/test inventories. Intermediate fixture failures are retained separately: console encoding, initial ambiguous control selection, a refused same-version changed-archive replacement, and an undeclared option on the custom project. They are not counted as successful architecture qualification.

The architecture gate remains open until its remaining duplicated contracts have an accepted disposition and public extension requirements are met or precisely accepted as limitations. The separate public-parity owner must reconcile genuine original catalogs and comparisons; neither the derivative package probe nor these green controls closes that gate. Linux/macOS, live Functions hosts, worker indexing and original catalogs were not exercised in this architecture workstream.
