# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Shared Parser Draft Qualification

`ITemplateArgumentParser.Parse` accepts a candidate identity, its projected parameter definitions, the calling command's complete reserved alias set and the unchanged template token sequence. It creates an ephemeral parser; it does not attach options to public commands. The result preserves effective aliases for help and prompting, canonical values, invalid-input diagnostics, authoring diagnostics and unresolved required parameters. Input diagnostics carry zero-based indices into the original tokens, or no indices for completion values. Schema-local alias mapping retains token provenance and requires every original token to occupy a declared option/value role, including tokens the framework would otherwise ignore. Response-file expansion and parser actions are not enabled. Missing visible required values remain argument-compatible, but `IsComplete` is false until resolved. Native preview remains authoritative for engine features not represented in the projected schema.

Callers apply argument compatibility before precedence. After prompting, they parse again with the **original** tokens and canonical completion values; completions cannot overwrite explicit input or erase its errors. Required defaults are validated and passed explicitly because the native engine ignores them. The native `name` symbol remains excluded from parameter projection; host `--name` is passed separately as `TemplateInvocationRequest.Name`.

`ExplicitParameterNames` is an immutable set of canonical names supplied through the parsed template tokens. Authored defaults and completion values remain distinct even when their values equal explicit input. Invalid explicit values retain their origin, so consumers must still check diagnostics and `IsComplete`. The shared option-like token predicate preserves the existing signed-number handling; it does not add a command entry route or change the grammar.

This draft proposes scalar options with one explicit value, including booleans; repeated options are rejected, and choice values must match a declared choice case-insensitively, without native abbreviations or multivalue expansion. Unknown dash-prefixed tokens fail even when the underlying parser would consume them as string values; literal dash-prefixed values use inline syntax (`--greeting=-literal`), while negative numbers remain valid separated values. The template token tail has no positional arguments or standalone `--` terminator. Missing or unknown datatypes fail closed rather than becoming strings. Supported native scalar spellings are `string`/`text`, `bool`/`boolean`, `int`/`integer` (signed 64-bit), `float` (current or invariant culture) and `hex` (`0x` prefix), plus declared `choice` values. These input policies need owner review before a public switch; unprojected conditional requirements, option-without-value defaults and multivalue semantics are not claimed qualified.

`TemplateArgumentParserJourneyTests` installs a real folder fixture into a private test hive, parses raw arguments for each native item candidate, selects an argument-compatible lower-precedence variant, and previews and creates that same capability. It checks required-default injection, post-prompt validation, host name handling, nested output versus project root, no output writes on preview or rejection, and unchanged type, constraint and lifetime guards. This is a shared parser/runtime boundary increment, not public `func new`/`func init` cutover, live-store locking, production package qualification or a completed milestone. Existing public grammar, hydration and provider dispatch remain unchanged.

## Project Runtime Qualification

The explicit internal `CreateProjectAsync` flow adds mandatory CLI-owned configuration to installed project-template creation. Existing `CreateAsync` calls remain item-only. The project flow requires supplied availability, installed `IProjectStack` metadata from the existing stack catalog, and paired `TemplateProjectSupport` planner/writer support.

This is staged internal support: the planner and writer are registered in DI for the unregistered `InitExecutionCommand` consumer, but no production command activates the project flow through `CreateProjectAsync`. Existing public `init` and `new` routes remain unchanged.

Language-alias metadata keys are matched case-insensitively, consistent with the installed catalog, while configuration retains the selected stack's canonical language spelling.

The current subset supports unconditional literal primary-output paths, the default source layout, content parameters and installed stack/language aliases. It rejects source remapping, topology conditions, filename-renaming symbols, supplemental configuration and ordinary post-actions before creation. Resolved `host.json` roots are inventoried independently of configuration declarations. Every root must have exactly one configuration action, and template content cannot author `.func/config.json`.

A configuration anchor can be another generated primary-output file when its resolved parent is an independently discovered host root. An anchor outside those roots is rejected before creation.

Host roots and configuration anchors must correspond to regular files in the captured native output image, not just reported native effects. Native placeholder directory effects cannot establish a `host.json` root or serve as a primary-output configuration anchor. Directory effects used in either role are rejected before real installation. A declared but ungenerated `host.json` cannot establish a Functions project root.

Preview reports template and configuration effects without writing. Creation finalizes configuration in declaration order through the existing atomic writer. A finalization failure preserves generated files and completed configuration writes, reports completed roots and fails the invocation. Existing user configuration is refused even with file-conflict force; adoption and repair remain owned by the existing initializer.

Configuration occupancy checks use the writer's injected `IFileSystem` for both files and directories. An occupied destination before creation returns `FileConflict` without generating a new project root. Configuration is committed with an atomic create-only rename, so a destination that appears after the checks is not replaced. That late collision returns `ConfigurationFailure`, preserving user bytes, generated output and any earlier completed configurations. Legacy forced initialization and language repair retain overwrite-capable writes. Non-forced legacy initialization uses create-only persistence.

After a configuration write commits, its root and file change are recorded before cancellation is observed again. Cancellation after the final configuration commit returns the completed result. If another plan remains, cancellation stops before that plan starts. Cancellation or failure during configuration preserves the installed native output, completed configuration writes and internal committed-output marker. The result never reports deferred configuration as committed. Cancellation before any real installation still propagates normally.

The project policy runs on the captured native image and resolved primary outputs before real installation, without rerendering or dispatching arbitrary native post-actions. The complete captured directory inventory remains available for path, link, kind and source checks, including existing directory no-ops omitted from reported changes. Creation results report installed native effects plus only completed configuration writes, not unwritten preview configuration paths. The session retains native lifetime ownership and the invocation coordinator owns the optional project policy.

The fixture action ID `00e33184-ffc7-43ef-bec0-b1684df8ad56` remains proposed, not a stable public authoring contract. These tests qualify controlled runtime fixtures and configuration compatibility, not production template packages or a public command switch. Ordinary restores, wider project topology, live-hive leases and concurrent filesystem mutation remain outside this qualified subset.

## Staged project consumer projections

The unregistered [staged INIT consumer](../func-init-execution/proposal.md#staged-installed-project-consumer)
composes this project runtime with template-first command binding and the shared
strict argument parser. In a project-enabled flow, catalog entries expose
`ProjectConfigurations` declarations from the same snapshot already validated by
the planner; malformed declarations retain separate project and constraint
diagnostics and cannot become a selected capability. Item entries and item-only
flows do not acquire project declarations.
Project errors do not populate the func host `MetadataDiagnostic` field or erase
projected parameters. Previously eligible project-validation failures retain
their failed classification; an independent project-diagnostic guard also blocks
group and capability creation.

Project invocation results additionally expose `ProjectConfigurations`, the
already computed native/preflight plans, separately from `ConfiguredProjects`,
the successfully finalized roots. Plans do not imply completion, including on
configuration failure. Neither projection changes the provisional action schema,
native creation order, failure file-change reporting or path/type validation.
Once planning succeeds, later conflict and revalidation results retain the same
ordered plans; failures before planning do not invent plans.

## Existing-project configuration persistence

The public `func init` adoption and language-repair paths remain independent of template discovery and invocation. Their configuration writes now use the existing atomic filesystem writer and must finish before init reports success. A write failure is a non-zero, actionable error rather than a warning followed by successful adoption or repair. Repair preserves unrelated configuration keys and refuses missing, malformed or non-object configuration instead of synthesizing replacement metadata. Cancellation is passed through configuration reads and writes and stops subsequent success output or initializer dispatch.

Non-forced initialization and adoption use atomic create-only persistence. Configuration that appears after the initial existence check is preserved, and the command fails without success output or initializer dispatch. Explicit force retains replacement behavior, while language repair replaces the configuration it is completing.

The same mandatory writer is used by the current workload-backed creation path before initializer dispatch. This does not change scaffolding order, language omission for single-language stacks, or introduce rollback; a later initializer failure can still leave configuration behind. File-level atomic writes do not provide concurrent-edit isolation or a transaction covering the entire project.

This is preparatory failure qualification, not the public template-first switch. Stack metadata migration, shared strict parsing, stable configuration-action authoring, template acquisition and all-six-stack package qualification remain separate gates.

## Runnable Node Smoke

The opt-in exporter and `ProjectJourney_NodeHttpFixture_GeneratesFunctionAndCanonicalConfiguration` share `NodeProjectFixture` setup rather than using a test class as a tooling API. The test asserts successful preview and output-directory absence before creation. The exporter also refuses creation after a failed preview or a preview that created the destination. The Node metadata is declared directly, independently of the Python fixture.

The fixture supplies controlled Node `IProjectStack` metadata through `InstalledProjectStackCatalog`, a workload availability snapshot and paired `TemplateProjectSupport` to the internal protected `TemplaterFactory.CreateProjectAsync` flow. It generates a dependency-free JavaScript HTTP function and finalizes canonical CLI configuration through the create-only writer. Unit tests cover canonical and case-insensitive language-alias keys and do not launch the host.

The following Windows smoke requires the repository's .NET 10 SDK, Node.js/npm and Azurite on `PATH`. Run from the repository root in an isolated checkout and dedicated terminals. Use a new temporary root on each run. The exporter refuses nonempty destinations.

```powershell
$smoke = Join-Path ([IO.Path]::GetTempPath()) ("func-project-smoke-" + [guid]::NewGuid().ToString("N"))
$feed = Join-Path $smoke "feed"
& dotnet publish src/Func/Func.csproj -c Release -r win-x64 -o (Join-Path $smoke "cli") -p:CI=true
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }
foreach ($project in @("Stacks/Node/Workloads.Node", "Workers/Node/Workloads.Workers.Node", "Host/Workloads.Host")) {
	$arguments = @("pack", "src/Workloads/$project.csproj", "-c", "Release", "-o", $feed, "-p:CI=true")
	if ($project -eq "Host/Workloads.Host") { $arguments += @("-r", "win-x64") }
	& dotnet @arguments
	if ($LASTEXITCODE -ne 0) { throw "Workload pack failed." }
}
$env:FUNC_CLI_HOME = Join-Path $smoke "cli-home"
$env:FUNC_CLI_WORKLOADS_HOME = Join-Path $smoke "workloads-home"
Remove-Item Env:FUNC_CLI_WORKLOADS_SOURCE -ErrorAction SilentlyContinue
$cli = Join-Path $smoke "cli/func.exe"
foreach ($package in Get-ChildItem $feed -Filter *.nupkg) {
	& $cli workload install $package.FullName
	if ($LASTEXITCODE -ne 0) { throw "Isolated workload install failed." }
}
& dotnet run --file eng/scripts/project-template-smoke.cs -c Release -r win-x64 -p:SelfContained=true -- (Join-Path $smoke "template")
if ($LASTEXITCODE -ne 0) { throw "Template generation failed." }
$smoke
```

In a separate terminal, set `$smoke` to that printed temporary root and start Azurite with private data. Ports 10000, 10001 and 10002 must be unused. Do not stop or replace an existing emulator to run this smoke.

```powershell
azurite --location (Join-Path $smoke "storage") --silent --disableTelemetry
```

Back in the setup terminal, choose an unused host port and run the generated app. `--offline` restricts workload acquisition. The existing Node preparation hook still runs npm, including with `--no-build`.

```powershell
$env:AzureWebJobsStorage = "UseDevelopmentStorage=true"
$env:FUNCTIONS_WORKER_RUNTIME = "node"
& $cli run (Join-Path $smoke "template/generated") --offline --port 7183 --no-azurite --output plain --log-file (Join-Path $smoke "host.log")
```

Once the function is listed, verify from another terminal.

```powershell
$response = Invoke-WebRequest http://127.0.0.1:7183/api/hello
if ($response.StatusCode -ne 200 -or $response.Content -ne "Hello from GeneratedApp") { throw "HTTP contract failed." }
$status = Invoke-RestMethod http://127.0.0.1:7183/admin/host/status
if ($status.state -ne "Running") { throw "Host is not running." }
```

Stop only the smoke host and emulator with Ctrl+C, then close the dedicated terminals so their environment overrides do not affect normal CLI sessions. Keep their logs with the run's inputs. The observed Windows run returned the expected body with the host running and no storage-health errors in its observed log window. Project recognition uses the installed Node workload's file fingerprints. Canonical configuration compatibility is asserted separately through the existing `StackOptionsSetup` consumer. This smoke does not demonstrate public template-first `func init`, production initializer capture or other stacks.

## Protected-store draft qualification

Factory admission is asynchronous and accepts the command's cancellation token. The factory acquires shared ownership of the live `<CLI home>/templates` store before native initialization and attaches it to the disposable runtime flow. Private settings/cache copies isolate native bootstrap and catalog writes; live absolute package mounts remain protected through selection and invocation. The composed project/parser fixture journeys use this boundary; legacy public command providers remain unchanged.

The protected project factory forwards the current paired `TemplateProjectSupport` unchanged. Its planner uses the existing installed `IProjectStack` catalog; its writer remains create-only, distinct from legacy init/adoption/repair writes. Workload availability snapshots contain only workloads, while resolved bundle identity/version remain in the immutable engine context. Composed controls cover those inputs, read-only preview, refusal to replace occupied user configuration even with force, and lease retention through invocation and disposal.

The exclusive store foundation snapshots opaque hive files, validates through caller-supplied mutation/content callbacks, checks native cache rebuilding, and restores failed or interrupted mutations before another runtime flow can open. Old engine capabilities are invalidated rather than transferred across recovery. Separate owned Windows processes qualify reader overlap, queued-writer admission, cancellation, stale-marker inspection and process-termination recovery. Windows-only mandatory namespace/access controls explicitly skip elsewhere.

These are controlled store/runtime tests, not package trust policy, public lifecycle commands, automatic installation, startup availability capture, Linux/macOS runtime proof or power-loss durability. See [the draft foundation contract](../template-package-install/design.md#draft-protected-store-foundation) for provisional owner decisions and exact exclusions.

## Qualified item invocation

The current runtime grants item invocation only when the current mounted template
type is recognized and agrees with its cached descriptor. An installed folder whose
type changed before selection remains unavailable, with a diagnostic to correct
the type and refresh the installed template. Changes after selection still require
selection to be rerun.

Invocable parameter definitions and func host aliases come from the current
mounted configuration, not the persisted engine descriptor. Native evaluation
receives an input dataset built from those same mounted definitions.
Folder-template parameter edits between flows therefore take effect without
refreshing the shared hive. The selected snapshot also captures and compares func
host content separately, so alias edits after selection require rerunning selection
without changing the raw template.json SHA used by source provenance.

Resolved items hold an explicit invoker bound to the owning command session.
The invocation coordinator owns admission, preview and creation ordering, while
the session retains native environment and disposal ownership. A captured snapshot
owns a cloned JSON view shared by host constraint, default and post-action checks.
Fresh mounted reads still produce new snapshots for source revalidation. Native
model parsing remains separate from the shared host JSON view.

Mounted primary parameter declarations without an authored default remain unset
in the projected command schema. This prevents an engine-synthesized scalar
default such as integer zero from becoming explicit input and overriding a
native generated fallback. Authored defaults, including zero, false and empty
strings, remain intact. Additional configuration and parameters not declared in
the primary configuration retain the engine's merged defaults.

Each invocation renders through the native engine into an owned in-memory output
image, retaining the real logical output, fallback name and command context.
Native parameter, form, condition and generated-symbol evaluation determines the
actual output. The engine still evaluates effects and creation separately, so its
prepass paths are not treated as the written-file inventory. Captured files,
completed streams and directory creation determine the image instead.

Generated, derived, computed and bound output paths are supported only when their
actual captured paths pass the CLI's containment, link, kind and real-target
conflict checks. Native force consent is not CLI force authorization. Preview
captures an image without installing real output. Separate preview and apply
invocations reevaluate the template and can produce different generated names;
each apply admits and installs its own captured image without another render.
Item post-actions, native deletion, metadata mutations and private-output
timestamp inspection remain unavailable through this capture path. Filesystem routing is not a sandbox for arbitrary
extension code or process, environment and network access.

Apply revalidates mounted source identity and real target state before installing
the captured bytes. Incoming files are materialized privately on the destination
volume, then moved into place with create-only or explicitly forced semantics.
Default and custom empty-directory placeholders remain directory effects; empty
regular files remain files. Existing directories are retained without an overwrite
effect or a force requirement. Final paths and kinds are checked before success.
Linked children are refused, while the requested directory and its ancestors may
themselves be linked, as with macOS temporary directories. Concurrent filesystem
mutation and crash-atomic multi-file installation are not qualified.

Preview and pre-install file conflicts describe proposed changes, not completed
output. Cancellation before real installation discards the image. Failure or cancellation
after partial installation reports the completed real file and directory entries,
without rolling them back. Once all output is installed, late cancellation does
not erase its successful effects. A successful private native render alone is
never reported as completed real output.

Resolved bundle versions are validated when constructing the command context;
host.json version ranges are not resolved versions. Bundle constraint input comes
only from that context. The availability snapshot captures workloads, not another
copy of bundle identity or version. A malformed folder-mounted template is
reported as an unavailable candidate without preventing healthy catalog entries
from being listed.
