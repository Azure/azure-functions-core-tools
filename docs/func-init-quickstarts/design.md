## Current staged integration

Start with `func init MyConnector`. Quickstarts are ordinary project templates
with no user template parameters. An eligible interactive fresh target offers
**Basic Functions app**, visible installed project templates and
**Browse more templates**. Basic delegates to the existing workload initializer
with the original parsed arguments. Installed templates and Browse use the
existing coordinator, with exact binding and qualified acquisition where
applicable. A sole eligible, uniquely owned installed native stack is selected
without an extra prompt and remains named in the template menu. Multiple eligible
stacks still require selection. Explicit stack constraints are never overridden.
Browse **Back** returns to the invoking template menu, including when no results
match. **Cancel browsing** and **Cancel** stop before acquisition or project writes.

The default menu requires a uniquely owned installed native stack. Scripts,
initializer-specific options, `--force`, existing or nonempty targets, and
unsupported or unavailable ownership retain the legacy initializer dispatch.
Explicit `--guided` remains the opt-in adapter for its staged flags; this narrow
default-dispatch layer does not migrate all initializer parameters or force
semantics. The existing `func quickstart` commands remain available.

The guided route uses the same browser, with an explicit **Use this template**
result. Default browsing remains read-only and never selects even a single
result automatically. Search, resource and IaC filters remain available.
Only independently verified exact installed duplicates are suppressed.
Unknown installed proof stays visible with a diagnostic.

Before downloading, guided init joins an advertised full identity, package,
version, HTTPS source and SHA256 to the independently bundled schema2 authority.
The association must cover the actual loaded stack owner, physical and logical
identity, source, host version, channel and `additional-project` role. It includes
the complete archive inventory and content revision. Remote project and workload
declarations are displayed as unverified claims, not interpreted as approval.
The resolved required companion packages, exact bytes, source, owner and target
are displayed before an explicit default-no acquisition confirmation.

Consent binds the complete resolved candidate plan, selected exact archives and
workload inventory. The existing acquisition runner revalidates these before
preparation and writer admission. Changed plans fail instead of selecting another
version. An explicitly consented owned-package update can replace a proven
CLI-owned archive with a strictly newer qualified release for the same package,
source and current workload owner, as described below. Ordinary acquisition does
not replace conflicting native ownership, upgrade workloads or change feeds.
Acquisition must finish successfully and release prepared payloads, reuse evidence
and leases before a new bound init scope opens.
Fresh runtime registration and exact installed artifact verification remain
mandatory. Native preview and mandatory project configuration share the same
selected flow and completed parameters. Interactive creation requires a second
default-no confirmation of those actual effects. Declining creation retains any
explicitly acquired packages and reports that fact.
Cancellation during acquisition reports retained-package and recovery diagnostics,
then propagates command cancellation after acquisition cleanup. It never starts
project creation or claims rollback.

Scripts must select a stack and either an installed template or a catalog ID.
Choosing guided mode alone is not acquisition authorization. Supplying
`--quickstart` implies guided routing and authorizes its exact qualified
acquisition plan.

```text
func init MyConnector --guided --stack node --template <installed-full-identity> --non-interactive
func init MyConnector --stack node --quickstart <exact-catalog-id> --non-interactive
func init MyConnector --guided --stack node --template <installed-full-identity> --dry-run
```

`--quickstart` authorizes only the displayed independently qualified exact
acquisition and creation plan; no separate `--guided` or
`--acquire-quickstart` is needed. The explicit flags remain accepted when
supplied. Interactive invocations still prompt. An unknown installed reference
never starts discovery or acquisition. `--quickstart` and `--template` cannot be
combined. `--template`, `--dry-run` and `--non-interactive` still require
`--guided`; `--quickstart` implies it. These options do not redirect ordinary
init.

Dry-run never acquires, registers, repairs, recovers or cleans. A discovery
selection without installed payload reports metadata intent and a diagnostic,
not a fictitious engine preview. Use an installed `--template` for native
dry-run. Guided init rejects nonempty targets, adoption, repair and `--force`
before acquisition or cleanup. The legacy initializer path retains its existing
adoption, repair, force and workload-option behavior, including response files.
Guided init does not accept workload initializer options.

### Advanced native-template compatibility

The general help and quickstart menus do not expose parameter editing.
The advanced `--template-argument` option remains accepted but hidden from
general help, following the existing hidden-option convention. Arbitrary native
parameter tokens are available through repeated `--template-argument=<token>`
options on the public guided adapter. Each occurrence supplies exactly one raw
token, in order, to the existing selected-template parser. An explicit target
path and either `--guided` with `--template` or `--quickstart` are required.
This does not add optional parameter entry to the default interactive menu or
pass native arguments to Basic. Required native values can still be prompted
interactively; scripts fail if they are unresolved.

```text
func init MyConnector --guided --stack node --template <installed-full-identity> --non-interactive --template-argument=--greeting --template-argument="hello world" --template-argument=--enabled=false
```

Use `=` on the host option for dash-prefixed and `@`-prefixed raw tokens.
The raw native token can itself contain an inline value, as in
`--template-argument=--enabled=false`, or its value can be the next repeated
option. Boolean symbols require an explicit `true` or `false`; supplying only
the symbol is an error. Empty raw host-option values are rejected rather than
dropped; use `--template-argument=--greeting=` for an empty native string.
Scalar types, choices, required values and duplicate
long/short aliases are validated by `TemplateArgumentParser`, not a second
public argument parser. Host and initializer aliases remain reserved, including
`--template-argument`. A conflicting native alias uses the existing canonical
fallback, for example `--template-argument=--param:Stack=node` or
`--template-argument=-p:s=node`. Native name input remains the host's `--name`.
No native token authorizes acquisition or sets another host option.

There is deliberately no native `-- <arguments>` suffix. A variadic positional
would also capture host typos before the delimiter in System.CommandLine 2.0.6,
changing `ParseResult.Errors`, help and other parser consumers. The repeated
option keeps strict unknown-option and extra-positional errors without changing
global parser settings. Initializer-only options remain legacy-only.

Public response files retain System.CommandLine expansion, nested references,
quoting and backslash behavior. The resulting raw option values are never
expanded again by the native parser. Use `--template-argument=@file` for a
literal `@file` value, including inside a response file. A bare `@file` token
still requests public response expansion. In the existing parser a direct argv
token after `--` is literal, but bare references inside response-file contents
are recursively expanded before insertion, even if textually after a delimiter.
This change does not normalize those different behaviors or enable a native
suffix. Missing response files and invalid host grammar still produce public
parse errors.

**Remaining gates.** The bundled production authority is still unqualified and
contains no approved quickstart associations. Production acquisition therefore
fails explicitly. Real fixtures demonstrate the complete mechanics, not publisher
or hosted release qualification. Production needs an approved `FuncTemplate`
archive, package/version/source/digest/contentRevision, full inventory and
actual stack/host/channel/role association. The default-init cutover still needs
the initializer compatibility inventory, default-menu optional parameter integration,
compatible force support, production replacement parity and legacy removal
approval. No held Node executor or ordinary post-action dispatcher is enabled.
Later policy alignment must preserve dispatch and rebuild policy/factory services
in the same fresh scope. The DA6B foundation remains the explicit baseline.

### Bounded migration qualification

The real workload/archive fixture exercises acquisition through a public
`RootCommand`, followed by a second fresh public invocation that selects the
installed exact identity for a different target. The repeat must not download or
prepare another archive, must prompt only for creation, must leave the installed
template hive unchanged and must write the selected project's configuration.
The fixture also checks workload writer admission after creation. Fixture
authority is not production approval.

### Selected acquisition and bundled Basic provenance

Schema2 companions can declare `kind` as `required` or `optional`. Omission means
`required`. Schema1 rejects the field even when explicitly null. Content roles
remain independent. A required `additional-project` companion is still required,
and an all-optional mapping is valid but cannot discharge an unselected mandatory
role demand.

An exact optional choice includes the full project identity, package, version,
digest, source, physical and logical live owner, and channel. The request copies
and freezes the choices. Qualification, installed external verification, and
every acquisition replan use the same projection. All genuinely required
companions remain, while only explicitly selected, independently qualified
optional packages participate. Unselected retained optional entries do not impose
ranges, roles, source or host constraints. A selected version is exact even when
a newer qualified candidate exists. Choices are intent, not approval. Acquisition
requires consent bound to the immutable projected plan, complete inventories,
content revisions, roles, host, owner, choices and workload inventory.

Bundled Basic content uses a separate capability, not an external native receipt
or an `additional-project` relabeling. A fresh scope derives expected declarations
and origin from the actual loaded runtime workload. Read-only validation checks
the declared local archive bytes and complete path, identity and type inventory,
the current physical and logical origin, unique installed native archive and
bundled ownership. Selection and each protected invocation revalidate that proof.
Missing, malformed, foreign or changed evidence refuses without repair or receipt
creation. Valid bundled projects appear beside Basic, installed external projects,
Browse and Cancel in the default menu.
An independently qualified external project does not depend on unrelated bundled
evidence. If external proof is unavailable, only complete independent bundled
proof can authorize a bundled selection. A missing external receipt is not
bundled authority, and unmatched projects retain their external diagnostic.

### Explicit owned-package updates

The dependent update layer supports an explicitly consented A1 to A2 transition
for the same package, source and independently verified current workload owner.
The selected full project identity must exist in both inventories. A2 must have
strictly newer NuGet release precedence, not merely different build metadata.
The old version need not remain eligible under the current mapping, but its actual
unique CLI-owned archive, complete inventory, pin and strict receipt must agree.
A2 independently satisfies current authority and all genuine required companions.
Consent freezes both BEFORE and AFTER proof and the complete plan, choices and
owner inventory. No-intent acquisition and ordinary ownership guards are unchanged.

Under the existing exclusive store lease, updates refuse a pending recovery
journal without recovering or replacing the backup. Isolated pre-snapshot
admission checks the incumbent and raw receipt bytes to classify exact BEFORE,
exact approved AFTER or conflict. BEFORE also requires exact staged new bytes and
no native identity collisions. Only exact BEFORE enters the existing snapshot,
provider replacement, full expected-old receipt transition, validation and commit
sequence. Precommit failure
uses whole-hive recovery, including archive, provider, cache and receipt.

Exact approved AFTER is an independently verified read-only retry with no payload
fetch, provider or receipt mutation, cache rewrite or journal arming. Near matches
refuse. Results remain per package. If A commits and B fails, A remains completed;
retry can reuse exact A AFTER and attempt only incomplete B under unchanged consent
and authority. No mapping-wide atomicity or durable consent token is introduced.

This transition does not permit adoption, downgrade, source or workload switching,
workload upgrades or a force bypass. It adds no public option and enables no
production authority, feed publication, general force or legacy removal.

### Qualification limits

This qualification is limited to Windows and the DA6B-based implementation.
An earlier composed run failed while deleting a loaded Node workload DLL during
fixture disposal, after its behavioral assertions completed. Narrow rechecks
have not reproduced it. Scope disposal requests collectible load-context
unloading, but that alone does not establish when all runtime references are
released. The cleanup failure's cause remains unproven, and passing rechecks
do not resolve that qualification limit.

Release readiness still requires independently producer-approved exact archive
and schema2 association data, an actual production end-to-end run, alignment
with the later `63b` policy/factory line, and cross-platform and hosted evidence.
Legacy removal additionally requires replacement compatibility evidence and
explicit removal approval. The explicit guided native-argument route is the
bounded parameter capability delivered here, not full default-menu parameter
editing or initializer/force parity. Keep the legacy routes until their
replacement conditions are satisfied.

## Context

See `proposal.md` for motivation and `specs/func-init-quickstarts/spec.md` for the behavior contract.

The `func-init-execution` design originally assumed stack-first selection, one prospective stack and language, one generated Functions project, and one CLI-generated `.func/config.json` at the init target. Azure-Samples quickstarts can contain multiple independently configured Functions projects plus non-Functions projects, so those assumptions cannot describe the required experience.

The existing templating program already separates concerns:

- `template-engine-integration` owns installed catalog projection, constraint-aware resolution, primary outputs, dry-run, and invocation.
- `template-package-install` owns explicit `FuncTemplate` package lifecycle.
- `template-engine-constraints` owns workload constraint syntax, evaluation, diagnostics, and remediation.
- `template-engine-post-actions` will own the trusted Functions project configuration action.
- `azure-samples-template-pipeline` owns building and publishing quickstart packages.

This change composes those capabilities at the `func init` command boundary. It does not introduce a second package manager, remote search client, constraint evaluator, or post-action implementation.

## Goals / Non-Goals

**Goals:**

- Give basic templates and Azure-Samples quickstarts one installed project-template experience.
- Make interactive selection work when one template creates heterogeneous Functions projects.
- Keep restricted installed templates discoverable and actionable without implicit state changes.
- Preserve CLI ownership of project configuration serialization.
- Resolve project roots through TemplateEngine primary-output behavior so source and symbol renames remain authoritative.
- Preserve dry-run and preflight before destructive cleanup.
- Keep existing-project adoption separate from new solution scaffolding.

**Non-Goals:**

- Define workload constraint JSON, range semantics, feed lookup, or remediation algorithms.
- Define the configuration action identifier or its serialized argument syntax.
- Build the browse page, remote catalog, package search, or an install flow
  outside an explicit qualified `--quickstart` selection.
- Create a distinct quickstart or solution template type.
- Recursively adopt existing multi-project solutions.
- Roll back scaffolded content after a configuration I/O failure.
- Modify ordinary post-action policy beyond ordering it after mandatory project configuration.

## Decisions

### Quickstarts remain ordinary project templates

Every init-capable template uses standard `tags.type = project`. “Quickstart” describes source and curation, not an execution type. One selected template can generate:

```text
target/
|- src/api/          Functions project: Node / TypeScript
|- src/processor/    Functions project: Python / Python
|- web/              non-Functions project
`- infra/            non-project content
```

Only declared Functions project configuration actions participate in Func topology. Other generated content is opaque to init.

**Alternative considered:** use TemplateEngine `tags.type = solution`. This would create a second init template category without changing the invocation mechanism and would force users and authors to distinguish templates based on project count. It is rejected.

**Alternative considered:** introduce a `quickstart` type. Package provenance and curation do not change TemplateEngine execution semantics. It is rejected.

### Package installation remains explicit

#### Read-only browse presentation

The internal `IQuickstartBrowser.BrowseAsync(QuickstartBrowseRequest, CancellationToken)`
is registered with DI and reused by opt-in guided init. It does not add a
standalone browse command. Its default request returns no template nomination.
`AllowSelection` enables an explicit nomination, never authorization.
It resolves stack IDs and aliases from installed `IProjectStack` capabilities,
using the existing quickstart provider only to bridge legacy language spelling.
The selected stack filters discovery metadata, not producer-declared project
topology. No package name is used to infer a stack or exclude installed content.

Interactive browsing lists metadata statuses, opens details only after explicit
selection (even for a single result), and supports back, cancel, search, resource
and IaC filter changes. Clearing filters retains the caller's stack/language
context. Empty results remain navigable. Noninteractive browsing prints results
without prompting or choosing a template and requires an explicit stack when
multiple capabilities are installed. Catalog failures and cancellation propagate
to the caller rather than becoming empty success.

Single-selection titles and choices are literal text at the console adapter.
The selected value remains the original string, including brackets. Spectre
Console 0.55.0 supplies the upstream escaped-segment search-highlighting fix,
so typing to search also preserves these labels. This dependency version adds
Spectre.Console.Ansi and intentionally disables ANSI when stdout or stderr is
redirected. No renderer forces ANSI back on. No native grammar, identity,
provenance or acquisition policy is changed by this rendering correction.

The same renderer enriches human-readable `func quickstart info <id>` with
transport provenance, package metadata diagnostics and explicitly unverified
package claims. Legacy list/info JSON shapes and the source scaffolding command
remain unchanged. Entries with usable package and artifact metadata point to
`func init <path> --guided --stack <stack> --quickstart <id> --dry-run` for
read-only inspection, not a claim that remote acquisition is ready. Legacy or
incomplete entries retain an explicitly labeled legacy source-scaffolder hint.
Evidence references and opaque project/workload summaries are
displayed as claims only, never fetched or interpreted as approval. Stale metadata
is visibly identified. The only permitted writes are those of the existing
manifest metadata cache.

The default browser still neither opens the installed template store nor provides
an install/create action. Only the opt-in guided caller enables nomination and
composes the separate authority, consent, acquisition and creation gates above.

#### Internal exact-artifact binding

The additive artifact-binding layer is opt-in through
`AddNativeTemplateArtifactBinding`, alongside the staged native setup services.
The registration helpers do not register a command, enable the default init
route, populate production authority or authorize a producer rollout. The CLI
host now composes these helpers for its explicit `--guided` route.

`InitExecutionRequest.ArtifactBinding` carries a full ordinal template identity,
package ID, normalized version, source and SHA-256 coordinates, plus the selected
physical workload owner and any logical pointer, runtime identifier and entry
point. These are coordinates, not proof. Null preserves direct-init resolution,
short names, precedence, adoption and repair.

Bound init creates a new workload capability scope after acquisition has ended.
One current registry snapshot feeds workload registration, availability, stack
metadata and project-configuration support. Failed workload loading or
configuration refuses this scope rather than using a partial registration or
falling back to the original command container. The scope outlives preview,
creation and mandatory configuration. Its artifact verifier refuses subsequent
registry changes. It does not reuse an acquisition transaction or its leases.

Inside the fresh protected project session, init lists project entries once.
Exactly one entry must match the full identity and package coordinates. The
verifier then joins the current installed owner, bundled schema-2 association,
host/channel/role policy, retained provenance and the actual current managed
archive. It uses the existing complete archive inspector through the session's
filesystem, checks the discovered engine inventory and compares every mounted
file with that archive. The owner must also be the unique physical workload
registration loaded as a runtime capability by that same fresh scope. Retained
registry rows that are not the live loaded version cannot authorize the binding.
A receipt, package descriptor or manifest claim alone cannot satisfy the binding.
No path or mounted handle comes from the caller.

The singleton entry passes through `CreateGroupAsync` using the same flow's
object identity. There is no group expansion or short-name fallback. Existing
metadata, constraint, explicit filter, strict argument and required-value checks
still apply. The selected invocation retains its artifact verifier at the
existing source checks, including before output commits. Existing authoritative
preview, all-project configuration checks, target recheck and partial-output
reporting remain shared with direct init. Bound invocations reject concurrent use
of their session and cannot survive its disposal.

An optional internal `QuickstartBrowseRequest.InstalledQuery` enables duplicate
projection using a separate fresh, clean-store read flow. Remote rows are omitted
only for the corresponding independently proven current installation with exact
identity, package, version, source and digest. The remote reference stays
unverified. Missing proof leaves the row visible with installed-proof diagnostics.
Availability diagnostics remain present even for a proven duplicate that is
currently restricted. This is not a new quickstart classification or a
mixed-stack eligibility policy.

Browse projection and init dry-run require a clean native store. They do not
install content or recover a pending journal. Ordinary unbound browse still does
not inspect the native store. Empty production association authority yields no
qualified installation proof, even if a receipt is retained. Test packages and
fixture authorities demonstrate mechanics only, not production approval.
An explicit preview or experimental association permits inspection of matching
prerelease artifacts, while the existing planner still enforces the exact
channel policy and artifact range. For an installed artifact with no asserted
channel, prerelease admission is derived from that exact artifact version; the
same qualified association and exact byte checks still apply. A null channel is
not treated as stable.

#### Internal catalog preparation

The CLI has an internal read-only catalog adapter alongside the legacy manifest
service. Legacy quickstart commands and the default init route are unchanged. Both views
use the existing manifest URL, validation, local override, and metadata cache.
The public `QuickstartEntry` and legacy list/info JSON shapes are unchanged.

For joint manifest/producer review, the adapter recognizes a proposed optional
per-entry `packageReference` object. This is not a published producer schema or
permission to populate production registrations. Its proposed version 1 fields
are:

- `schemaVersion`, the integer `1`.
- `packageId`, `version`, `source`, and `templateIdentity`, the advertised exact
  package reference and full template identity. Version ranges are invalid.
  The proposed source representation is an absolute HTTPS URL without user
  information or a fragment. Parsing it does not approve that source.
- Optional `groupIdentity`, a nonempty descriptive identity.
- Optional `artifact`, containing `algorithm` (`sha256`), a 64-character
  hexadecimal `digest`, and a nonempty opaque `evidenceReference`.
- Optional `projects` and `requiredWorkloads`, retained as opaque JSON pending
  agreement on publication-derived summary formats. They are not evaluated as
  topology, workload constraints, or executable actions.

Missing, null, malformed, unsupported, and conflicting references are distinct
catalog outcomes. Otherwise-valid legacy entries remain available through the
legacy service. Duplicate catalog IDs, competing template package owners, and
conflicting advertised sources/digests for one package version receive explicit
conflict diagnostics. Filters do not hide conflicts elsewhere in the document.

Every structurally accepted reference remains **unverified**. Evidence
references are never fetched by this adapter. There is no approved/ready state,
installed ownership comparison, payload acquisition, or target creation.
Metadata transport records distinguish download, fresh/revalidated/stale cache,
and local override. Unknown cache origin remains unknown, including after a
304 response. Caller cancellation is not converted into stale-cache success.
Conditional requests use only an ETag from the configured source. An unsolicited
304 does not refresh the cache timestamp or label an unrelated cache revalidated.
The existing stale-cache fallback remains available with its original provenance.

Search/resource/IaC/language filtering preserves the legacy manifest semantics.
An optional legacy stack capability and existing quickstart language provider
can narrow those legacy language labels. This is not generated-topology
eligibility and does not decide heterogeneous-template behavior. No filter
infers stack identity from a package name.

Before a public browse consumer or acquisition flow ships, the owners must agree
extension placement/versioning, exact-reference variants, source normalization,
summary formats, authenticated approval and revocation/freshness/offline policy,
and the foundation's installed ownership and init handoff contracts. The
adapter does not change publishing inputs or establish a second registry.

`func init` queries only the installed template catalog. An unknown `--template` reference produces:

```text
Template '<reference>' is not installed.
Browse available templates: <Functions-owned URL>
Install a package with: func new install <package>
```

Without a remote discovery index, an uninstalled short name cannot be mapped reliably to an exact package ID. The browse experience supplies package-specific installation instructions.

A future trusted first-party implicit flow would require a signed trust index, source policy, consent behavior, offline behavior, and lifecycle ownership. It remains deferred rather than being approximated from package naming.

**Alternative considered:** automatically query and install NuGet.org after an unknown reference. This makes init mutate global package state, introduces trust ambiguity, and couples command execution to remote search. It is rejected initially.

### The browse URL is an indirection contract

The CLI displays one stable Functions-owned URL, preferably through a redirect controlled by the Functions team. The destination can later be a dedicated gallery, documentation page, or generated discovery site.

The awesome-azd gallery is a visual precedent but not the authoritative destination: its entries are azd-curated GitHub repositories, its source field requires GitHub URLs, and it does not represent `FuncTemplate` package IDs or install commands. This change does not depend on that site's schema or availability.

### Template-first selection replaces stack-first orchestration

The interactive flow becomes:

```text
load installed project-template groups with eligibility and declared projects
  -> narrow groups by stack/language filters
  -> display picker, unavailable groups included, and browse URL
  -> select one template group
  -> resolve template variant and parameters
  -> resolve active project configuration actions
  -> apply whole-template stack/language filters
  -> preflight effects
```

Template selection comes first because no singular stack or language can represent a heterogeneous template. Basic template groups can still contain stack/language variants and expose parameters after selection.

The catalog comes from the command's own session. It projects enough trusted configuration-action metadata to describe each template's potential stacks and languages, and its constraint results give the unavailable state, without invoking the template. Authoritative active actions are resolved with the selected candidate and final parameters before scaffolding.

**Alternative considered:** keep the stack/language/template prompt order and branch to a separate quickstart picker. This fragments installed project templates and makes package type determine command UX. It is rejected.

### Restricted templates are visible but unavailable

The picker contains eligible and restricted installed groups:

```text
Select a project template:
> Basic Node project
  Event processing solution       unavailable: requires workloads
  Python OpenAI quickstart

Browse more templates: <URL>
```

Restricted entries cannot be selected. Concise summaries fit the picker; detailed constraint diagnostics and calls to action are rendered after or alongside it using structured results from the constraint system. An explicitly requested restricted template bypasses the picker but produces the same detailed remediation.

When one eligible group remains beside unavailable ones, the picker is still shown so the unavailable ones stay visible. When no eligible group remains, init skips the picker. It reports that no installed template can be used, shows each distinct call to action once, adds the browse URL, and exits without modifying the target. The missing piece is usually a workload rather than a template, so the calls to action matter more than generic install guidance.

Func does not infer package commands from constraint text. `template-engine-constraints` owns the distinction between missing, outdated, incompatible, unevaluable, and failed constraints and supplies any appropriate call to action.

**Alternative considered:** hide restricted templates. Users would not know an installed quickstart exists or how to unblock it. It is rejected.

### Workload constraints cover heterogeneous projects

Project templates declare workload constraints, as `template-engine-constraints` defines, for all required stack, host, bundle, and related workload availability. Func init requires an eligible result before cleanup or scaffolding.

This change deliberately does not require an existing-project bundle constraint. A new project has no resolved bundle identity or version; bundle capability needed to use the generated solution is represented as workload availability.

The exact declaration shape and version policy remain in `template-engine-constraints`. The init contract consumes only:

```text
TemplateConstraintOutcome
|- Eligible
`- Restricted
   |- Summary
   |- Diagnostics
   `- CallsToAction
```

### Configuration actions are the project topology

Every generated Functions project is represented by one mandatory trusted configuration action. Conceptually, each action supplies:

```text
FunctionsProjectConfiguration
|- PrimaryOutputReference
|- Stack
`- Language
```

The primary output is any file located directly in the project root. TemplateEngine resolves its final relative path after `sourceName`, symbol `fileRename`, explicit rename, source target, and conditions. The action's resolved output path identifies:

```text
project root = parent(resolved primary output)
config path  = project root/.func/config.json
```

The author can choose `host.json`, a project file, `package.json`, or another stable root file. Func does not impose a stack-specific filename.

The exact action ID, argument representation, rename propagation, and projection model belong to `template-engine-post-actions`. That capability must make the configuration action distinguishable from ordinary actions and expose its validated metadata before invocation.

**Alternative considered:** put stack/language properties directly on `primaryOutputs`. TemplateEngine accepts unknown JSON properties but drops them from `PrimaryOutputModel` and `ICreationResult`; Func would need a second raw parser and fragile correlation. It is rejected.

**Alternative considered:** add a topology map to `func.host.json`. Authors would declare constraints, primary outputs, and a second mapping back to those outputs. It is rejected as redundant.

**Alternative considered:** include `.func/config.json` in template content. This handles paths naturally but couples packages to the CLI config schema and permits templates to author CLI-owned state. It is rejected in favor of a trusted action.

### Configuration declarations are preflighted before scaffolding

After candidate parameters are complete, init resolves active configuration actions and primary outputs during TemplateEngine effects evaluation. Preflight rejects:

- no active configuration action;
- unsupported or non-trusted action identity;
- optional or continue-on-error configuration behavior;
- missing, ambiguous, or inactive primary-output reference;
- output outside the target;
- output whose parent cannot be a project root;
- empty or non-canonical stack/language;
- duplicate resolved project roots;
- template file effects targeting `.func/config.json`;
- configuration output collisions with another planned effect.

This validation occurs before `--force` cleanup. It validates declaration and planned paths, not the future filesystem contents.

No temporary rendering is performed. The actual primary-output file is verified after scaffolding before configuration is written.

### Stack and language are whole-template filters

After active configuration actions are known:

```text
--stack S
  -> every active project.Stack equals S

--language L
  -> every active project.Language equals L
```

A heterogeneous template is valid when those singular filters are absent. Supplying a filter is an assertion about the whole generated topology, not a request to rewrite one or all action values.

For conditional projects, only active configuration actions participate after final template parameter resolution. This may require completing template parameters before a filter can be authoritatively evaluated.

Before a template is selected, the filters narrow the picker to templates that can still match. Every unconditional project must match all supplied filters, and at least one project must match them all. A conditional project that uses another value does not remove the template, because parameters can still turn it off. If it stays on, the check after parameters rejects the template and names that project. A group remains in the picker when any of its variants can still match, and the same rule narrows the variants of the selected group.

Standard TemplateEngine language tags remain useful for homogeneous variants but are not required to encode a mixed topology. Configuration actions are authoritative for each generated project's canonical values.

**Alternative considered:** match when any project satisfies the filter. A user asking for a Node project could receive a Node/Python solution, making the explicit filter misleading. It is rejected.

### Init's context has no stack or language

The template execution context supplies the target working directory and solution root. It never supplies a stack or language, whether a template's projects share one or not. Configuration actions already declare each project's values, and the context is fixed before a template is selected, when those values are not known.

Workload constraints do not depend on a singular stack or language. A template that binds `func:stack` or `func:language` receives its bind default.

This preserves the invariant that host context reflects true resolved state.

### Required configuration is a finalization phase

Actual execution is ordered:

```text
Prepare
  validate candidate, actions, constraints, and combined effects
  validate each action's installed stack recognizes its canonical language
  clear non-git target content when --force is authorized

Template
  invoke selected project template

Finalize
  for each configuration action in declared order:
    verify resolved primary-output file exists
    compute parent project root
    atomically write .func/config.json

Post
  execute ordinary post-actions in declared order
```

Stack and language support is checked before cleanup, so `--force` never clears a target for a project init cannot configure.

Configuration actions use the current CLI serializer and are mandatory. They cannot be skipped by future ordinary-action consent policy. Ordinary post-actions never run when finalization is incomplete.

Dry-run resolves the same primary outputs and action metadata, adds planned `.func/config.json` effects, and reports ordinary actions without executing any action.

**Alternative considered:** implement configuration as an ordinary action. Optional/continue-on-error semantics and post-action consent could leave a nominally successful project without required CLI state. It is rejected.

### Finalization failure preserves generated content

All configuration declarations and expected paths are preflighted, but I/O can still fail after template creation. The command stops at the first failed configuration write, skips remaining ordinary post-actions, reports the failed project, and leaves:

- generated template files;
- any configurations already written;
- no synthetic rollback of source-controlled output.

This matches the existing planned partial-initialization boundary while extending it to multiple projects. Each individual configuration file is written atomically.

### Adoption remains a separate root-project path

Existing adoption and healing do not list or invoke templates and do not execute configuration actions. Supplying `--template` without `--force` remains an error on those paths.

This change does not recursively scan a solution for `host.json` or `.func/config.json`. New multi-project topology is explicit in template actions; inferring topology for arbitrary existing directories is a separate problem.

### Package authoring must satisfy the runtime contract

The Azure-Samples packaging pipeline must produce templates that include:

- `tags.type = project`;
- required workload constraints;
- at least one project primary output;
- one trusted configuration action per Functions project;
- canonical stack and language action values.

For a repository lacking authored template configuration, the repository-owned `.github/azure-functions-template.yaml` synthesis descriptor can declare one or more static Functions project roots with canonical stack and language. The packager uses each root `host.json` as a primary output and adds the corresponding configuration action.

Parameterized or conditional project topology cannot be expressed by the synthesis descriptor and requires authored template configuration and project actions. Authored root `.template.config/template.json` and `.github/azure-functions-template.yaml` are mutually exclusive. The packaging pipeline loads and dry-runs either resulting template through the same validation path.

## Risks / Trade-offs

- **[Template-first flow revises the pending init execution design]** -> Treat this focused change as authoritative for selection order and restack/reconcile the companion specification before implementation.
- **[Configuration action metadata duplicates project facts already present in source]** -> Keep only the stable primary-output reference, stack, and language; avoid a separate topology manifest.
- **[Conditional topology delays whole-template filtering]** -> Resolve required parameters and active actions before applying the authoritative filter.
- **[Project templates cannot read a stack or language from the host]** -> Configuration actions declare both for each project, and a template that binds them sets a default.
- **[Finalization can fail after files are generated]** -> Preflight every declaration and path, use atomic writes, skip ordinary actions, and report partial initialization without destructive rollback.
- **[Restricted picker entries can overwhelm the prompt]** -> Show concise summaries in the picker and render detailed calls to action separately.
- **[Browse URL destination is not yet designed]** -> Use a Functions-owned redirect so the CLI contract remains stable.
- **[Synthesized quickstarts require reviewed project topology]** -> Require static project roots, stacks, and languages in the repository-owned synthesis descriptor and use authored template configuration for parameterized or conditional topology.

## Migration Plan

1. Complete the workload constraint outcome and required configuration-action contracts in their focused changes.
2. Extend template catalog and resolved-candidate projections with project configuration action metadata and unavailable summaries.
3. Reconcile `func-init-execution` by replacing stack-first selection and CLI companion configuration generation with the template-first and action-finalization contracts.
4. Migrate built-in project templates to declare primary outputs, workload requirements, and configuration actions.
5. Update Azure-Samples package synthesis and authored-template validation to require the same contract.
6. Add installed quickstart rendering, browse guidance, restricted entries, whole-template filters, and multi-project success output.
7. Exercise the flow with homogeneous, heterogeneous, conditional, restricted, dry-run, forced, and partial-failure fixtures.
8. Enable the experience only after default project templates and representative Azure-Samples packages satisfy the authoring contract.

Rollback restores the previous init selection and CLI-owned single-root configuration step. Installed template packages remain managed by the same template package lifecycle, but packages relying only on multi-project configuration actions will not be fully usable by the prior init flow.
