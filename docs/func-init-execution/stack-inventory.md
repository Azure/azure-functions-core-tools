# Installed stack metadata inventory

This is the additive metadata step for [#5648](https://github.com/Azure/azure-functions-core-tools/issues/5648).
The sections below describe existing initializers, not future template parameters.
See [PR 3 stack readiness](#pr-3-stack-readiness) for the package-backed HTTP
contribution boundary and [Sarah's first work item](#first-work-item-for-sarah).

## Current stacks and init options

Canonical stack IDs and language labels are preserved. Language aliases and
worker runtime aliases are separate: for example, `pwsh` is a language alias,
not an alias for `--stack powershell`.

| Stack ID / display name | Canonical languages (aliases) | Additional worker runtime aliases | Workload-contributed init options |
| --- | --- | --- | --- |
| `dotnet` / .NET | `C#` (`csharp`), `F#` (`fsharp`) | `dotnet-isolated` | `--target-framework` / `-tfm` (default and supported value: `net10.0`) |
| `node` / Node.js | `JavaScript` (`js`), `TypeScript` (`ts`) | None | Bundle options; `--skip-npm-install` (default: false) |
| `python` / Python | `Python` (`py`) | None | Bundle options |
| `go` / Go | `Go` (`golang`) | `native` | Bundle options; `--skip-go-mod-tidy` (default: false) |
| `java` / Java | `Java` (none) | None | Bundle options |
| `powershell` / PowerShell | `PowerShell` (`pwsh`, `ps`) | None | Bundle options; `--managed-dependencies` (default: false); `--runtime-version` (default: `7.4`; supported: `7.4`, `7.6`) |

Bundle options are shared through `CommonInitOptions` and `InitOptionRegistry`:
`--no-bundles` (default: false) and `--bundles-channel` / `-c`
(`Stable` by default, also `Preview` and `Experimental`). They appear once in
help even when multiple workloads contribute them.

`InitCommand` still owns `[path]`, `--stack` / `-s`, `--language` / `-l`,
`--name` / `-n`, and `--force`, in addition to inherited/global options.
Stack and language matching remains case-insensitive. Multi-language stacks
prompt for a language; single-language stacks select their sole language.
The .NET, Node and Python initializers also normalize language aliases when
called directly; their direct-call defaults are C#, JavaScript and Python.

Current scaffolding remains unchanged: .NET runs `dotnet new func` using
`Microsoft.Azure.Functions.Worker.ProjectTemplates` and a private template hive;
Node generates v4-model JS/TS files and optionally runs `npm install`;
Python generates v2-model files; Go writes worker SDK files and optionally runs
`go mod tidy`; Java writes Maven/sample HTTP-trigger files; PowerShell writes
profile/runtime files and optionally resolves the Az module for managed
dependencies. Non-.NET stacks write a default extension bundle unless disabled.

## Additive contract and catalog

`Abstractions/Projects/IProjectStack` is public because separately shipped
workloads implement it. Its only members are `Stack`, `DisplayName`,
`WorkerRuntimeAliases`, `SupportedLanguages`, and `SupportedLanguageAliases`.
It owns no command options, validators, template acquisition, or scaffolding.
Each stack registers a singleton metadata implementation alongside its existing
`IProjectInitializer`; the initializer delegates its metadata to that same
instance so the two contracts cannot drift.

The CLI registers `InstalledProjectStackCatalog` as a singleton over DI's
`IEnumerable<IProjectStack>`. It validates on resolution, not host startup,
rejecting duplicate stack IDs case-insensitively and ambiguous stack/runtime
or language/alias mappings rather than choosing the first registration.
Malformed metadata is an argument error; ownership conflicts are
`InvalidOperationException`s. `FindStack` accepts stack IDs/runtime aliases;
`GetCanonicalLanguage` accepts language labels/aliases; `GetStacksForLanguage`
returns **all** owners of that canonical language in registration order,
including owners that do not declare the particular alias supplied.
Lookups trim input and ignore case, return null/an empty list for unknown
values, and reject null/blank input. Metadata names must be nonblank and trimmed.

Existing consumers (`InitCommand`, template option hydration, and related
initializer registrations) stay on the old interface until the later command
switch. This step does not introduce template orchestration or initializer
fallback policy. The [quickstarts design](../func-init-quickstarts/design.md)
remains authoritative for future template-first, multi-project initialization.

## Release order

**Ship a CLI containing the new Abstractions contract before shipping any
stack workload that references `IProjectStack`.** These workload binaries are
not safe to load into an older CLI. CLI/workload compatibility gating
([#5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)) is
unresolved; this step does not add a gate. Release coordination must enforce
the order, and mixed old/new installations expose metadata only for workloads
that register the new contract while current initializer behavior is retained.

Template acquisition (#5649), first-party template publishing, runtime
integration, and the `func init` switch are separate work. The switch must wait
for usable template packages and its regression coverage; none is required
to register and inspect this metadata.

## PR 3 stack readiness

Evidence baseline: merged Foundation, PR #5767, commit
`664830e683a35dffb9747bbfcac48929c591451c`, inspected on October 29, 2026.
PR 2 changes are not included. The implementation and tests cited below are
evidence of existing initializer/provider behavior, not completed package-backed
HTTP enablement. None of these four stacks supplies production capability
reports at this baseline. Upstream package payloads were not available in this
checkout, so producer references and synthetic fixtures do not prove their
generated applications run.

The [stack enablement contract](../template-engine-integration/README.md#stack-enablement-contract)
defines ownership, content production, shared consumption, and qualification.

Delivery tracking, October 29, 2026: Sarah Vu owns the stack/content contribution;
Francisco owns shared PR 2 integration. PR 2's Node JavaScript/HTTP reference is
under implementation and is not merged. Package Contract v1 remains an
**unfrozen candidate**. Preview5 is the architecture demonstration target; full
catalog parity is not a prerequisite for the bounded contribution.
The RC1 target is **November 5, 2026**; the bug bash is **November 6, 2026**.

- **READY TO START**: bounded stack-owned work can begin from Foundation.
- **READY AFTER PR 2 REFERENCE**: shared-path reconciliation or qualification
  needs the stable Node/HTTP reference, not necessarily its merge.
- **REQUIRES STACK CONTRACT WORK**: settle the content layout or capability
  meaning before treating it as an implementable shared-path contribution.
- **BLOCKED BY MISSING EVIDENCE**: obtain the identified content or producer
  evidence before claiming that portion is ready.
- **OUT OF CURRENT SCOPE**: excluded variants or capabilities, not a claim that
  the stack can never support them.

| Stack | Stack-owned readiness | Main reason |
|---|---|---|
| .NET isolated | READY TO START | Native upstream inputs and test seams support bounded C# preparation. `net10.0` is the proposed target from the initializer default; RC1 framework confirmation and exact content validation remain pending. |
| Python | REQUIRES STACK CONTRACT WORK | The v2 HTTP fixtures include app-file append behavior. Establish a usable item layout within create-only publication before adapting it. |
| PowerShell | BLOCKED BY MISSING EVIDENCE | Project scaffolding exists, but an HTTP item source and native package producer were not found in the inspected workload tree. |
| Java | READY TO START | Checked-in Maven project and HTTP sample content support a bounded Maven-only contribution; native metadata, inspection, and package production remain work. |

Shared-path reconciliation for all four is **READY AFTER PR 2 REFERENCE** only
after their listed stack-owned gaps are closed; this is not implemented support.
F#, .NET in-process/output-only item generation, additional Python models, Java
Gradle/multi-module enablement, non-HTTP triggers, and catalog parity are
**OUT OF CURRENT SCOPE** for Sarah's first contribution.

### .NET isolated evidence and remaining work

- **Project/HTTP content and producer inputs:** the
  [initializer](../../src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs)
  uses `Microsoft.Azure.Functions.Worker.ProjectTemplates` with C#/F# and
  `net10.0`. It does not pin the project-template package version. The
  [existing item producer](../../src/Workloads/Templates/DotNet/README.DEV.md)
  projects `Microsoft.Azure.Functions.Worker.ItemTemplates` into a legacy
  catalog using the [source pin](../../src/Workloads/Templates/DotNet/Directory.Version.props)
  and [pack target](../../eng/build/Workloads/Workloads.Templates.DotNet.targets).
  The [HTTP provider fixtures](../../test/Func.Tests/Templates/DotNet/DotNetEngineProviderTests.cs)
  are synthetic, not qualification of the pinned isolated HTTP content.
- **Inspection/reporting:** the
  [factory](../../src/Workloads/Stacks/DotNet/DotNetProjectFactory.cs)
  recognizes project files and output directories. Templates-only inspection
  must establish a C# isolated source project, its target framework, and the
  Worker/SDK/HTTP dependencies required by the chosen item. A project-file
  extension or worker label is insufficient. Supply reports through the existing
  [project contract](../../src/Abstractions/Projects/FunctionsProject.cs).
- **Tests and missing dependencies:** extend
  [factory](../../test/Workloads/Stacks/DotNet.Tests/DotNetProjectFactoryTests.cs),
  [initializer](../../test/Workloads/Stacks/DotNet.Tests/DotNetProjectInitializerTests.cs),
  and [source-project](../../test/Workloads/Stacks/DotNet.Tests/DotNetSourceProjectTests.cs)
  coverage with inspection and native-content tests. Verify and pin the actual
  project/item inputs and matching HTTP dependencies. Generated-app evidence
  needs the .NET SDK, Azure Functions Runtime, and isolated worker dependencies;
  the current catalog is not the new package producer.

### Python evidence and remaining work

- **Project/HTTP content and producer inputs:** the
  [initializer](../../src/Workloads/Stacks/Python/PythonProjectInitializer.cs)
  embeds a v2 `FunctionApp` project. The
  [templates workload](../../src/Workloads/Templates/Python/README.DEV.md)
  takes language-filtered V2 jobs from extension-bundle `StaticContent`;
  [HTTP fixtures](../../test/Func.Tests/Templates/V2/V2TemplateEngineTests.cs)
  model create/append operations. These are not native package-backed templates.
- **Inspection/reporting:** the
  [factory](../../src/Workloads/Stacks/Python/PythonProjectFactory.cs)
  recognizes entry-point, dependency-manifest, lock-file, and Python-file
  fingerprints. The stack must define the supported v2 registration/file layout
  and dependency semantics, then inspect them and supply matching reports.
  Mere Python-file presence does not establish HTTP-item support.
- **Tests and missing dependencies:** preserve
  [stack tests](../../test/Workloads/Stacks/Python.Tests/) and
  [V2 projection coverage](../../test/Func.Tests/Templates/V2/V2TemplateProjectionTests.cs).
  Obtain the actual filtered HTTP payload and settle its native content
  adaptation without assuming app-file replacement is permitted. Qualification
  needs a supported Python interpreter, `azure-functions`, the Python worker,
  Azure Functions Runtime, and the selected project's bundle dependencies.

### PowerShell evidence and remaining work

- **Project/HTTP content and producer inputs:** the
  [initializer](../../src/Workloads/Stacks/PowerShell/PowerShellProjectInitializer.cs)
  supplies profile/settings content and optional managed dependencies.
  The inspected [template workload tree](../../src/Workloads/Templates/)
  has .NET, Node, and Python producers, not a PowerShell HTTP producer.
  Identify an owned HTTP source and its production steps before asserting
  package-backed HTTP readiness.
- **Inspection/reporting:** the
  [factory](../../src/Workloads/Stacks/PowerShell/PowerShellProjectFactory.cs)
  recognizes profile, dependency, script, and Modules fingerprints. The owner
  must supply the intended HTTP file/binding contract, define its capability
  meaning, and inspect required inputs; existing fingerprints are insufficient.
- **Tests and missing dependencies:** the
  [stack tests](../../test/Workloads/Stacks/PowerShell.Tests/) cover initialization,
  classification, and current project behavior, not native HTTP generation.
  HTTP content/metadata and package inputs are missing evidence. Execution also
  needs the supported PowerShell worker, Azure Functions Runtime, selected bundle,
  and any managed dependencies used by that content.

### Java evidence and remaining work

- **Project/HTTP content and producer inputs:** the
  [initializer](../../src/Workloads/Stacks/Java/JavaProjectInitializer.cs)
  embeds a [Maven project](../../src/Workloads/Stacks/Java/Templates/pom.xml)
  and an [HTTP sample](../../src/Workloads/Stacks/Java/Templates/Function.java).
  This is project scaffolding with an HTTP function, not a standalone native
  HTTP item producer. Extract a matching native project/item pair and metadata.
- **Inspection/reporting:** the
  [factory](../../src/Workloads/Stacks/Java/JavaProjectFactory.cs)
  recognizes Maven, Gradle, or a Java source tree, while
  [host preparation](../../src/Workloads/Stacks/Java/JavaFunctionsProject.cs)
  builds with Maven. A bounded contract should establish the selected Maven
  module, Java level, Functions library/plugin, and source layout before
  reporting support; a `pom.xml` alone is not sufficient.
- **Tests and missing dependencies:** extend
  [stack tests](../../test/Workloads/Stacks/Java.Tests/) with native content and
  inspection cases. Package production and capability definitions remain work.
  The current [stack prerequisites](../../src/Workloads/Stacks/Java/README.md#prerequisites)
  are JDK 21 and Maven 3.6+; execution additionally needs the Java worker,
  Azure Functions Runtime, and the project's bundle dependencies.

### Common PR 2 integration dependencies

Reconciliation needs the stable Node JavaScript/HTTP reference for shared intake
and native metadata adaptation, the Templates-only inspection call path, canonical
parameter parsing, the prepared operation, and create-only publication integration.
These are dependencies for shared-path qualification, not reasons to postpone
content and stack inspection work. Exact candidate package layout/build hooks
and the final handoff to those shared APIs remain unsettled. Do not treat older
installed-hive/lifecycle plans as completed PR 2 behavior.

## First work item for Sarah

**Title:** Add one package-backed C# .NET isolated project/HTTP-item pair.

**Owner and selected stack:** Sarah Vu; `dotnet`, C#, isolated source project.

**Proposed target framework and RC1 decision:** `net10.0` is the current
initializer default and proposed contribution target. That implementation
behavior does not establish RC1 product approval. Product confirmation must
cover the target framework and intended Azure Functions Runtime/worker cohort.
Framework-specific acceptance below is conditional on that decision. No other
framework is substituted here; framework-independent preparation can proceed.

**Problem:** Additional stacks need content and project capability evidence for
the shared Templates path. Existing .NET template invocation and synthetic HTTP
fixtures do not establish that the new package path can generate and run a
compatible application.

**Exact scope and deliverables:**

1. Verify and pin one native project input and one matching C# isolated HTTP
   item input from the identified Worker template producers. Supply repeatable
   candidate content production, native metadata, raw requirements, and an output
   inventory. Settle final package layout/build hooks against the stable reference.
2. Define the capability meaning for that single supported project/item shape
   alongside its content. Choose the production ID in the stack contribution
   under Foundation's existing convention; this work item invents no ID or HTTP
   parameters. Ensure requirements and inspection use the same definition.
3. Implement and test framework-independent stack inspection and immutable
   reports using the existing project contract. Keep strict validation
   Templates-only. Leave ordinary factory/classification and host-run behavior
   unchanged.
4. Add producer/content tests and stack inspection tests. Reconcile the stack
   adaptation with shared PR 2 APIs when stable, then provide stack-specific
   [qualification evidence](../template-engine-integration/README.md#qualification).
   Francisco owns shared integration changes; shared infrastructure/release
   owners retain shared publication and platform qualification.

**Authoritative repository references:** the
[stack enablement contract](../template-engine-integration/README.md#stack-enablement-contract),
[B1 scope and compatibility contract](../template-engine-constraints/design.md#templates-only-inspection-b1),
[compatibility scenarios](../template-engine-constraints/specs/template-constraints/spec.md),
and [.NET implementation, producer, and test evidence](#net-isolated-evidence-and-remaining-work)
above. Historical proposals do not replace merged Foundation.

**Acceptance criteria:**

- One native project and one matching HTTP item validate from recorded inputs.
  Enumerate and test the selected pair's actual parameters and defaults, including
  invalid/unknown arguments and unresolved required values.
- After RC1 framework confirmation, project generation yields the expected C#
  isolated files and HTTP dependencies for the confirmed target (currently
  proposed as `net10.0`). Item generation adds a new function without editing
  existing project or user files. Record expected outputs rather than relying on
  a successful legacy `dotnet new` invocation as a proxy for the shared CLI path.
- Production inspection establishes support only for the defined shape. Valid
  unsupported projects do not pass; malformed required inputs and I/O failures
  fail the Templates operation. Test cancellation, immutable capture, and
  content/report agreement. Preserve existing source/output classification tests,
  including recognized projects that do not establish the selected capability.
- The shared matcher rejects missing/Unsupported/conflicting reports and
  absent/invalid declarations before preparation. Rejection and collision cases
  do not overwrite existing content.
- For final qualification, demonstrate the representative journey against the
  actual installed release candidate: shared-path project and HTTP item
  generation, application restore/build/start, and an HTTP request with the
  expected response. Sarah supplies stack-specific content/application results;
  shared infrastructure/release owners retain shared filesystem, publication,
  and platform evidence under the
  [qualification contract](../template-engine-integration/README.md#qualification).

**Stage A - Can begin now:** identify and pin .NET isolated producer inputs,
inspect actual project and HTTP content, prepare native metadata, define
stack-owned capability semantics, implement framework-independent inspection,
and add producer/content and inspection tests. This work can start and be
reviewed from merged Foundation without RC1 framework approval or a PR 2 merge.

**Stage B - Requires dependencies:**

- Framework-specific acceptance needs product confirmation of the RC1 framework
  and intended Azure Functions Runtime/worker cohort.
- Final shared-path integration needs the stable PR 2 Node/HTTP reference and
  actual pinned template artifacts, not uncommitted PR 2 documents.
- Installed-release qualification needs the actual release candidate and the
  selected toolchain/Runtime/worker dependencies. Sarah contributes stack-specific
  generated-application evidence. Shared infrastructure/release owners supply
  shared publisher/filesystem and platform evidence, including Windows, Linux,
  macOS, and Unix atomic no-replace runtime validation.

Keep unqualified platforms and pending shared-path evidence explicit; do not
mark enablement complete on unit evidence alone.

**Explicit exclusions:** other stacks, F#, .NET in-process or output-only item
generation, additional frameworks/HTTP variants/triggers, full catalog parity,
shared PR 2 implementation, Foundation redesign, new capability states or central
registration, Package Contract v1 freeze, guarded force replacement, #5799/#5800,
Quickstart, remote acquisition, package-selection flags, and PR 4 implementation.

**Suggested PR target:** `feature/v5-templates`. Use Sarah's normal contributor
branch and workflow from merged Foundation. Reconcile with the stable PR 2
reference when available; do not branch from its dirty worktree. Submit this
single-stack contribution rather than a multi-stack implementation PR.
