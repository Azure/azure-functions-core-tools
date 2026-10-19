# Bundled native Python templates

The Python stack physically owns `Azure.Functions.Templates.Python` inside
`tools/any/templates/python.nupkg`. Its version follows the stack package.
It is not a companion feed, separately released catalog, or runtime download.
The existing manifest task derives the complete inventory and SHA-256 from
the built archive. The archive carries the repository MIT license, original
template authors, original source records, and channel provenance.

## Source and channel coverage

The checked-in sources are the Python v2 records from these exact extension
bundle `StaticContent/v2` snapshots:

| Channel | Bundle version | Raw items | Listed items |
| --- | --- | --- | --- |
| Stable | 4.35.0 | 18 | 16 |
| Preview | 4.42.0 | 24 | 22 |
| Experimental | 4.6.0 | 23 | 21 |

`source/channels.json` records exact source URLs, original download hashes,
and each channel's inventory. The pins originate in
`eng/build/Workloads/Workloads.Templates.SourceBundleVersion.props`.
These records have 25 distinct identities; identical records shared by
channels are stored once. The archive separately carries 25 hidden,
internal-expansion helper items; they are not additional user templates.
Preview has `MCPToolTrigger-Python` and
`McpPromptTrigger-Python`; stable and experimental retain
`MCPTrigger-Python`. The five OpenAI templates occur only in preview and
experimental. Native item constraints require the usable Python workload
snapshot and the actual command's resolved bundle ID and legacy minimum
range `[4.0.0,)`. Installed bundle workloads alone do not satisfy that context.
Items also declare the **provisional**, all-required
`func-bundle-bindings` constraint. `source/binding-requirements.json` maps
the source decorators to exact names declared in the pinned bundles'
`bin/extensions.json`, preserved under `source/bundles/<channel>/`.
The channel record includes each original metadata SHA-256 and bundle archive
URL. HTTP and timer explicitly declare no extension binding dependency.
The existing channel/version constraint remains conjunctive.

The staged runtime reads capabilities only from the actual resolved local
bundle path, not these provenance copies, CDN content, or installed-workload
rows. A missing secondary binding restricts an item even when its primary
binding and bundle ID/version match. The runtime constraint is explicitly
opt-in and stays unavailable in default flows; it is not publicly activated.
Metadata eligibility is not execution or backend qualification.

`DurableFunctionsEntityTrigger-Python` and
`DurableFunctionsOrchestration-Python` retain the legacy hidden-listing bit;
direct identity selection is not removed. The original IDs remain short
names. Native identities use `Azure.Functions.Python.<original ID stem>`.
Inputs retain original IDs, choices, requiredness, defaults, and legacy
kebab-case long aliases. The command's name is normalized using the
legacy ASCII identifier substitution, including a leading digit prefix.
The source snapshot retains prompt validators; native parameter metadata
does not itself enforce the legacy regex validators. The optional,
source-qualified compatibility adapter is exercised against the packaged
original prompts for the authored name default, invalid function names and
invalid timer schedules; no default/public validation policy is changed.

When the original create job has no function-name input, the native item's
authored default retains the legacy raw template ID. This applies to
`OpenAI-Assistant-Python`, `OpenAI-Chat-Python`, and `OpenAI-RAG-Python` in
preview and experimental. Their matching internal helpers use the same default.
An explicit `--name` is used unchanged, without appending `-Python`. Function
bodies and filenames are unchanged; the source-qualified reader checks this
fallback against the original record rather than accepting a shorter stem.

`Convert-PythonTemplates.ps1 -SourceRoot <download-directory>` reproducibly
converts the three pinned downloads. It verifies source digests and refuses
to collapse differing records, prompts, or resource values. The expected
input paths are in the script. Packing uses only the checked-in conversion;
it performs no CDN downloads.
Owned JSON metadata is normalized to LF before hashing so its descriptors
match Git checkout bytes as well as the packed archive; Python payload bytes
are not normalized by this step.
The local `.gitattributes` prevents Git from normalizing the imported Python
payloads: original CRLF/LF and trailing whitespace are retained so the recorded
source hashes and exact-byte parity survive another checkout.

## Project parity

`basic` reuses the existing initializer's `function_app.py`,
`requirements.txt`, `getting_started.md`, `.gitignore`, and
`local.settings.json` at pack time, rather than copying their content.
`host.json` retains the minimal version and optional default bundle range,
with stable/preview/experimental channel choices and `--no-bundles`.
Project configuration uses the existing **draft** project action with
canonical `python` / `Python`; it is not a stable public authoring ABI.

The legacy initializer remains registered and unchanged. It writes missing
files, supports explicit force, and merges extension bundles into an existing
host file. Native project creation is not a substitute for that existing-file
merge path. Neither initializer nor this native project runs pip or creates
a virtual environment at creation time. The existing Python project factory
and `PrepareForHostRunAsync` remain unchanged: interpreter/venv selection and
requirements installation belong to host preparation, not template generation.

## Controlled native append expansion (internal)

### Qualified custom app filename creation

For an original create action whose path is literally `$(APP_FILENAME)`, the
converter exposes its original filename input through `--app-file-name`. The
native parameter owns the `function_app.py` filename rename; the CLI does not
rename files after generation or transform the Python body. Its default remains
`function_app.py`, and the matching internal helper keeps the same parameter
schema without renaming its `function_body.py` output.

The restored generation contract covers a new, safe, project-relative `.py`
filename or an extensionless basename using only ASCII letters, digits,
underscores or hyphens in an authored no-app project. The extensionless name is
used literally, without adding `.py`. The Python adapter validates that name,
revalidates the selected package/source mapping and captures the engine's output.
Creation requires exactly one new regular output at that filename, from the
owned create source. Extra sources, changed descriptors, path escapes and
occupied targets refuse before writing. This create-only capability does not
authorize `--force` replacement. Existing default-app detection still enters
the original source-qualified append path for existing `.py` options; an
extensionless option is not newly admitted when a canonical app already exists.
A create-only filename is not an append target or a way around app/Blueprint analysis.

This does not reinterpret the four suffix-corrected source actions, establish
arbitrary filename extensions or other file-option behavior, change the separate
`--trigger-function-name` axis, or prove that a nondefault filename is loaded by
the Python worker/SDK. No script-file environment setting, app import, pip,
worker, SDK or host execution is added.

### Qualified existing-app selection

`--app-selected-file-name <existing_app.py>` is a distinct native parameter,
authored from the original append job's `$(SELECTED_FILEPATH)` input. It selects
an existing app, not a create filename or Blueprint operation. Its default is
`function_app.py`; an omitted default does not select append over the ordinary
no-app, custom `.py`, or extensionless create routes.

The selected-file adapter binds one existing project-local `.py` basename using
ASCII letters, digits, underscores or hyphens to its qualified mapping and target
digest for that invocation. Both the canonical app and the selected app must be
regular files. The native helper still supplies the original snippet. Preview
and the typed append executor validate the same bound target, source, path,
name/collision and digest. Only that selected file is appended; the canonical
app and unrelated files are retained. Failed or stale selection never falls
back to canonical append or ordinary creation, and `--force` cannot bypass
collision or target checks.

Alternate targets must have exactly one hard link. A per-invocation filesystem
identity is checked during qualification, on the executor's actual held write
handle, and immediately before append along with the current pathname and link
count. This refuses aliases to the canonical app, another protected file or an
outside file, even when distinct legitimate app files have identical content.
The injected reader uses public Windows file-information, Linux `statx`, and
macOS `fgetattrlist` contracts. Missing APIs or required returned fields refuse
the operation rather than falling back to hashes, pathname equality or a private
runtime stat layout. This does not promise an atomic cross-process namespace
transaction or change the existing canonical append link policy.
Unix pathname qualification queries metadata without a blocking content open:
Linux uses an `O_PATH` descriptor and macOS uses pathname attributes. A FIFO or
other nonregular selected target is refused before digest or snippet reads.
The selected writer also validates its held target before hashing.

The source reader accepts this capability only when its native symbol, explicit
host alias and original existing-file action agree. Older packages without the
declaration keep the original canonical behavior and do not gain a synthetic
alias. No parser override, content replacement, generic path resolver, module
import or worker entrypoint policy is introduced. Subdirectories, other filename
domains, absent-canonical selection and Blueprint semantics remain unqualified,
not accepted historical compatibility losses.
The source-linked executor keeps its original canonical append signature.
The added selected-file member has an explicit unsupported default for older
implementations; it never silently invokes the canonical writer without the
required held-target validation.

The native item create sources preserve each original `function_app.py`.
`snippets/<stem>/function_body.py` preserves the matching append payload,
with the same name replacement and all remaining parameter tokens. Packing
links those exact bytes into a second native item's
`content/append/<stem>/function_body.py`, without maintaining another copy.
Each helper has the original symbols, forms, name, language and constraints,
a distinct identity, and hidden host metadata.

`source/applications.json` binds each original identity to its exact
configuration/source hashes and helper identity/configuration/source hashes,
expected `function_body.py` regular-file output, and `internal-expansion` role.
The packed envelope records the actual package ID/version, replacing only
the build-time version placeholder. It also binds the original template,
prompt, resource, channel and binding-requirement files by SHA-256. The basic
project's eight linked/configuration sources are hashed at pack time so the
declaration describes the actual bytes on the build platform.
Hidden metadata is **not** authorization:
the selected-item bridge must refuse ordinary requests for the exact mapped
helper identities, while retaining explicit selection of original hidden
Durable items.

Legacy `V2TemplateEngine.PickJob` selects `AppendToFile` whenever the default
create target exists; otherwise it selects `CreateNewApp`.
Append retains user imports, helpers, functions and app initialization, adding
only a separator when needed and the selected body. Editor-only blueprint
jobs are preserved as source provenance, not advertised as CLI support.

Four pinned records have an inherited filename defect: both hidden Durable
items, `EventGridTrigger-Python`, and `ServiceBusTopicTrigger-Python` resolve
their create action to `function_app.py.py`, but append to `function_app.py`.
Each occurs in all three pinned channels. The pre-correction checkpoint
`d61e4abe7102fe052f0eb6329897c699b7efff96` preserves the original 65-case
native/legacy path-and-content parity and four-quirk characterization.
`FilenameBeforeEvidence` continues to execute the unchanged V2 engine against
all 65 raw records, including its double suffix and custom-filename behavior.

**Intentional native-only fix:** the user authorized correcting exactly these
four native create outputs to `function_app.py`. Their native create and append
now select the same file, so a subsequent function appends without forcing an
overwrite or creating another app. `source/applications.json` retains the
original resolved `legacyCreateTarget`, marks only these four records with
`nativeFilenameCorrected`, and binds the corrected `createTarget`/`createSource`
to the unchanged content SHA-256. All original raw records, provenance,
licenses, function bodies, helper metadata, defaults and validators remain
unchanged. The converter uses an exact four-ID allowlist and checks the old
target; it does not normalize arbitrary filenames, other templates or blueprint
jobs. The other 21 native items already used the single-extension target.
The default/public V2 workflow is not corrected or activated by this change.

Plain native creation alone cannot express that read/modify/write contract.
The controlled selected-item adapter must bind the original and helper to the
same installed package/version, source hashes and command context, render only
the exact helper into an owned temporary directory using the existing native
invocation path, then apply the approved append effect. It must reject extra,
missing or non-file outputs and avoid recursively invoking the effect hook.
There is no manual token substitution, selected-config mutation, alternate
renderer, item post-action, force-overwrite workaround or shell action.

The Python-owned `PythonTemplateApplicationPlanner` accepts approved target
facts and an explicit native-expansion callback, selecting only the original
CLI jobs and refusing empty content. `PythonTemplateAppendExecutor` checks
the approved raw SHA-256 and appends through the same open file handle,
preserving original bytes, BOM, user code and the legacy newline rule.
The native append preflight and executor also check every generated function's
declared Python symbol and effective SDK name against the existing source.
Repeating an explicit or default name, or colliding with a literal
`@app.function_name(...)`, refuses before writing, including with `--force`.
Force does not authorize replacement of arbitrary user-authored Python.
Names follow the SDK's case-sensitive comparison; Python identifiers use
Python's NFKC normalization, but explicit SDK name strings are not normalized.
Comments, ordinary/raw/triple-quoted strings, and function/class-local symbols
are not treated as module-level function declarations. Fixed-name multi-function
payloads are checked in full, not only against the requested CLI name.
Effective SDK names are recognized on `app` and its simple module-level aliases.
Literal `app.function_name(...)` names applied as decorators or directly to
the immediately preceding plain function remain checked in unconditional
module-level call expressions or simple assignments, including route/trigger
wrappers. Other execution contexts or intervening module code remain partial.
Explicit definitions or assignments invalidate prior app/alias identity before
the new binding is classified. A recognized new FunctionApp/DFApp constructor
starts a new registry; unknown factory or alias bindings remain partial.
Existing builders or multiple name
settings remain partial rather than assuming the outer name overrides the SDK's
first setting. Unknown Unicode identifier scalars stay in
their whole token and make analysis partial instead of reserving a truncated
name. The lexer is not a complete Python parser or identifier-domain validator.
Passing a recognized app or alias as a value to an opaque helper also makes
analysis partial, without inspecting that helper's registrations or inferring names.
Computed names, conditional declarations, Blueprint/programmatic registration, other decorators, and
unsupported lexical shapes make analysis partial; they do not by themselves
refuse append. Known symbol/name collisions still refuse even alongside unknown
registrations. An unregistered Blueprint's effective names are not assumed to
belong to `app`. Blueprint/dynamic registration alone does not block append.
Successful append and preview report a non-failing validation-limit diagnostic
when source analysis is partial, in both plain and JSON output. Confirm actual
application indexing before deployment: opaque computed or imported registrations
may still collide, and static validation does not establish runtime uniqueness.
This bounded source check does not execute Python, require a Python installation
for scaffolding, resolve arbitrary imported aliases or data flow, or prove dynamic
applications indexable. The legacy V2 renderer and raw payloads remain unchanged.
Missing/changed targets, repeated stale effects and pre-cancellation refuse
without writes. An I/O error or cancellation during writing can leave a
partial append; it is not a transactional rollback implementation.
These services are DI-registered but have no public command route.
The focused asset tests execute all pinned helper expansions through the
actual native engine and then the owned executor, comparing the resulting
app bytes with the actual legacy append path. Additional tests consume the
shared selected-item adapter using the real installed package mapping and
mounted source reader: preview/apply, source/config/package mismatches,
changed targets, decline, cancellation and missing effective names. Ordinary
runner tests deny mapped helpers even without a capability while preserving
explicit original hidden Durable selection. These are explicit internal
compositions, not a newly registered public command route.
The correction tests separately label the authorized filename difference:
all 12 affected channel variants exercise default and explicit effective names,
native preview/create then owned append, retained user bytes and bound source
hashes. Unaffected HTTP, occupied-directory, decline, cancellation and internal
helper denial remain controls; the corrected native cases no longer claim
legacy path parity.

## CLI-owned adapters (unregistered)

`src/Func/Templates/Python/` contains the production item compatibility,
source-map reader, selected-item effects and append capability. Explicit
`PythonItemWorkflowProvider` composition uses the same resolved template's
mounted reader for package/version, original and helper configuration hashes,
source hashes, internal roles, defaults, required values, choices and original
regex validation. Unknown, malformed, duplicated or changed mappings refuse;
the package ID alone is not authorization.
Original prompt documents are matched case-insensitively, preserving the
legacy labels and validators while retaining raw canonical argument IDs.
Prompt IDs that collide under that lookup are refused before projection.
The actual `NewExecutionRunner`
tests use this provider through the common workflow composer with request
capability fields unset, rather than supplying test-only mapping callbacks.

`PythonProjectWorkflowProvider` qualifies only the packaged basic project.
It has no preparation action: initialization does not run pip, create a venv,
or call a second legacy initializer. Generic paired CLI configuration remains
mandatory. The production `InitExecutionRunner` tests exercise both the
normal and no-bundle variants and reject changed owned requirements.

The CLI source-links the existing append interface and executor, using a
host-only namespace conditional to avoid exposing identically named internal
types from two assemblies to friend tests. The helper bodies and physical
workload DI behavior are unchanged. There is no runtime reference from the CLI
to the physical Python stack, reflection provider, public SDK API or global
provider registration. The physical legacy planner remains unchanged; native
job selection is still owned by the common selected-item support.

The CLI append capability checks the exact target and SHA-256 before
execution, rejects links below the explicit invocation root, and repeats
validation before append. An explicitly linked invocation root retains the
existing native boundary semantics. These checks do not make filesystem
name replacement atomic with opening the helper's file handle; external
rename/link races and partial I/O/cancellation are not rollback guarantees.
The earlier private Python 3.13/HTTP qualification belongs to the unchanged
`6a3c1dea` checkpoint, not this new adapter source.

Public `func init` and `func new` continue their current routes. Actual native
creation tests cover missing-target generation and plain native occupied-target
refusal alongside the controlled append journey. No Python host,
worker, backend, Unix or deployment qualification is implied.
