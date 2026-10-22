# Bundled native Node templates

This first-party authoring slice ships `Azure.Functions.Templates.Node.Basic`
**inside** the Node stack workload. Its version follows the stack build.
It is not an independently released companion, NuGet dependency, or feed lookup.
The package ID is an authoring choice, not a trust assertion.
The bundled archive contains only the two basic projects. The separate
`Workloads.Node.FunctionTemplates.csproj` produces
`Azure.Functions.Templates.Node.Functions` with all 33 owned function items,
including HTTP. This is an internal authoring identity, not a production
publisher or acquisition approval. No production external association is added.

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
No model-v3 `function.json` is emitted. The function archive carries the
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
and source files under `content/items/` in the separate function archive. It reads
the existing jobs, prompts, resources and `_bindings.json`; there is no second
maintained item catalog. Separate packaging does not qualify a template feed
or publisher release. Changes to the
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
required/default values, choices and resource descriptions. Only tokens actually
present in the owned source are replaced. In particular, most existing
connection/path/schedule prompts are no-ops in these payloads, and MCP
resource/tool registration names are fixed literals. This migration does not
silently repair those inherited semantics, add package dependencies, or run
shell actions. The command-owned `--name` replaces the original function-name
input where the payload uses it.
The native parameter projection reserves `name` for the command; it does not
carry the legacy function-name regex. The baseline staged facade also supplies
the output-directory name as the native fallback when `--name` is omitted.
Neither policy is changed globally. The Node workload's `NodeNativeTemplatePolicy`
supplies the original effective name and regex through the neutral compatibility
bridge, with explicit input winning over the authored default.

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

## Selected-source compatibility in the owning workload

The inner archive additionally contains the byte-exact original
`templates.json`, `_bindings.json`, `userPrompts.json` and `Resources.json`
under `provenance/node/v2/`. A Node-local pack-generated
`provenance/node/compatibility.json` binds each original ID to its native
identity, package ID/version, configuration, host metadata and payload hashes.
These are internal qualification records, not a second catalog, release,
user-supplied metadata policy or stable authoring ABI.

The Node workload registers `NodeNativeTemplatePolicy` through
`FunctionsCliBuilder.AddNativeTemplatePolicy`. Its `NodeItemSource` reader
accesses the host-issued `NativeTemplateSource` only after actual loaded-owner,
selected runtime and retained external association admission. It verifies the
raw configuration fingerprint, original prompts and actual package bytes, then
delegates validation and name origin to the generic CLI bridge. It never locates another archive by
package ID/version, trusts hash syntax without reading bytes, changes core
name fallback, or invokes the legacy V2 file-writing backend.


The policy requires invocation and captured-output qualification. It retains
all eight selected source pins, final canonical inputs and the one regular-file
source association. The CLI keeps containment, capture, source lifetime,
ownership, hash and output-installation safety generic. Registering the policy
does not turn on native public commands or authorize missing production
external acquisition data.

`NodeProjectSource` also qualifies the selected canonical Node project's
existing literal `host.json` configuration shape and package files. The generic
CLI planner still verifies the actual root and mandatory configuration action.
This path requests invocation qualification only, not a new owned-layout,
dependency executor or npm completion callback. Other project shapes require
additional owner support rather than a fallback or guessed execution policy.


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

`NodeItemSource` retains the original single-job/action/body
association and native configuration/payload checks without selecting a
target. `NodeNativeTemplatePolicy` supplies directory-free generic
source pins in both modes: exact selected identity/configuration hash,
manifest, config, host, native body and all four original source-file pins,
plus the expected native regular-file source. Shared engine guards revalidate
these pins before preview and creation and reject extra/wrong-kind outputs.
The runtime Node directory planner and typed placement override are removed.
`LiteralOutput` remains the default for staged/direct-native callers.

The legacy-relative base is always the original command directory, including
omitted root, explicit root and explicit nested directories. No folder suffix
deduplication or re-anchoring to the discovered project root occurs. Thus a
public-style nested base retains the old nested `src/functions/<name>.js|ts`
formula, while an explicitly literal nested output remains literal.

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
The existing preparation bridge remains CLI-internal until a neutral owning
workload completion contract preserves its preview, skip, cancellation and
partial-result ledger. This is a remaining ownership migration gate, not a
new npm execution path. No preparation provider is registered by default, and no arbitrary command
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
Check-only preparation, final validation and no-op updates require a clean native
store. A pending recovery journal produces an actionable workload error without
restoring the snapshot, deleting the journal or changing retained packages and
receipts. Ordinary install and explicit repair retain the owning store's normal
recovery behavior.
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
This layer consumes the published `IProjectStack` metadata surface without
duplicating it. Independent catalog releases are not prerequisites for local
bundled registration. Required external acquisition has its own qualification
inputs described below.
The Node workload retains its initializer and factory alongside that metadata.

## Hybrid delivery boundary

The stack workload's actual bundled archive contains only the two basic
projects. All 33 function items, including both HTTP languages and six hidden
variants, are produced in the separate function archive. They remain available
through explicitly qualified selected-owner associations in controlled tests.
Function and additional project templates require independently qualified
external acquisition. No fixture association is copied into production data,
and missing required function content is not bundled-ready success.

The shared installer and setup foundation must keep bundled readiness separate
from required external readiness. CLI release owned association records must
identify compatible stack and native packages, approved source and channel,
engine compatibility and qualified release artifact integrity. Profiles may
narrow those requirements, not grant trust. Package prefixes, equal versions
and stack self-declarations are not association authority.

The explicitly composed native setup path uses schema version 2 of the existing
acquisition records. It validates actual selected-stack ownership, source,
compatible ranges, FUNC host version and channel before acquiring external
archives. Protected native provenance and exact-version pins support verified
reuse without treating a bundled package as external readiness. This composition
is not enabled by the default CLI host.

Concrete publisher-qualified external records and artifacts are not yet
available. Fixture package evidence cannot replace them or establish production
acquisition. The owned Node project fixture exercises separately acquired native
project content through staged preview/create and canonical configuration, not
an independently published production catalog. Shared packaging and Node catalog
production remain separate prerequisites. Public command activation and runtime
qualification are unchanged.

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
