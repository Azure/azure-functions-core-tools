# Installed stack metadata inventory

This is the additive metadata step for [#5648](https://github.com/Azure/azure-functions-core-tools/issues/5648).
It describes current `vnext` initializers, not the future template parameters.

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

## Local command/workflow parity bridge

`NativeTemplateCommandAdapter` is an **unregistered parity harness**, not the
public route. It composes fresh existing `InitCommand` / `NewCommand`
definitions with the native execution runners. Both retain their current
optional `[path]`; the public `new` positional is not interpreted as a template.
Preview and init's template selection/noninteractive/JSON settings are internal
harness inputs, not new CLI flags. No initializer or legacy provider is invoked
after native generation.

`NativeTemplateCommandInput` retains original argv separately from the native
scalar input. It does not reconstruct tokens from `NewInvocation.UserOptionValues`.
Only an existing workload Boolean option's implicit value is made explicit;
aliases, inline spelling, duplicate occurrences and diagnostic source indices
are retained. Workload options remain owned by `InitOptionRegistry`; unsupported
preparation options fail native validation instead of being consumed and ignored.
`NativeTemplateHelpProjector` uses the strict parser's effective aliases without
attaching options to a singleton command.

The optional item compatibility hook resolves the name only after a native
candidate is selected. An explicit name wins over an original-catalog default,
then the native authored `defaultName`; missing defaults prompt or refuse rather
than inventing an output-directory name. The hook retains explicit/default/prompt
origin and applies source-qualified legacy regex rules without sanitizing input.
Its selected-entry contract and owned-source reader must be explicitly supplied;
the source bytes are hashed and compared before validation. The controlled hook
bounds regex evaluation to one second and reports timeouts as validation failures;
this operational timing difference must be qualified before public activation.
Native direct
invocations without this hook and project name behavior are unchanged.

### Remaining composition gates

| Boundary | Required behavior before any public switch |
| --- | --- |
| Python items | Preserve the original job's create-target selection independently from append target selection. The optional selected-item hook renders an exact source-bound hidden helper through the same native session into owned temporary output before planning append; never force-overwrite the app. Real Python asset/owned-executor composition remains a separate gate. The raw catalog's blueprint branches are not automatically CLI-selected. |
| .NET items | Use actual MSBuild-evaluated framework/namespace and native-evaluated, ordered package-reference/restore actions against an exact project. Source generation alone is not `dotnet new` parity; package addition itself can restore. Preserve inherited secondary-restore failures rather than claiming every original action succeeds. |
| .NET projects | Preserve workload-qualified framework, source-name and layout conventions against exact upstream inputs. The owning workload must supply a source declaration before catalog eligibility. Missing required policy refuses rather than falling back to generic creation. |
| Node initialization | Preserve default `npm install` and `--skip-npm-install`. Host preparation is not a substitute: `SkipBuild` still permits dependency installation. |
| Go initialization | Preserve default `go mod tidy` and its skip option. The old initializer ignores exit/stderr; the staged workflow must report nonzero/partial results explicitly before activation. |
| Java initialization | Generate the existing Maven project; do not add a Maven invocation to init. |
| PowerShell initialization | Validate runtime `7.4`/`7.6` before writes. Managed dependencies default off; when enabled, prepare create-only `requirements.psd1` from owned source. Failed optional Az-major lookup retains the commented placeholder with an explicit diagnostic. |
| Item eligibility | Actual bundle ID/version/capabilities and project/profile targeting remain authoritative. Binding capability composition and exact original name/prompt contracts must be qualified for each source. |
| Force/configuration | Keep current refusal, adoption, repair and user-owned source/config preservation controls. Destructive init force remains unqualified by the staged runner. |

`NativeTemplateEffectPlanner` consumes native **evaluated** actions, not raw
conditions. Its internal plans cover append, source-qualified create-only
content, exact package references/restores and an explicit editor no-op.
Unknown actions reject the entire plan even when `continueOnError` is set.
`NativeTemplateEffectWorkflow` requires an explicitly injected capability that
preflights every source hash, target snapshot, path and occupancy before mutation
and rechecks each effect at execution. Preview/refusal never execute effects;
partial failures retain completed/failed effects and diagnostics. Continuing an
action does not turn its failure into success. No production capability is
registered, default native-session denial remains intact, and no arbitrary shell,
transactional rollback or universal concurrency guarantee is introduced.

`TemplateInvocationRequest.EffectSupport` is an explicit internal opt-in for
generic ITEM invocations and bounded, explicitly source-qualified project
layouts. Native preview supplies evaluated actions; all effects are planned and
preflighted before native generation, confirmation is required, and effects run
only after generated files pass the existing path/kind checks. Effect failure
retains native file changes and structured partial diagnostics. Default item
post-action denial and project topology/action qualification remain unchanged.

The optional append hook binds original/helper configuration fingerprints,
package identity/version, owned mapping/create/helper source hashes, and one
expected helper output. It uses the same canonical arguments, effective name,
context and native invocation path, rejects extra outputs, and invokes no
effect hooks while expanding the helper. An authored `func-role` tag is a
deny-only signal: ordinary item runners reject internal roles even without an
append capability. It never authorizes expansion. Exact source-bound role
mapping is still required, while ordinary legacy hidden items remain supported
by explicit identity.

`InitExecutionRequest.ProjectPreparation` is a separate optional owned
capability. It receives the selected resolved source, canonical parameters,
target and actual native preview; it plans only fixed Node npm, Go tidy or
PowerShell managed-dependency operations for the matching single project stack.
Preview does not complete preparation. Completion runs only after native files
and mandatory configuration succeed, and a nonzero owned-helper outcome reports
`partialPreparation` with the created project retained. Skip decisions remain
explicit. This does not register a new command/action ABI, authorize arbitrary
template executables, or add Java Maven execution. Concrete stack helper
composition is separately qualified; the old Go ignored-exit behavior remains
an intentional activation decision rather than a claimed compatibility fix.

`TemplateInvocationRequest.MsBuildBindings` forwards an explicitly supplied,
already-evaluated immutable snapshot through the native `IBindSymbolSource`
component API. It must match the exact selected item, current mounted
configuration fingerprint and controlled project/effect boundary before native
preview. Controlled item effects with `msbuild:` bindings refuse a missing
snapshot rather than use the template's framework default. A session that has
accepted a snapshot refuses absent or replacement snapshots on later
invocations. This adds no default component registration, project discovery,
SDK evaluation or framework inference; the owned .NET preparation helper remains
responsible for evaluating and validating the supplied project properties.

Cancellation of owned preparation still propagates the original
`OperationCanceledException` and token. After verified native/configuration
success, that phase emits an existing stderr warning that created outputs were
preserved and preparation may be incomplete; it does not return success, invent
a completed-effects result or claim rollback. Pre-create and default no-prep
cancellation are unchanged.

### Workload-owned native policy

`FunctionsCliBuilder.AddNativeTemplatePolicy<TPolicy>()` registers the single
shared boundary in `Abstractions/Templates/Native`. The host captures the actual
loaded runtime owner. A stack name, matching version, source prefix or
workload-created source object is not ownership authority. Admission validates
the selected physical runtime and its loaded policy/stack, installed payload,
native archive and retained provenance. External content retains the schema 2
selected logical/physical stack, channel, source, Functions CLI native host ABI,
required role and exact receipt/artifact digest checks. Runtime and content
versions need not be equal.

For an owned project, `ValidateProjectAsync` runs before the optional
`DescribeProjectLayoutAsync`. A required layout declaration supplies the
authored primary-output index and installed canonical stack/language before
catalog listing, resolution and init eligibility. The host replays that same
declaration for creation and joins it to the actual captured regular output and
host root. Capture cannot invent replacement catalog metadata. Legitimate
unhandled generic templates keep their existing metadata path.

The native policy registry, policy instances, templater factory and NEW runner
share the existing command scope. Every invocation, including preview and
create, receives fresh lifetime-bound source and captured-image views. Captured
canonical values include completed defaults, strict reparsing, explicit option
provenance and the accepted project binding. Returned collections are copied.
Package-relative original source hashes and generated byte hashes remain
distinct. Source reads and revalidation expire with the invocation.
Default reparsing retains original raw explicit input and host prompt answers,
passing only non-explicit canonical values as completions. Layout association
uses the captured raw configuration fingerprint, not a hash reconstructed from
decoded JSON, so an unchanged encoding BOM does not invalidate its source pin.

Append remains a separate typed workload execution transport. Package, restore,
compile and create-only methods qualify existing roles but do not execute them.
Those roles require the exact existing host-composed internal executor and
confirmation privately paired to the same owner and invocation, or refuse
before the first native/configuration install. A plain request capability or a
clean qualifier result is insufficient. No production loaded non-append
executor is registered in this layer. Editor no-op and append ledger success
do not imply a native/configuration physical commit.

The additive builder method and policy methods retain explicit unsupported
defaults for old compiled builders and original eight-type policies on the new
host. A workload using the new boundary still requires a host containing it.
There is no reverse compatibility claim, all-stack policy migration, public
activation or production publisher qualification.

### Workload-owned project preparation

`NativeTemplatePolicy.RequiresProjectPreparation` declares a required phase for
the selected source. `CreateProjectPreparation` returns the workload's
`INativeTemplateProjectPreparation` implementation. Both members are optional
for old compiled policies. An unrequested phase defaults to false, while a
required missing or unsupported capability refuses before output installation.
An owned refusal cannot fall back to request preparation or another provider.

The host binds the exact loaded capability to its runtime owner, selected source,
command invocation and one frozen preview plan. Required invocation/capture
guards apply even when the policy requests no stages. Each phase gets fresh
lifetime-bound views over copied native bytes and final canonical values.
The copies outlive disposal of the native output image, not its streams or
delegates. Direct creation cannot omit a declared preparation phase.

The owning workload plans skip behavior in `PreviewAsync` and executes its own
helper in `CompleteAsync`. Core contains no language command selection,
executable/argument port, npm executor or public process callback. The existing
internal legacy preparation operations keep their compatibility guards and
are not authority for this new owned path.

Completion receives a host-issued `NativeTemplateCreatedProject` only after
actual native and mandatory configuration success. It exposes the actual
captured output, canonical configuration path and copied finalized configuration
bytes. Revalidation checks the owner/source, current generated file kinds and
bytes, unlinked target and unchanged configuration. This is not atomic against
external changes and does not constrain subsequent workload helper mutations.
Completion authorization is consumed once before the workload callback.
The first configuration read must exactly match the successful writer's
canonical byte receipt. Matching runtime/language alone cannot adopt a replaced
configuration as CLI-created evidence.

The typed workload result preserves diagnostics and `TargetMayHaveChanged`.
Its `Succeeded` value is authoritative, not keywords in diagnostic strings.
Returned success or failure is recorded before late cancellation. Failure is
`partialPreparation` with retained project/configuration and nonzero exit.
Thrown cancellation preserves the original exception and retained-output warning.
Skipped preparation remains explicit and does not claim a process ran.
No logical plan, skipped result or helper outcome creates a native physical
commit marker. Production Node policy/helper qualification remains with its
workload owner, and the public native route remains off.

### Bounded F# workflow repair

The internal, explicit `NativeTemplateInvocationEffects.FSharpRepair` contract
carries a selected-source/configuration fingerprint, exact `.fsproj` and
expected project digest, an owner-qualified evaluated root-file compile
sequence and hashed native-generated `.fs` files. The qualified planner
matches those files against actual native `Create` effects and evaluated primary
outputs, then places a required compile-update effect before package/restore
actions. Unknown actions, mismatched source/project/file sets, overwrites and
unqualified sequences refuse the whole plan. Preview reports the compile plan
without applying it; update failure prevents later dependency operations and
does not erase generated outputs.

Insertion remains immediately before the last owner-qualified evaluated
include, which is excluded from generated-file additions. This is not
permission for a nonterminal insertion or a different project rewrite.
The owned updater validates the actual MSBuild sequence, project/source
digests and supported XML/file shape and performs the surgical project edit.
The shared seam neither edits project files nor infers a project by scanning.
The pinned F# restore action has no arguments; only the explicit selected
`.fsproj` is restored by the controlled path. Restore actions with `files`,
`targetFiles` or other arguments remain unhandled, not broadly reinterpreted.
This opt-in repair is an approved intentional correction to the characterized
upstream omission, not a claim that the old workflow compiled the generated
function. Public commands, default registrations and no-capability denial stay
unchanged.
