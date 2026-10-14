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

Ordinary creation post-actions remain unsupported by the staged project path.
Therefore these packages **do not yet preserve the initializer's default
creation-time `npm install` or its `--skip-npm-install` switch**. Do not mistake
the absence of an npm post-action for full init parity. The staged journey
explicitly calls the existing installed Node project's
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
This base uses the existing initializer metadata adapter. The separately
published `IProjectStack` migration must be integrated later without duplicating
it here; independent template feeds/releases are not prerequisites.

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
