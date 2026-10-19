# Bundled native Node templates

This first-party authoring slice ships `Azure.Functions.Templates.Node.Basic`
**inside** the Node stack workload. Its version follows the stack build.
It is not an independently released companion, NuGet dependency, or feed lookup.
The package ID is an authoring choice, not a trust assertion.

`dotnet pack src/Workloads/Stacks/Node/Workloads.Node.csproj -c Release`
uses the existing workload pack pipeline. The physical archive contains:

* `tools/any/Azure.Functions.Cli.Workloads.Node.dll`
* `tools/any/templates/node-basic.nupkg`
* root `bundled-templates.json`

The current Node stack is RID-independent. For RID workloads, bundled bytes
belong in the selected physical implementation's `tools/<rid>/` payload, never
the logical pointer. Installed paths retain these archive-relative paths.
A normal pointer/implementation install can make several requests; bundling
eliminates an **additional template-specific download**, not all but one request.

## Source and content

The projects reuse the existing `../Templates/` package files, tsconfig,
local settings, and ignore files at pack time. They preserve Node programming
model v4, package scripts and dependency ranges, JavaScript/TypeScript `main`
paths, the empty `src/functions` directory, and stable/preview/experimental
bundle choices. Native deterministic forms trim/lowercase the project name,
replace spaces, remove characters outside npm's ASCII name alphabet, and use
`function-app` for an empty normalized name. ASCII filtering is slightly stricter
than the legacy initializer's Unicode `char.IsLetterOrDigit` predicate.

The owned HTTP source is
`src/Workloads/Templates/Node/content/v2/templates/templates.json` at the
published parent `1445698680ee1774b66cca90b3009d377bb8bfd2`:
`HttpTrigger-JavaScript` and `HttpTrigger-TypeScript`. Despite that catalog's
`v2` envelope label, both payloads already use Node programming model **v4**
(`app.http`), and their write actions target `src/functions/<name>.js|ts`.
The conversion replaces `$(FUNCTION_NAME_INPUT)` with the native `sourceName`
token and exposes authorization as a choice parameter. It preserves GET/POST,
query/body/default name precedence, logging and greeting response, and the
typed handler export. The same payloads are corroborated by the owned
`main` `node-v4-templates.json` catalog at
`831ae149a99f54aab6c8b88d32710b9d390d2216`, with its `%functionName%` token.
No model-v3 `function.json` is emitted. The inner archive carries the
repository MIT `LICENSE` and this provenance document.

Both project variants share `basic`; both item variants share `http`.
Languages remain `JavaScript` / `TypeScript` with installed-stack `js` / `ts`
aliases. Project parameters are `--no-bundles true` and
`--bundles-channel Stable|Preview|Experimental`. HTTP items expose
`--authlevel anonymous|function|admin` and use the command's `--name`.
Run `new` at the project's `src/functions` directory so the existing `main`
glob picks up the generated item.

## Complete owned item catalog

`Convert-NodeItemTemplates.ps1` projects the owned Node
`Templates/Node/content/v2` source at pack time into native item configurations
and source files under `content/items/` in the same inner archive. It reads
the existing jobs, prompts, resources and `_bindings.json`; there is no second
maintained item catalog, template feed, or separate release. Changes to the
job/action shape or missing binding/prompt metadata fail the pack.
The existing basic projects and HTTP payloads/options are unchanged. HTTP
metadata additionally declares its legacy IDs and display-name alias.

The b2a2f295 source contains 33 items: 18 JavaScript and 15 TypeScript. All are
carried, not claimed as 33 visible choices in every channel. Six preserve the
legacy hidden policy: TypeScript Blob/Event Grid Blob and both languages'
Durable entity/orchestrator templates. Hidden entries remain addressable by
their exact legacy IDs. New native items retain those IDs, display-name aliases,
default function names and canonical installed-stack languages. No TypeScript
Dapr variants or other absent entries are synthesized. Existing HTTP identities
remain `Azure.Functions.Node.Http.JavaScript|TypeScript`, with legacy IDs as
source-declared aliases.

Non-name parameters retain the original prompt IDs, kebab-case option aliases,
required flags, choices and resource descriptions. Native authoring
now makes all 14 distinct advertised non-name prompts operative for JavaScript
and TypeScript: Timer schedule; Queue connection/name; Blob and Event Grid
Blob connection/path; Event Hub name; Service Bus connection/queue/topic/
subscription; and the existing Cosmos connection/database/collection/lease
properties. This intentionally repairs inherited raw payloads that ignored
those inputs. `native-defaults.json` explicitly authors the JavaScript and TypeScript omitted
entity-name defaults to preserve the old generated targets: Event Hub
`samples-workitems`, Service Bus queue `myinputqueue`, and Service Bus topic
`mytopic` / subscription `mysubscription`. The converter emits these as native
symbol defaults, so help, canonical argument values and generation agree.
Explicit values, including accepted empty strings, remain operative. Other
defaults, connection values (including TypeScript Event Hub's `%connection%`
literal) and Dapr policy are unchanged.

The converter replaces exactly one reviewed property declaration associated
with the original template kind, prompt ID, assignment token and literal.
Before replacement, both the converter and runtime qualifier verify a reviewed
fingerprint of the entire raw body for each parameterized item. Only CRLF/LF
representation differences are normalized. Unexpected body edits, including
comments, template literals and unrelated objects, require source-association
review and updated reviewed fingerprints; a matching line alone grants no
substitution authority. Normal owned comments and unrelated literals remain
unchanged. Missing, changed or ambiguous declarations fail packing.
Derived native symbols JSON-escape string
content and escape apostrophes before insertion into single-quoted JavaScript
strings. The lease choice is emitted as a lowercase Boolean literal, never a
quoted string. These generated symbols are not extra user parameters. This
does not introduce CRON validation, change Cosmos API property names, supply
unadvertised Event Hub connections, add dependencies or run shell actions.
MCP resource/tool registration names remain fixed literals. The command-owned
`--name` replaces the original function-name input where the payload uses it.
The native parameter projection reserves `name` for the command; it does not
carry the legacy function-name regex. The baseline staged facade also supplies
the output-directory name as the native fallback when `--name` is omitted.
Neither policy is changed globally. The unused command bridge can explicitly
compose `NodeItemCompatibility` to obtain the original item's effective name
and regex validation from its selected packaged source, with explicit input
winning over the authored default.

Extension-dependent items declare the existing `func-bundle` identity/version
requirements plus a **provisional** `func-bundle-bindings` requirement containing
the exact source binding list. Bundle channel names alone are not evidence that
a binding exists. The default runtime does not register this additional
constraint, so these items deliberately fail closed unless a private harness explicitly enables
binding evaluation and reads the actual selected bundle's local
`bin/extensions.json`. Capabilities belong to the immutable resolved command
bundle, not the workload snapshot. HTTP and timer have no extension binding
dependency. This is not a stable authoring ABI, default activation, or a claim
that a published channel has the same bindings as a private fixture.

`BundledNodeCatalogTests.cs` compares the physical package against the owned
source, reopens all native descriptors and prompt metadata, and renders every
raw item with the actual native backend into private existing-project fixtures.
The backend authoring check is deliberately separate from facade eligibility;
it does not bypass the production facade or claim an unavailable item is
workflow-qualified. Separate facade checks cover timer preview/create, conflict
refusal/source preservation and binding-dependent unavailability.

## Host-approved conversion output

`Export-SourceQualification.ps1` exports a reviewable source association from
explicit, already-built physical Node workload archives. It verifies the inner
archive digest and original/configuration/host/payload descriptors before
recording original input pins, complete native definitions and normalized payload hashes.
It does not convert, render, install or execute template content. For example:

```powershell
.\Export-SourceQualification.ps1 -WorkloadArchives @('<reviewed-workload.nupkg>') -OutputPath '<qualified-sources.json>'
```

The CLI embeds the reviewed `qualified-sources.json`; it never loads a replacement
from a workload or user path. Matching requires the selected original source
pins, original model, exact source paths, complete native configuration/host
semantics and payload. Native source/host property order is insignificant;
payload comparison retains only the existing line-ending equivalence.
The selected archive/configuration and actual source hashes are still checked.
Adding a package's own assertion cannot change the embedded authority.

Matching approved conversions need no runtime property replacement or Dapr body
reconstruction. Native definitions alone render values, names and placement.
Existing original prompt/default/choice validation remains independent of this
source proof, and Dapr reserved names and auxiliary-name length limits still
apply. Unmatched historical packages retain their unchanged compatibility path;
the frozen legacy qualifier must not acquire a parallel implementation of each
new pack-time rule.

This host inventory exists only to preserve the stronger historical claim that
a converted definition corresponds to the reviewed original source. A workload
receipt proves ownership and unchanged bytes, not that conversion relationship.
It is not enrollment for ordinary `func-generation=native` templates: those use
the shared owner/archive contract without a CLI identity list. Newly generated
qualification data require source/output review and focused compatibility
evidence; running the exporter is not semantic approval.

## Selected-source compatibility in the unused bridge

The inner archive additionally contains the byte-exact original
`templates.json`, `_bindings.json`, `userPrompts.json` and `Resources.json`
under `provenance/node/v2/`. A Node-local pack-generated
`provenance/node/compatibility.json` binds each original ID to its native
identity, package ID/version, configuration, host metadata and payload hashes.
The six JavaScript/TypeScript entity-template mappings additionally pin the byte-exact
`provenance/node/native-defaults.json` authoring source. Each changed default
must match both its original prompt default and its authored native value;
other parameters and packages without this descriptor retain the original
default comparison. The runtime does not maintain a per-template default table.
These are internal qualification records, not a second catalog, release,
user-supplied metadata policy or stable authoring ABI.

The unregistered, CLI-internal `NodeItemCompatibility` provider reads those
records only through the exact selected `ResolvedTemplate`'s mounted-package
reader. It verifies the selected configuration fingerprint and actual bytes,
reuses the original V2 prompt projection, and delegates validation/name origin
to the shared compatibility helper. It never locates another archive by
package ID/version, trusts hash syntax without reading bytes, changes core
name fallback, or invokes the legacy V2 file-writing backend.

`BundledNodeBridgeTests.cs` composes that provider with the real unused
`NativeTemplateCommandAdapter`, installed Node stack, protected native store,
and selected-bundle metadata reader. Private literal extension metadata tests
all-required, case-insensitive binding checks and fresh source capture across
stable/preview/experimental identities; they do not equate the old pack-time
latest-channel filter with the selected runtime bundle. The bridge tests
exercise all owned items' omitted/explicit names, display/ID aliases, hidden
explicit IDs, original regex rejection and existing-source preservation.
The canonical-options check separately executes the physical legacy engine
to retain evidence of its unchanged ignored Queue inputs, then verifies the
intentional native parameter behavior. Missing or altered mapped provenance, native
payloads, package versions and configuration hashes refuse before generation.
Public command routing and default service registration remain unchanged.

`NodeTemplateWorkflowProvider` adapts the same CLI-owned item compatibility and
project preparation to the shared invocation-scoped workflow contracts. Its
project ownership check selects only the existing native Node package and
canonical JavaScript/TypeScript project identities; the adapters still verify
the actual selected archive bytes. Composition must supply the CLI-compiled
`NodeNpmInstaller`, not the physical workload's same-named assembly type.
The provider does not register itself, alter command routing, or add a second
template catalog.

`BundledNodeWorkflowCompositionTests.cs` explicitly composes the production
workflow registry, composer, native runners, captured workload availability and
canonical context resolver against the installed Node archive. Requests leave
their compatibility, effects and preparation fields unset: the registry selects
the Node provider from the installed stack catalog. The same scoped provider
supplies both interfaces, and a separate scope receives a new instance.
The factory-created command adapter retains the existing flag surface.
These checks substitute only npm execution and private bundle inventory;
they do not replace provider selection with test-only capability delegates.
The earlier real-npm and HTTP qualification remains evidence for its pinned
`2438be76` source, not a new runtime claim for this composition increment.

The explicit `NativeTemplateWorkflowHostFactory.CreateAsync` boot path is also
qualified directly, including its captured successful-workload stack filter.
`BundledNodeExplicitBootBoundaryTests.cs` records the unchanged literal-output
contract: an item requested at the project root writes `httpTrigger.js|ts`
at that root, unlike the owned legacy `WriteToFile` action's
`src/functions/$(FUNCTION_NAME_INPUT).js|ts` target. That root output is outside
the project's JavaScript `src/functions/*.js` or TypeScript
`dist/src/functions/*.js` main glob. An explicitly nested `src/functions`
invocation works with the original default name. The tests preserve both
requested paths; they do not redirect the root request, change template bytes,
or establish public project-root `func new` placement parity by themselves.

## Template-owned item layout

The native `template.json` owns two mutually exclusive `sources` mappings.
`InvocationOutputIntent` binds to the internal generic host parameter
`host:func:item-output-intent`: `legacy` selects the original write directory,
and any absent intent selects the literal `./` mapping. The command context
supplies the validated `literal` or `legacy` value before opening the template
session; it is not a user parameter, public option or environment setting.
The converter derives the static legacy target from the owned raw write action
at pack time. The CLI does not calculate a Node output directory or evaluate
the source conditions; the native engine does both from the authored config.

`NodeItemSourceQualifier` retains the original single-job/action/body
association and native configuration/payload checks without selecting a
target. It independently reconstructs the reviewed property canonicalization
and verifies its exact derived symbols and escaping forms. Raw source proof
remains byte-exact and hash-bound; only the authored native body contains
parameter placeholders. A changed raw declaration, assignment, escaping form,
symbol source or native body refuses qualification rather than weakening the
raw/native equality check. `NodeTemplateWorkflowProvider` supplies a directory-free generic
source snapshot in both modes: exact selected identity/configuration hash,
manifest, config, host, native body and all four original source-file pins,
plus the expected native regular-file source. Shared engine guards revalidate
these pins before preview and creation and reject extra/wrong-kind outputs.
The exact historical encoding without forms or derived substitutions is also
qualified through the same body fingerprints, graph, layout and selected-source
pins. It can execute only when **every effective parameter** equals the
independently reviewed raw operative literal. Thus an older Timer's default
schedule remains usable without an update; a custom schedule it cannot apply
refuses before writes with explicit same-owner workload-update guidance.
Event Hub and Service Bus advertised defaults that disagree with their raw
literals do not qualify by omission. Mixed encodings and unsupported inputs
never fall back to silently ignored parameters. There is no version/latest
allowlist, automatic workload update or native-cache migration.
The runtime Node directory planner and typed placement override are removed.
`LiteralOutput` remains the default for staged/direct-native callers.

The legacy-relative base is always the original command directory, including
omitted root, explicit root and explicit nested directories. No folder suffix
deduplication or re-anchoring to the discovered project root occurs. Thus a
public-style nested base retains the old nested `src/functions/<name>.js|ts`
formula, while an explicitly literal nested output remains literal.
`BundledNodeRelativePlacementTests.cs` exercises actual native writes and
preview agreement, unchanged entry globs, file/reparse-point prefix refusal,
user-file conflicts, cancellation and changed mounted raw-source refusal.
These typed-intent checks do not activate the public command route.
The internal staged JSON `outputDirectory` now means the native invocation
base (the original command directory), not a CLI-derived target parent.
Nested `invocation.changes` paths identify the actual authored destinations.
This is an intentional internal result-semantics change; property names are
unchanged.

### Shared item-name aliases

`func new --trigger-function-name <Name>` is an operative alias of `--name` and `-n` on the shared item command. It changes the native filename and generated function name, including JavaScript and TypeScript Timer registrations. This intentionally fixes the old advertised spelling that was accepted without changing the name. Omitted names still use the authored default. Supplying the name more than once, including mixed spellings, follows the existing single-value rejection rules. No legacy precedence or ignored-option behavior is introduced.

### Name-scoped Node Dapr helpers

Newly authored JavaScript Dapr Publish items register the requested primary
name and `<Name>-Topic`; Service Invocation items register the requested
primary name and `<Name>-Invoke`. The service helper route is
`<Name>/invoke/{appId}/{methodName}`. Both the Dapr CLI caller and HTTP caller
examples use the generated primary name, and the HTTP example includes the
same route prefix. Exact requested ASCII casing is preserved through the native
`sourceName` substitution. Existing generic log messages, Dapr topic/pubsub,
binding `appId`/`methodName` placeholders, payloads, HTTP methods and
authorization remain unchanged.

These two original raw bodies are pinned in full before exact helper/caller/
route conversions, separately from their byte-exact vendor provenance.
Partial conversions, extra replacement symbols or unreviewed body edits do
not qualify. No TypeScript Dapr entries are synthesized. Different requested
primary names produce different helpers and helper routes. The helper-only
hyphen separates these IDs from primary names allowed by the unchanged owned
Node validator, preventing ordinary CLI HTTP/Dapr primary names such as
`Foo_Invoke` from colliding with `Foo-Invoke`. This is not a
project-wide JavaScript registration or arbitrary user-code collision scan.
Existing source files and routes are never rewritten.

The existing 128-character function-name contract applies to generated helper
IDs too: Publish primary names are limited to 122 characters and Service
primary names to 121. Preview and creation reject longer names before writes,
with a shorter-name instruction; names are never truncated or hashed.
The reserved primary name `host` is also rejected case-insensitively before
writes for these affected items.

These are intentional compatibility changes from the fixed sample helper names `DaprTopicTriggerNodeV4` and `InvokeOutputBinding` and the unprefixed Service route `invoke/{appId}/{methodName}`. Existing projects are not migrated or rewritten by this change. Other function-name restrictions, parameter behavior, SDK registration and backend qualification remain separate.

Older native archives containing the fixed helper IDs or fixed service route,
and the earlier underscore-derived encoding without primary/helper separation,
cannot meet this new repeat-safe helper contract. Selecting **either of these
two items**, even for its first/default name, now requires an explicit update
of the captured owning workload. No update occurs during generation. This
intentional selected-feature incompatibility does not block old project
initialization, HTTP items, matching-literal Timer items or other compatible
templates. It does not silently retain duplicate helpers, scan/execute user
JavaScript, impose a latest-version gate or migrate native caches.

`NodeStagedEntryPlacementTests.cs` additionally exercises the explicit
`NativeCliEntryPoint` full argv/boot/parser path. Existing public `[path]`
syntax receives legacy-relative placement, including a nested base already
named `src/functions` without deduplication; the staged `NewExecutionCommand`
`--path` surface remains literal. Prefix files and a linked prefix are
refused as structured failures without falling back to the legacy engine.
`Program` retains its internal `Legacy` default: this staged entry proof is
not permission to switch the default route.

## Staged creation, not a public command switch

Project templates use the **draft** configuration action
`00e33184-ffc7-43ef-bec0-b1684df8ad56` with literal `host.json` primary output,
`node` stack, and explicit canonical language. This GUID and argument schema
are provisional staging contracts, not a stable production authoring ABI.
The existing protected runtime/configuration guards are unchanged.

`InitExecutionCommand` and `NewExecutionCommand` can preview/create these
templates with CLI-owned configuration finalization. They remain unregistered;
the public `func init` / `func new` continue their existing paths. This package
does not claim M3/M4 production completion or qualify other runtimes.

Ordinary template-authored creation post-actions remain unsupported by the
staged project path. A private bridge harness can explicitly supply
`NodeProjectPreparation`, which plans the fixed `NodeNpmInstall` operation and
honors the canonical `SkipNpmInstall` parameter (`--skip-npm-install`).
Preview never starts npm; completion uses the existing owned npm helper only
after native project files and mandatory CLI configuration exist. Nonzero
results retain those files, preserve stderr, and report `partialPreparation`.
Cancellation emits a retained-output warning, then propagates without claiming
completion or rollback.
No preparation provider is registered by default, and no arbitrary command
string is accepted from a template.

The Node initializer's npm process logic is extracted once into internal
`NodeNpmInstaller.cs`, compiled by the Node workload and source-linked by the
CLI. The legacy delegate, best-effort result behavior, process arguments and
cancellation/error semantics remain unchanged; there is no runtime reference
between the CLI and physical stack assemblies. Controlled helper substitutes
prove native init ordering/skip/failure behavior, not an actual dependency
restore. A separate offline real-npm proof on a private dependency-free package
does not qualify installation of a generated project's published dependencies.

The staged journey also explicitly calls the existing installed Node project's
`PrepareForHostRunAsync`: restore missing `node_modules`, then compile TS via
`npm run build`. Public `func run` also invokes this preparation hook.

## Local installation contract

Root `bundled-templates.json` is optional for legacy workloads. Schema version
1 declares `assets`, each with `path`, `packageId`, `version`, `sha256` and the
complete raw `templates` inventory (`path`, `identity`, `type`).
The pack task derives the declaration from the real built inner archive.
Only explicitly declared relative `tools/.../*.nupkg` assets are prepared;
there is no directory scan, alias/prefix trust, companion map or template feed.
The outer archive remains exclusively workload-owned; the inner archive is
`FuncTemplate` and must pass digest, identity, raw constraints, inventory, host
metadata and actual private-hive engine discovery before stack admission.

The participating workload operation revalidates registry and prepared payload
bytes. It commits the stack first, then registers local archives through the
existing protected template store. TemplateEngine copies the archive into its
managed hive; deleting the original archive does not leave a dangling mount.
Registration does not instantiate templates or run post-actions.

The workload's `.bundled-template-origin.json` records the validated physical
ID/version/archive digest/actual source, plus logical ID/version/source when
applicable. The physical digest is byte authority; logical fields are
attribution, not a substitute for pointer/implementation validation.
Protected hive `.bundled-owners/<package-id>.json` receipts tie that origin to
the inner version/digest. Receipts contain provenance only, not another native
installed-template inventory. Receipt writes, native registration, validation
and cache rebuild share one snapshot/recovery transaction.
The workload row retains one validated `bundledTemplatesDeclared` bit so setup
does not mistake a retained partial stack for completeness, or invoke installer
operations for undeclared legacy check-only dependencies. Missing declaration
or origin files cannot silently convert an installed bundle into a legacy row.

A registration failure retains the installed stack and reports a typed partial
failure, never successful completion. Cancellation settles native recovery
before releasing operation ownership. Retrying the same stack install or
setup's installed-stack ensure path repairs locally. A same-version catalog
reuse does not re-download stack or template payloads. Unknown native ownership,
package/template identity collisions, conflicting source/bytes or versions are
visible conflicts. User-installed templates are never adopted or replaced.
Setup check-only verifies actual native registration without repairing it.
Workload and setup boundaries emit the retained-stack/retry cancellation
warning to stderr, then propagate cancellation so the public process retains
exit code 130. Incoming undeclared archives cannot complete a retained bundled
stack, enter side-by-side with it, or overwrite a bundled update destination.
A different requested catalog source cannot silently repair from stored bytes.
Reuse validation never registers packages. After workload-operation acquisition,
one existing protected native write transaction checks **all** declared package,
template-identity and receipt ownership before invoking the validated workload
admission callback. The callback completes accepted workload ownership before
native registration. The same native session and participating operation remain
held through registration, receipt/final-content validation, recovery and session
disposal. A competing native writer cannot enter between admission and registration.
Conflicts discovered after private preparation refuse before stack effects.
Protected snapshot/recovery bookkeeping may run, but refusal preserves workload
payloads, registry rows, native packages and receipts.
Setup applies the requested resolved physical or logical origin before ensure.
Bundled no-op updates verify payload, requested origin and actual native
completeness without repair. Cached ownership reuse applies the same bundled
family admission as prepared updates before changing any owner.
Missing or malformed retained bundled manifests cannot authorize the destructive
reinstall fallback, even without `--force` and even after a prior partial failure.
Missing declared archives are typed workload errors preserving their original
missing-file/directory cause, so setup check reports a failed dependency and its
normal JSON terminal result instead of aborting with an unclassified path error.

This phase supports **fresh installs and exact-origin repeats** only. Bundled
force replacement, destructive upgrades, side-by-side versions and uninstall
are explicitly refused before destructive effects; an ownership/removal policy
is future work. Undeclared legacy workloads retain their prior behavior.

Release coordination must ship the supporting CLI before these stack artifacts.
This base retains the initializer and factory alongside the canonical installed
`IProjectStack` metadata; independent template feeds/releases are not prerequisites.

## Exercising the staged journey

`BundledNodeTemplateTests` packs the actual production Node project, installs
that outer artifact into private homes, and exercises the staged command
contracts and ownership/failure/cancellation controls.

The process fixture's `bundled-node` mode accepts a private root, project path,
`js|ts`, then actual locally built Node/host/worker/bundle archive paths. Set
`FUNC_CLI_HOME` and `FUNC_CLI_WORKLOADS_HOME` to private directories first.
It installs the supplied archives, opens a fresh actual workload host, previews
then creates `basic` and `http`, and runs the installed Node preparation hook.
The resulting project can be launched with public
`func run <path> --offline --output plain`. Use private host ports and a
controlled emulator with private data/endpoints when storage is required.
