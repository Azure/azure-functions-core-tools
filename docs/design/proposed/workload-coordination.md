# Workload coordination and CLI/workload compatibility

**Status:** Proposed

**Issue:** [#5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)

**Scope:** Azure Functions CLI v5, the Workload SDK, workload packages, and release coordination

## Summary

Keep the CLI and workloads independently versioned. Each workload package
declares what it requires from the CLI and what optional behavior it provides.
For workloads loaded into the CLI process, the SDK generates a minimum CLI
version from the resolved Abstractions package version used to build the
workload. The CLI enforces that requirement; assembly-version differences are
only diagnostic warnings. Default install, update, and setup select the newest
compatible package version; an explicit version remains an exact request.
During prune, Abstractions warnings do not remove workloads. Runtime
diagnostics explain recognized failures during workload loading or activation.
No compatibility scan runs on every CLI invocation. This design is not related
to or controlled by profile behavior and is considered a separate feature.

| Question | Source of Truth |
| --- | --- |
| Can this CLI consume the workload? | Manifest schema, structural validation, the required minimum CLI version for CLI-loaded workloads, and supplemental Abstractions-version warnings |
| Can this workload perform a requested operation? | Capabilities declared by the selected workload implementation |
| What exact versions were tested or requested? | Release evidence and explicit version/profile pins |

Use static workload package metadata, not a CLI-shipped workload bill of materials or
a runtime negotiation service. A capability check applies to the operation that
needs it; an absent optional capability does not make the entire workload
unusable.

For implicit install, update, and setup selections, consider available package
versions from newest to oldest and choose the first compatible version. An
explicit version pin is exact and is never replaced with another version.

## Current state and motivation

The CLI and workloads are released independently. A newer workload can depend
on an API that an older CLI does not have. An older workload can also depend
on an API that a newer CLI no longer supports.

Workload packages have a `workload.json` manifest, but it does not currently
declare these compatibility requirements. The CLI checks the manifest schema
at installation, then loads installed workloads from the registry without
re-reading the manifest.

## Goals and non-goals

**Goals**

- Warn during install or update when the workload references a different
  Abstractions version; the warning alone does not block installation.
- Warn during prune about all installed workloads with a different
  Abstractions reference; do not remove them for that reason.
- Let workloads advertise optional behavior; when a command needs a capability,
  check only the individual workload it will use.
- Explain recognized assembly/type-loading failures during workload loading or
  activation; leave errors during workload command execution unchanged.
- Explain incompatibilities and keep workload inventory and repair available.
- Respect explicit version pins; for implicit selections, choose the newest
  compatible version and report incompatible newer candidates that were skipped.

**Non-goals**

- Updating workloads in the background or downgrading the CLI.
- Treating a metadata declaration as proof that the workload behaves correctly.

## Compatibility requirements

### Abstractions version and minimum CLI

Some workloads load into the CLI process and use its shared
`Azure.Functions.Cli.Abstractions` assembly. A version difference can be a
useful diagnostic, but assembly versions do not reliably prove compatibility:
prerelease details may be omitted, so multiple previews can have the same
assembly version despite different APIs. For every workload loaded into the CLI
process, the SDK generates `minCliVersion` from the full, resolved version of
the Abstractions package used to build it. The CLI rejects the workload if its
version is below that minimum. A missing or malformed minimum is not evidence
of compatibility and is rejected for a v2 CLI-loaded workload.

At install/update, inspect the workload DLL's assembly reference and report it
alongside the CLI's Abstractions version when they differ. This remains a
supplemental warning and does not replace the minimum-version check. Prune
inspects every installed workload and does not remove a workload for an
Abstractions-version warning.

Do not scan workloads on unrelated CLI invocations. When a command actually
loads a workload, make known assembly/type-loading failures actionable: name
the workload and versions when available, preserve the underlying exception
for verbose diagnostics, and suggest updating the CLI or workload. Scope this
handling to workload loading and activation. Errors raised later while running
the workload's command code retain their existing behavior.

Different assembly versions don't necessarily mean the workload will fail.
Warnings should show the versions found without claiming incompatibility.
`minCliVersion` is a CLI version, not an assembly version; NuGet prerelease
ordering applies, so `5.2.0-preview.1` is older than `5.2.0`. A workload that
needs additional CLI behavior beyond its Abstractions dependency may raise the
SDK-generated minimum.

### Package shape

The CLI continues to validate the existing package types and layouts.
Recognizing the manifest schema alone does not make an invalid package usable.
This design does not change package layouts.

## Package metadata

Introduce a v2 package-manifest schema. Safety-critical requirements must not
be added only as optional v1 properties: older CLIs can ignore unknown JSON
properties. The current preview CLI, which only understands v1 manifests,
rejects an unknown schema during installation, but may still encounter v2
packages already registered by a newer preview CLI. The preview downgrade
reset/reinstall procedure below covers that case. Before stable v2 workloads
are published, every stable CLI release that consumes workloads must understand
the schema.

For illustration, a CLI-loaded workload could declare:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v2/schema.json",
  "kind": "workload",
  "entryPoint": {
    "assemblyPath": "tools/any/Example.Workload.dll",
    "type": "Example.Workload"
  },
  "minCliVersion": "5.2.0"
}
```

A host workload could advertise a capability in its manifest. This excerpt
omits the other manifest fields:

```json
{
  "capabilities": [
    "host.bound-address-reporting"
  ]
}
```

The schema URL, version numbers, and capability name are proposed examples,
not metadata already present in published packages.

For v2, `minCliVersion` is a top-level property required for workloads loaded
into the CLI process. The SDK generates it from the resolved Abstractions
package version used to build the workload; authors may raise it for additional
CLI behavior requirements but must not lower it. Reject a missing or malformed
value for these workloads. The manifest does not declare an Abstractions
version; the CLI reads the assembly reference to produce supplemental install
and prune warnings.

Not every new manifest property requires a schema-version bump. An optional
field can be added compatibly when older CLIs may safely ignore it and the
schema allows that evolution. Use a new schema version when older CLIs must
understand a field to behave safely, or when the package structure or meaning
changes incompatibly.

`workload.json` is authoritative. NuGet tags may be discovery hints but do not
authorize installation. A package-level `minCliVersion` may be published as a
search prefilter so the CLI can skip downloading candidates that require a
newer CLI. The SDK must generate it from the same value as the manifest; the
manifest remains authoritative and every selected package is validated against
it. Do not use NuGet dependencies to install a second copy of an assembly the
CLI already supplies, such as Abstractions.

### Provided capabilities

`capabilities` lists optional behaviors the workload provides as stable,
lowercase identifiers. Each operation checks for the exact identifier it needs.
If a capability is missing, the CLI cannot rely on that behavior, even if the
executable happens to support it. Unknown provided capabilities can be ignored;
malformed or duplicate entries are rejected. Give an incompatible change in
behavior a new capability name.

Check capabilities on the selected workload implementation. The SDK writes
the workload author's declarations to the manifest and regenerates it when
they change. Workload owners test that each advertised capability works.

### Installed state

Keep enough package metadata for prune to inspect installed workloads,
including after a CLI upgrade. During preview, users switching to an older
CLI must clear the workload store and reinstall workloads before using it. A
differing Abstractions reference produces a warning only; it is not grounds
for deleting or changing an individual workload.

Inspect each meta-package member independently during installation and inspect
all installed members during prune. For a capability-gated command, check only
the individual workload being used. Do not let one owner's requirements block
a separately owned workload. If required metadata is missing or corrupt,
report how to repair the installation rather than assuming it has no
requirements.

### Preview upgrades and downgrades

Preview CLI versions may not agree on the workload manifest or registry
format. An older preview CLI can load entries written by a newer one or discard
registry fields it does not recognize. We will not build separate workload
storage for this preview transition.

When switching to an older preview CLI, users must first delete the CLI
workload store (including the path set by `FUNC_CLI_WORKLOADS_HOME`, if any),
then reinstall workloads for that CLI. This is a clean reinstall, not an
attempt to preserve preview workload state across CLI versions.

Every stable public CLI release that consumes workloads will support this
manifest schema before stable workloads using it are published. That release
requirement removes the need for separate storage to handle the preview
transition. Stable upgrades and downgrades must still be tested against the
shared registry format.

## Example: an OS-assigned host port

For #5881, a host can advertise `host.bound-address-reporting` when it reports
its actual bound address in the `host.listen_uri` field of a `HostListening`
event after startup.

When the user requests `--port 0`, the CLI checks the selected host for that
capability before launching it. If it is missing, the CLI explains that the
user can update the host, change an explicit version pin, or use a fixed port.
Fixed-port runs do not need this capability.

The CLI must still validate the address the host reports. If the event is
invalid or never arrives, it fails within a bounded time and cleans up the
host rather than reporting `:0` as a usable address. If `func start` installed
the host first, that installation may remain, but the command must report
that startup failed.

`FUNC_HOST_CONTENT_ROOT` can point to a local host build without a manifest.
Such a host does not advertise any capabilities unless it has associated,
validated metadata. Document how developers can use this feature with local
builds instead of inferring support from the executable path or version.

## Enforcement and selection

### Default version resolution

For install, update, or setup without an explicit version, enumerate available
versions from the configured package sources in descending NuGet version order,
across major versions. Validate candidates until the newest compatible version
is found. Compatibility includes valid manifest schema, package structure, and
the CLI's satisfaction of `minCliVersion`; capabilities are checked later by
the operation that needs them, and Abstractions-version warnings alone do not
make a candidate incompatible. Report incompatible newer candidates and why
they were skipped.

A package-level `minCliVersion` may prefilter candidates that require a newer
CLI. If the hint is absent, download and inspect the candidate manifest. Always
validate the selected package's manifest and structure; do not treat the
package-level hint as proof of compatibility.

If no candidate is compatible, fail with an actionable explanation, including
the newest candidate and its required CLI version when available. For update,
never downgrade an installed workload: update only to a compatible version
that is not lower than the installed version. If no such version exists, leave
the installation unchanged and report why.

An explicit version pin selects only that version. Reject it if incompatible;
do not fall back to another version. An update pin below the installed version
is rejected rather than downgrading. If the installed version is already the
newest compatible version, report that it is up to date. `--force` does not
bypass compatibility checks.

### Compatibility enforcement

1. During install/update and before loading a CLI-loaded workload, enforce its
   SDK-generated `minCliVersion`. Reject a missing or malformed minimum, or a
   CLI version below it.
2. During download/install or update, inspect the selected workload DLL's
   Abstractions reference. If it differs from the CLI's, warn and continue;
   this warning is supplemental to `minCliVersion`. Apply the same behavior to
   local and reused packages.
3. During prune, inspect every installed workload and warn about differing
   Abstractions references. Do not remove or change workloads because of these
   warnings.
4. When a command needs a capability, check that capability on the individual
   workload it will use before relying on the behavior.
5. When loading or activating a workload fails with a recognized assembly or
   type-loading error, report the workload identity and version information
   when available, with the original error in verbose diagnostics. Do not
   reclassify errors from later command execution as compatibility failures.

Continue to enforce declared minimum CLI requirements at the appropriate
install/use boundary. Abstractions version differences alone produce warnings,
not compatibility rejections. Inventory, uninstall, and update must remain
available even if a workload cannot be loaded.

Do not run a full compatibility scan at CLI startup or for unrelated commands.
Prune checks all installed workloads from metadata without loading their code.
A capability-gated command checks only the workload whose capability it needs.

## Migration and release coordination

### Existing v1 packages

Here, v1 and v2 refer to the **manifest schema**, not the CLI or workload
version. A v1 manifest does not declare the requirements or capabilities in
this design, so its compatibility is unknown.

During preview, retain the current behavior for v1 workloads and show that
their compatibility is unknown. Do not assume they provide new capabilities;
they can still fail at runtime. At the transition to stable releases, tell
preview users to delete the workload store and reinstall so they start with
packages using the stable manifest schema. Keep inventory and repair available
for workloads that cannot be loaded.

### Release order

Before publishing stable workloads that use v2 metadata, ensure every stable
public CLI release that consumes workloads understands the schema. If a
workload needs a newer Abstractions version, release a CLI that includes it
first. Workloads that do not need a new CLI can continue to release
independently.

For preview users switching to an older CLI, document the required workload
store reset and reinstall. Test stable upgrades and downgrades against the
shared registry format. Record the versions tested and link the relevant
workload release notes.

## Implementation outline

1. Agree on the v2 schema, Abstractions versioning policy, and the preview
   reset/reinstall guidance for existing workloads.
2. Resolve implicit install/update/setup requests to the newest compatible
   version, while keeping explicit pins exact and updates non-downgrading.
3. Add non-blocking Abstractions mismatch warnings during install/update and
   prune. Keep prune from removing workloads for these warnings.
4. Add actionable diagnostics for recognized load/activation errors, without
   changing errors raised during command execution.
5. Update the SDK to generate `minCliVersion` from the resolved Abstractions
   package version and write capabilities to the manifest.
6. Add the host capability check for `--port 0`, then verify stable CLI schema
   support before publishing stable v2 workloads.

## Acceptance criteria

| Scenario | Expected outcome |
| --- | --- |
| A downloaded, local, or reused workload references a different Abstractions version | Warn during install/update; independently enforce `minCliVersion` |
| A CLI-loaded v2 workload is built against a newer Abstractions package | The SDK records its full resolved version in `minCliVersion`; reject it when the CLI is older |
| A CLI-loaded v2 workload has no generated minimum or has a malformed one | Reject it rather than assuming it is compatible |
| Two CLI previews have the same Abstractions assembly version but different package versions | Use the generated full-SemVer `minCliVersion` check to reject the older CLI |
| Prune is called | Warn for every installed workload with a different Abstractions reference; do not remove it for that reason |
| A command needs a capability | Check that capability on only the individual workload the command will use |
| An unrelated command is called | Do not scan installed workloads for compatibility or load their code for that purpose |
| Workload loading or activation fails with a recognized assembly/type error | Name the workload and available version information, preserve the original error for verbose diagnostics, and suggest recovery |
| A workload command fails after loading | Preserve the existing error behavior; do not report it as an Abstractions compatibility failure |
| A user switches to an older preview CLI | Document clearing the workload store and reinstalling before use |
| A stable CLI consumes v2 workloads | Every stable public CLI release that consumes workloads supports the schema |
| Stable CLI versions share a workload store | Upgrade and downgrade paths preserve registry usability |
| A package has an invalid requirement or layout | Reject it |
| A prerelease CLI does not satisfy a stable minimum | Reject the package with guidance to upgrade |
| A package advertises an unknown capability | Ignore it; it does not satisfy a capability the CLI needs |
| A pinned or forced install violates a declared minimum CLI requirement | Reject it without changing the selection or existing installation |
| A newer candidate is incompatible but an older candidate is compatible | Select the newest compatible version and report incompatible newer candidates |
| The only compatible candidate is in an older major version | Select it when resolving an implicit version |
| No available candidate is compatible | Fail with the newest candidate's incompatibility reason when available |
| An update's newest compatible candidate is older than the installed version | Do not downgrade; leave the installation unchanged and explain why |
| An update is explicitly pinned below the installed version | Reject the downgrade |
| A local, reused, or meta-package member is selected | Apply the same version and compatibility rules to each package without changing unrelated ownership |
| A host lacks bound-address reporting | Allow fixed-port runs; reject `--port 0` before launch with guidance |
| A host advertises bound-address reporting | Report a valid nonzero address and fail if the event is missing or invalid |
| A local host or v1 manifest has no capability metadata | Do not assume it provides the capability |
| An installed v1 workload is used during preview | Show that compatibility is unknown and keep repair available |

Test the metadata and compatibility rules, package installation paths, and
stable upgrade/downgrade scenarios. Verify the preview reset/reinstall
instructions. For the host example, verify both a successful request to the
reported port and a fixed-port run with an older host.

## Open decisions

- How should Abstractions assembly versions represent prerelease builds so
  install and prune warnings remain useful?
- How will local host builds advertise capabilities, and how long should the CLI
  wait for a required host event?

## References

- [Coordination issue #5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)
- [OS-assigned port PR #5881](https://github.com/Azure/azure-functions-core-tools/pull/5881)
- [Public-feed/CLI mismatch #5624](https://github.com/Azure/azure-functions-core-tools/issues/5624)
- [Workload specification](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Package-layout specification](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-package-layout.md)
