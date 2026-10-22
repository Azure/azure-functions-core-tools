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
- Build the browse page, remote catalog, package search, or implicit install flow.
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
is registered with DI for a later guided init caller. It does not add a public
command or connect to init yet. It returns no template selector or authorization.
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

The same renderer enriches human-readable `func quickstart info <id>` with
transport provenance, package metadata diagnostics and explicitly unverified
package claims. Legacy list/info JSON shapes and the source scaffolding command
remain unchanged. Evidence references and opaque project/workload summaries are
displayed as claims only, never fetched or interpreted as approval. Stale metadata
is visibly identified. The only permitted writes are those of the existing
manifest metadata cache.

The default browser still neither opens the installed template store nor provides
an install/create action. Consent, acquisition and public init integration remain
subsequent gates.

#### Internal exact-artifact binding

The additive artifact-binding layer is opt-in through
`AddNativeTemplateArtifactBinding`, alongside the staged native setup services.
It does not register a public command, enable a default route, populate production
authority or authorize a producer rollout.

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
channel policy and artifact range.

#### Internal catalog preparation

The CLI has an internal read-only catalog adapter alongside the legacy manifest
service. It does not change the public quickstart or init commands. Both views
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
