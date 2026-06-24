# Azure Functions CLI – Extension Bundles workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Build-time channel selection

The CDN bundle id used at pack time is driven by `$(BundleChannel)` in
`Directory.Version.props`:

```bash
dotnet pack ... /p:BundleChannel=preview
dotnet pack ... /p:BundleChannel=experimental
```

## Layout

The package places the bundle payload under `tools/any/<BundleVersion>/`
so the on-disk layout matches what the host's `ExtensionBundleManager`
expects to probe (`<downloadPath>/<version>/...`). After install:

```
<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<version>/
  tools/any/
    <BundleVersion>/         ← extension bundle root (bin/, extensions.json, ...)
  workload.json
```

The CLI bundle resolver sets `downloadPath` to `<install>/tools/any/`
and the host walks the `<BundleVersion>/` child for the requested version.
`tools/any/` is the canonical content-workload payload path.
