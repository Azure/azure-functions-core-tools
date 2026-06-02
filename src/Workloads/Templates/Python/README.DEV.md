# Azure Functions CLI: Python templates workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Programming model scope

v1 (legacy) programming model templates are **not** shipped in the v5
Python templates workload (Templates Workload Spec §5.2 / §6.2 / §7.1).
Users still authoring against the v1 programming model should migrate
to v2 or stay on v4 tooling.

## Versioning

The workload pkg version is independent of the source bundle version
(Templates Workload Spec §4.4.1). The channel a workload was built
from is encoded in the pkg version's prerelease label, and that
channel maps 1:1 to an extension-bundle id (Templates Workload Spec
§4.4.2).

## Bundle compatibility (sources of truth)

The minimum compatible extension-bundle version is recorded in three
locations that all derive from the single `$(MinBundleVersionRange)`
MSBuild property, so they cannot drift:

- `tools/any/content/templates-workload.json`, CLI-owned sibling
  manifest. Authoritative. Generated at pack time by the
  `GenerateTemplatesWorkloadJson` target.
- Nuspec `minBundle:[4.0.0,)` tag, discoverable via
  `func workload search` / `list`.
- Nuspec `<description>` sentence, surfaced by `func workload list`.

## Build-time channel selection

The source bundle id used at pack time is driven by
`$(TemplatesChannel)` in `Directory.Version.props`:

```bash
dotnet pack ... /p:TemplatesChannel=preview
dotnet pack ... /p:TemplatesChannel=experimental
```

`$(SourceBundleVersion)` selects the bundle version whose
`StaticContent/v2` subtree is snapshotted at build time. It is
recorded as build provenance only; it does not derive the templates
pkg version (Templates Workload Spec §4.4.1).

## Layout

The package places the template payload under `tools/any/content/`,
matching the upstream extension bundle's `StaticContent/v2/` subtree
with the `StaticContent/` wrapper stripped (Templates Workload Spec
§5.2). Python ships only the v2 programming-model subtree:

```
<workload-home>/workloads/azure.functions.cli.workloads.templates.python/<version>/
  tools/any/content/
    templates-workload.json      ← min-bundle sibling manifest
    v2/                          ← v2 programming model
      bindings/userPrompts.json
      resources/Resources.json
      resources/Resources.<locale>.json
      templates/templates.json   ← NewTemplate[] (jobs/actions)
  workload.json
```

Template files are carried **inline** inside each `NewTemplate`
entry's `files: { <filename>: <contents> }` map; there are no separate
per-template file directories on disk (Templates Workload Spec §5.2).
v2 templates reference prompt ids defined in `v2/bindings/userPrompts.json`
via `paramId` references inside `jobs[].inputs[]`. `tools/any/` is the
canonical content-workload payload path.
