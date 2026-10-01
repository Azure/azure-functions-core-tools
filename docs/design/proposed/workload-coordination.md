# Workload coordination and CLI/workload compatibility

**Date:** October 1, 2026

**Issue:** [#5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)

**Scope:** Core Tools v5 CLI, Workload SDK, workload packages, and release coordination

## 1. Summary

Keep the CLI a thin anchor. Workloads declare the contracts they require in
SDK-generated package metadata; the CLI evaluates those requirements during
resolution, installation, and activation. Profiles remain independently published
constraints for cloud-aligned host and bundle selection. Do not ship a workload
bill of materials inside the CLI.

Compatibility includes more than the managed Abstractions API. The migration of
Host and Python worker packages to RID pointers changes the packaging contract,
even though these content packages do not implement the CLI's managed workload
interface. Original packages can therefore be incompatible with a newer CLI while
correctly packaged replacements are compatible.

The templating switch presents the same coordination problem: replacing
`IProjectInitializer` with `IProjectStack` changes the managed workload contract,
while replacing template content workloads with independently shipped template
packages changes what the CLI can discover and consume. A new CLI must not become
the default while installed stack workloads or available templates still require
the removed paths.

This design separates:

| Concern | Authority |
| --- | --- |
| Can the CLI consume or load this package? | Package requirements evaluated against CLI-provided contracts |
| Does this host/bundle combination match the target cloud environment? | Profile constraints and release validation |
| What exact combination was tested or restored? | Release evidence; explicit version pins for consumers |

All new fields, contract identifiers, and contract versions below are proposals.
Historical package versions are identified separately from illustrative examples.

## 2. Motivation

[#5413](https://github.com/Azure/azure-functions-core-tools/issues/5413) identifies
the original gap: workloads share CLI-provided contracts, but no declared
version-compatibility gate protects either newer workloads on older CLIs or older
workloads on newer CLIs.

The RID-package migration
([#5624](https://github.com/Azure/azure-functions-core-tools/issues/5624)) and
templating switch
([#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655)) add concrete
motivations: packaging and API changes can invalidate existing packages, and CLI
releases must be coordinated with compatible replacements. Section 7 covers these
migration examples.

## 3. Goals and non-goals

Goals:

- Declare CLI/workload compatibility explicitly while preserving independent
  package versions and compatible workload release cadences.
- Reject forward and reverse incompatibility before loading CLI extensions or
  consuming content, with actionable CLI-owned errors and usable repair commands.
- Reuse one evaluator for workload contracts, including logical pointers and RID
  implementations; keep template-format and profile checks in their own subsystems.
- Make RID and templating migrations actionable on fresh and upgraded machines.
- Decide the GA enforcement scope: activation checks are the minimum, selected-
  package install checks are preferred, and automatic compatible selection can follow.
- Coordinate CLI-first releases with a lightweight completion/recovery checklist,
  accepting a short release gap where agreed and using stricter gates when needed.

Non-goals:

- A general transitive dependency solver or CLI-shipped workload manifest.
- Automatic background installation, workload updates, or CLI downgrades.
- Requiring automatic compatible-version discovery, fallback, or exact replacement
  recommendations for GA.
- Guaranteeing behavioral correctness from version comparisons alone.
- Guaranteeing uninterrupted setup/update during every release, or requiring atomic
  cross-repo publication and isolated feeds for every change.
- Redesigning profiles, workload ownership, or distribution channels.
- Adding a project lock-file format in this change; reproducibility can be designed
  separately without changing the compatibility model.

## 4. Versioned contracts

The CLI publishes a small built-in description of its own supported contracts.
This is not a list of workload packages. The same description is available to the
SDK/release pipeline and to CLI diagnostics.

| Contract | Meaning | Required by |
| --- | --- | --- |
| `cli-abstractions` | Managed public boundary implemented by the CLI, including shared boundary types | CLI-loaded extensions (`kind: workload`) |
| `workload-packaging` | Package roles, RID indirection, payload layout, and CLI consumption rules | Every package, including pointers and content |

Use SemVer and NuGet version-range semantics. A package requirement such as
`[2.3.0,3.0.0)` accepts compatible additions but excludes a breaking contract
change. A CLI satisfies a requirement only if it provides a supported contract
version within that range. It must not claim an older contract merely because its
own version is newer; supporting multiple contract majors requires a real,
validated compatibility implementation.

`cli-abstractions` tracks the released Abstractions package's contract version,
not the CLI version or an assembly version inferred at runtime. Its compatibility
promise also covers shared DI and `System.CommandLine` types. A breaking change in
those boundary types requires a breaking contract release. If that promise cannot
be maintained, introduce explicit requirements for those shared contracts before
shipping the change; do not silently widen the promise.

`workload-packaging` is independently owned and versioned by Core Tools. For the
examples below, `1.0.0` means the original packaging model and `2.0.0` means the
RID-pointer model. These numbers are illustrative, not existing release metadata.
A CLI may support both models only if it retains and tests both consumption paths.
The CLI in the migration example supports only the newer model.

A minimum CLI version additionally covers CLI fixes or behavior not captured by
these contracts. A minimum alone cannot express that a newer CLI dropped an older
packaging or API contract.

## 5. Authoritative package metadata

Introduce a new package-manifest schema with mandatory compatibility metadata.
Do not add safety-critical fields only to schema v1: older CLIs could ignore them.
Existing strict unknown-schema rejection gives older CLIs a safe failure instead
of allowing them to execute packages with requirements they cannot interpret.

Illustrative CLI-loaded extension:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v2/schema.json",
  "kind": "workload",
  "entryPoint": {
    "assemblyPath": "Example.Workload.dll",
    "type": "Example.Workload"
  },
  "compatibility": {
    "contracts": {
      "cli-abstractions": "[2.3.0,3.0.0)",
      "workload-packaging": "[2.0.0,3.0.0)"
    },
    "minCliVersion": "5.2.0"
  }
}
```

`compatibility.contracts` is required and non-empty. `minCliVersion` is optional,
but must be a valid full NuGet version when present. Unknown required contract
identifiers, invalid or empty ranges, and missing role-required contracts fail
validation. Stable selection excludes prerelease packages by default; explicit
prerelease selection still has to satisfy all requirements, including CLI version
ordering (`5.2.0-preview.1` does not satisfy a minimum of `5.2.0`).

Content packages must declare `workload-packaging`; they do not need a fictitious
Abstractions reference. Pointers declare their own packaging requirements. A
selected implementation declares its requirements independently, and the CLI must
satisfy both. Meta packages declare packaging requirements and every selected
member is evaluated separately; no aggregate declaration bypasses a member gate.

Keep `workload.json` authoritative. NuGet tags may be generated as discovery hints,
but never authorize installation. Do not encode these requirements as NuGet
dependencies that cause a second copy of a CLI-provided contract to be installed.

### SDK responsibilities

The Workload SDK:

- Derives a conservative managed contract minimum from the resolved Abstractions
  reference and uses the next breaking major as the default exclusive upper bound.
  A lower declared minimum requires explicit configuration and compatibility
  validation, not an assumption that compilation proves compatibility.
- Stamps packaging requirements from the package model it emits, including
  pointer and implementation manifests.
- Validates shared assembly references and excludes CLI-owned contract assemblies
  from workload payloads.
- Validates CLI-loaded extension target frameworks/shared-framework requirements against the
  intended CLI runtime baseline. A contract range alone does not make a workload
  targeting a newer runtime loadable.
- Emits identical requirements from identical build inputs; merely upgrading the
  SDK does not raise requirements unless generated output requires a newer contract.

The SDK cannot infer a CLI release number from an independently published
Abstractions version. Use a versioned, release-owned contract-to-first-CLI mapping
when deriving a minimum CLI automatically; until a mapping exists, authors must
declare any required CLI minimum and release CI must validate it. Do not guess a
CLI minimum for a contract that has not yet shipped in a CLI.

## 6. Open question: what compatibility enforcement is in scope for GA?

Should GA enforce compatibility only at activation, also reject incompatible
installations, or include compatible-version discovery? These are incremental
options using the same metadata and shared evaluator; the scope is not yet decided.

| Option | Behavior | Tradeoff |
| --- | --- | --- |
| **A. Activation-time checks (minimum)** | Read metadata and reject incompatibility before extension loading/registration, Host launch, or worker/template consumption, including after CLI upgrades/downgrades | Install/update may succeed but the next command fails; recovery may require selecting or reinstalling an older version |
| **B. A plus selected-package install checks (preferred)** | Keep existing version selection, then validate the selected package before committing install/update state; apply equally to local packages and installed-payload reuse | Prevents accepting an unusable update, but still fails if the selected version is incompatible even when an older compatible version exists |
| **C. B plus compatible-version discovery** | Search candidates newest-first and automatically select the newest compatible version under existing source, profile, major-version, and prerelease constraints | Best acquisition experience, but adds catalog inspection, fallback, and caching complexity |

**Recommendation:** prefer B for GA if feasible; A is the minimum safety boundary.
Hold C, automatic fallback across older candidates, exact compatible-version
recommendations, and catalog-wide metadata indexing/caching optimizations until
after GA. Options A/B do not promise "latest compatible": users may need to upgrade
the CLI or explicitly select a compatible package.

In either GA option, the check and error output must be CLI-owned and run before
workload code executes, not catch an arbitrary failure afterward. Show the package
and version, required versus provided contract/CLI version, and recovery guidance;
only name a specific replacement when known. Commands requiring an incompatible
component fail with a nonzero exit code, while inventory, uninstall, and update
remain usable. Skip incompatible extensions during discovery without silently
hiding their status.

Preserve existing schema, structural, RID, source, and ownership checks regardless
of scope. Evaluate both a pointer and its selected implementation; explicit pins
and `--force` cannot bypass activation safety. Template-format checks stay in the
template subsystem. Failed installations and partial setup must report their actual
state; option A cannot promise that an accepted update remains runnable.

The install/selection behavior elsewhere in this document describes the fuller
target, not a settled GA commitment. Phase those requirements according to this
decision. Apply Section 8's release-window and recovery policy: compatibility errors
do not replace completion checks, but clear rejection may be an accepted old-client
experience without automatic fallback or a separate feed.

## 7. Migration examples: RID packages and templating

### RID migration: Host and Python

#### Package shape

For Host, the logical identity stays
`Azure.Functions.Cli.Workloads.Host`; the Windows x64 implementation is
`Azure.Functions.Cli.Workloads.Host.win-x64`. Python worker uses the same convention
with `Azure.Functions.Cli.Workloads.Workers.Python` and its RID-suffixed packages.
The Python stack (`python`) is a separate CLI-loaded extension, not the Python worker
content package (`python-worker`).

Illustrative Host pointer, published at the same version as all its implementations:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v2/schema.json",
  "kind": "rid-pointer",
  "packages": {
    "win-x64": "Azure.Functions.Cli.Workloads.Host.win-x64",
    "linux-x64": "Azure.Functions.Cli.Workloads.Host.linux-x64"
  },
  "compatibility": {
    "contracts": {
      "workload-packaging": "[2.0.0,3.0.0)"
    }
  }
}
```

Illustrative selected implementation:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v2/schema.json",
  "kind": "content",
  "runtimeIdentifier": "win-x64",
  "compatibility": {
    "contracts": {
      "workload-packaging": "[2.0.0,3.0.0)"
    }
  }
}
```

Its nuspec must declare `FuncCliWorkloadRidPackage` and `rid:win-x64`; its executable
must be under `tools/win-x64`. Python worker payloads follow the same RID-root
convention. Do not assume every workload supports every CLI RID: the current Python
worker project, for example, excludes `win-arm64`. Report unsupported RID clearly;
do not fall back to x64, another OS, or `tools/any`.

#### Compatibility outcomes

| Candidate | New RID-only CLI result | Reason |
| --- | --- | --- |
| Original Host/Python worker package using the legacy package role/layout | Reject | Unsupported packaging model, regardless of payload or Host/worker runtime version |
| Legacy package with no compatibility declaration | Reject under the GA policy | Unknown compatibility; do not manufacture a supported range |
| New pointer with missing current-RID implementation | Reject that candidate | Incomplete publication |
| New pointer and implementation with valid RID metadata but payload under `tools/any` | Reject before installed state is published | Package violates declared layout |
| New pointer and implementation with correct role, exact version, RID layout, and satisfied requirements | Accept | Both declared and structural requirements are met |
| New Python stack requiring a newer Abstractions contract | Reject even if its worker content is valid | Managed API and worker packaging requirements are independent |

The first four outcomes are not fixed by an Abstractions minimum alone. The final
two explain how corrected packages become usable without requiring every workload
to adopt the CLI's version number.

Historical Host `4.1048.200-preview.3` illustrates a layout correction, not a
retroactive claim that it contains this proposed v2 metadata. Existing immutable
packages must be republished at new versions to adopt the declaration. The same
migration policy applies to original Python worker packages; no historical Python
package version is assumed here.

### Templating migration: stack contracts and template packages

The [v5 templating epic #5657](https://github.com/Azure/azure-functions-core-tools/issues/5657)
rebuilds `func new` and `func init` on Microsoft.TemplateEngine. The
[command switch #5655](https://github.com/Azure/azure-functions-core-tools/issues/5655)
removes each old command path when its replacement lands and moves in-repo stack
workloads from `IProjectInitializer` to `IProjectStack` together. This is a second
concrete application of the compatibility gate, not just a future possibility.

There are two separate boundaries:

| Boundary | Example failure | Required protection |
| --- | --- | --- |
| Managed stack API | A previously installed Python or Node stack workload implements `IProjectInitializer`, but the new CLI expects `IProjectStack` | Version the breaking `cli-abstractions` change; reject the old workload before loading it and identify a compatible replacement |
| Template format and discovery | Existing template content workloads contain data for the old providers, but the CLI now discovers Microsoft.TemplateEngine template packages | Validate template package classification, supported format/features, and availability through the template subsystem; do not treat an installed old content workload as a usable replacement |

Moving all stack projects in one source change does not update packages already
installed on users' machines. Conversely, publishing a new stack workload using
`IProjectStack` must not let an older CLI install and load it as though it still
implemented the old API. The managed contract range handles both directions.

Template packages are distinct from workload packages. Following
[#5632](https://github.com/Azure/azure-functions-core-tools/issues/5632), workload
installation must direct template-package users to `func new install`, and template
installation must reject workload-only packages. Do not require a fictitious
Abstractions reference or impose this document's `workload.json` schema on ordinary
template packages. Their supported format and engine-feature requirements must be
checked by the template subsystem before discovery/execution; any new declaration
needed there belongs in the template design. Bundle, target-framework, and other
template constraints remain additional checks, not substitutes for CLI compatibility.

For example, after upgrading the CLI, a machine may have an original Python stack
workload and the original Python templates content workload installed. Installing
the new template package alone does not repair the old stack API. Updating the stack
alone does not supply templates in the new format. Recovery must identify both
requirements and use explicit acquisition (or the separately approved companion
acquisition flow), without silently converting package types or deleting old assets.

Gate the command switch and release promotion on compatible stack workloads plus
published, installable first-party template packages. As required by
[#5647](https://github.com/Azure/azure-functions-core-tools/issues/5647) and #5655,
default project templates must cover every supported stack on fresh and upgraded
machines before `func init` loses its old path. Verify `func new` item generation
and `func init` creation, adoption, and repair against the actual published packages.
Successful package installation alone does not demonstrate that either command works.

## 8. Profiles and release coordination

Profiles constrain host/bundle versions and environment features; they do not
authorize incompatible packages. Resolve the intersection of profile constraints,
package compatibility, RID availability, and user selection policy. A profile
pinning an original incompatible Host package must fail and identify the pin;
never silently broaden it to select a newer Host.

`func start` uses installed compatible assets only. Setup/profile-install commands
are explicit acquisition operations. Profile publication and CDN behavior remain
owned by [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329) and
[#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332).

### Release ordering

**Release the supporting CLI first, then dependent workloads immediately afterward.**
The team's normal combined release window is under two hours. Temporary setup or
update failures during that window may be an acceptable, documented availability
tradeoff; they do not require elaborate promotion infrastructure for every release.
Already installed compatible combinations can continue working.

Distinguish that short gap from persistent incompatibility: users remaining on an
older CLI may still encounter incompatible new workloads after publication finishes.
Section 6's GA scope may provide clear rejection and upgrade/pin guidance rather
than automatic compatible-version selection. Verify actual old-client behavior;
shipping checks in the new CLI does not retrofit older clients.

### Release checklist

1. Validate the intended CLI/package combination and assess both old-CLI/new-package
   and new-CLI/old-package behavior. For breaking transitions, explicitly decide
   whether the short failure window is acceptable. Otherwise make the CLI opt-in
   and hold default promotion until replacements are available.
2. Publish the CLI, then required workloads, with all advertised RID implementations
   before their logical pointers. Keep package versions immutable. Ordinary
   compatible workload updates remain independently releasable.
3. Before declaring the release complete, verify consumer-feed availability and
   fresh setup/upgrade smoke tests, including Host startup and affected templating
   flows. Publish profile updates only after referenced packages are obtainable.
4. If publication fails, a required RID is missing, or the window exceeds the
   expected two hours, pause further promotion and choose fix-forward or rollback.
   Record the tested versions and link workload release notes.

Stricter promotion gates or isolated feeds are precautions for high-impact breaking
changes, not universal requirements. Clear rejection can be an accepted old-client
experience; unsafe or opaque failures need a specific mitigation. Unknown-schema
rejection alone does not guarantee useful recovery guidance. The templating
switch's own prerequisites in Section 7 still apply.

CLI owners own compatibility enforcement; workload owners own valid artifacts and
publication; release/profile owners own completion checks and recovery. Rollback
must account for already installed newer workloads that may not support the older
CLI; it is not an automatic workload downgrade. Retain the internal tooling feed
only for confirmed partner needs, not as a requirement of this coordination model.

## 9. Rollout and implementation plan

1. Decide the Section 6 GA scope: activation-only checks (A) or the preferred
   addition of selected-package install checks (B). Agree on contract ownership/
   versioning, publish the v2 schema and evaluator semantics, and update the specs.
2. Implement SDK stamping and pack validation for ordinary, content, pointer, and
   implementation packages. Establish the contract-to-CLI mapping process.
3. Implement CLI-owned checks and diagnostics before extension loading/registration
   and Host/worker consumption, including reuse of installed packages. Coordinate
   template checks with that subsystem and preserve repair commands. If B is chosen,
   also validate selected catalog/local packages before install/update state changes,
   without adding compatible-version discovery.
4. Validate candidate CLI/package combinations, including fresh installs, upgrades,
   downgrades, and actual old-client failure/recovery behavior. Release the supporting
   CLI first, then dependent packages. Use isolated feeds or hold default promotion
   only where the change-specific risk assessment requires it.
5. Apply the Section 8 release checklist and change-specific promotion decision;
   verify replacement availability and clean-machine/upgrade smoke tests before
   declaring completion. Make mandatory compatibility metadata a GA requirement.

Defer automatic compatible-version discovery (C), older-candidate fallback, exact
replacement recommendations, and catalog indexing/cache optimizations until after
GA. They are not prerequisites for A or B.

Legacy package metadata is not evidence of compatibility. The proposed GA minimum
rejects packages without the declaration at activation; install-time rejection
depends on the Section 6 scope decision. Preserve inventory/uninstall access.
This is an intentional pre-GA breaking change requiring migration notes and
replacement packages within the agreed release sequence. There is no `--force`
compatibility bypass or silent fallback to legacy interpretation.

Existing registry rows may remain readable; registry readability does not imply
their referenced payloads may activate. The package schema evolves independently
from the installed-registry schema.

## 10. Acceptance criteria

### GA baseline: activation safety and existing validation

These criteria apply to either A or B. Preserve existing installation/schema/RID
validation; activation-only scope does not remove those checks. New compatibility
checks must run before consuming a package, even if installation previously succeeded.

| Scenario | Expected outcome |
| --- | --- |
| Workload requires newer managed contract/CLI | Reject before activation with package/version, required versus provided compatibility, recovery guidance, and a nonzero exit code |
| CLI has dropped the workload's contract major | Installed workload cannot activate; inventory/update/uninstall remain available |
| CLI is downgraded after a workload update | Requirements are rechecked; no reuse of a stale compatibility decision |
| Original Host or Python RID package on the new CLI | Actionable packaging incompatibility; replacement is explicit, not silently installed |
| Correct Host/Python pointer plus same-version current-RID implementation | Install succeeds and host/worker smoke test passes |
| Implementation has wrong role, suffix, tag, RID, ID, or version | Rejected before installed-state publication |
| RID metadata is valid but required payload is under `tools/any` | Structural/payload validation fails before activation |
| Pointer source lacks an implementation that exists on another feed | No cross-feed substitution |
| Python worker on an unsupported RID | Clear unsupported-RID error; no architecture fallback |
| Old `IProjectInitializer` stack workload on the new `IProjectStack` CLI, or the reverse | Reject before loading with managed-contract diagnostics and an explicit compatible-package recovery path |
| Only legacy template content workloads are installed after the command switch | Explain that new-format template packages are required; do not report usable templates or silently use the removed provider |
| New compatible stack workloads and template packages are installed | `func init` creation/adoption/repair and `func new` generation pass their baseline scenarios |
| Explicit version/profile pins an incompatible original package | Pin remains unchanged; actionable failure |
| Package came from a local install, installed reuse, or meta member | Same activation checks as a catalog package, including both pointer and selected implementation where applicable |
| Unsupported schema, unknown contract, missing declaration, or malformed range | Explicit rejection before consumption; preserve earlier schema/structural rejection and do not partially interpret requirements |
| Feed unavailable or candidate corrupt | Explicit availability/integrity error, not a misleading compatibility result |
| Incompatible extension encountered during discovery | CLI-owned output works without extension services; inventory/uninstall/update remain accessible and commands requiring the extension fail clearly |
| Activation-only update installs an incompatible version | Installation may succeed; activation fails with repair guidance rather than claiming the update remains usable |
| Partial setup | Report actual component successes/failures; do not claim a cross-workload transaction or guaranteed runnable state |
| Offline activation with compatible installed packages | No acquisition required; compatibility checks still run locally |

### Scope-dependent acquisition criteria

| Scope | Expected outcome |
| --- | --- |
| B: selected-package install/update validation | Reject incompatibility before committing installation changes and leave the existing installation unchanged; cover catalog, local, and reused payload paths |
| A or B: latest selected package is incompatible but an older compatible one exists | Clear failure at the chosen enforcement point is acceptable; no automatic fallback required |
| C: automatic compatible-version discovery (post-GA) | Choose the newest compatible candidate within existing source, profile, major-version, and prerelease constraints; explain skipped incompatible versions |
| All options: explicit version/profile pin | Never substitute another version or silently broaden constraints |

### Release completion and recovery

| Scenario | Expected outcome |
| --- | --- |
| Templating command switch before required first-party templates are obtainable | Preserve the switch-specific prerequisites in Section 7; validate supported stacks on fresh and upgraded machines |
| CLI release precedes required compatible RID packages | Apply the agreed short-window policy or hold default promotion; do not declare release completion until packages are available |
| Dependent workloads are ready but their supporting CLI is not obtainable | Block consumer-feed publication; keep artifacts staged |
| Old CLI resolves from a feed containing newer incompatible workloads | Compatible selection or clear rejection with upgrade/pin guidance; verify actual behavior and mitigate unsafe or opaque failures |
| Old CLI rejects the new schema but does not fall back to a compatible package | May be accepted under the chosen GA scope if recovery guidance is adequate; fallback is not mandatory |
| New CLI supports all currently supported workloads | Default promotion may precede dependent workload publication |
| Workload publication fails, a required RID is missing, or the expected two-hour window is exceeded | Do not declare completion; pause further promotion and choose fix-forward or rollback, accounting for already installed newer packages |

Use unit tests for range/schema/evaluator behavior, package-fixture tests for
layout and ownership, and end-to-end clean-machine plus upgrade/downgrade tests.
Run the acquisition tests for the selected scope; do not make C a GA blocker.
For Host, assert startup readiness and an HTTP response; for Python, invoke a
function to prove worker discovery and launch, not merely successful installation.
Exercise each advertised RID on matching runners, including executable permissions
on Unix platforms.
For templating, exercise both a clean machine and one retaining original stack and
template workloads, assert generated project/function content, and preserve bundle
and target-framework constraint checks.

## 11. Alternatives and decisions

- **Minimum CLI only:** insufficient for dropped packaging models or managed
  contract majors. Retain it as an additional constraint, not the primary contract.
- **CLI-shipped workload manifest:** couples independent releases and becomes
  stale. Use package requirements and external release evidence instead.
- **NuGet dependencies or tags as the authority:** dependencies imply acquisition
  semantics we do not want for shared contracts; tags are discovery metadata.
- **Central tested-workload sets:** useful later for opt-in reproducibility, but
  unnecessary for the compatibility gate and not a substitute for it.
- **Accept legacy packages and wait for runtime failure:** rejected; the RID
  migration already demonstrates that package install success is insufficient.

Before approval, owners must finalize the initial contract versions, the required
distribution channels for promotion, and the partner tooling feed inventory.
These do not change the selected contract-aware architecture.

## References

- [Coordination issue #5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)
- [RID naming/migration issue #5326](https://github.com/Azure/azure-functions-core-tools/issues/5326)
- [RID-pointer implementation #5512](https://github.com/Azure/azure-functions-core-tools/pull/5512)
- [Host layout correction #5614](https://github.com/Azure/azure-functions-core-tools/pull/5614)
- [Public-feed/CLI mismatch #5624](https://github.com/Azure/azure-functions-core-tools/issues/5624)
- [Templating epic #5657](https://github.com/Azure/azure-functions-core-tools/issues/5657)
- [Templating command switch #5655](https://github.com/Azure/azure-functions-core-tools/issues/5655)
- [Stack metadata and first-party templates #5647](https://github.com/Azure/azure-functions-core-tools/issues/5647)
- [Template package classification #5632](https://github.com/Azure/azure-functions-core-tools/issues/5632)
- [Workload specification](https://github.com/Azure/azure-functions-core-tools/blob/vnext/proposed/workload-spec.md)
- [Package-layout specification](https://github.com/Azure/azure-functions-core-tools/blob/vnext/proposed/workload-package-layout.md)
- [Profile design](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/cli-profiles.md)
