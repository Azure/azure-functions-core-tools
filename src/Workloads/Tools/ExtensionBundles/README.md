# Azure Functions CLI – Extension Bundles workload

This package ships the Azure Functions extension bundle payload for the
`func` CLI. It is a **content workload** (Workload Spec §3, §5.3): the
package carries files only, with no entry-point assembly and no runtime
services. `func start` resolves the install directory by package id and
reads the bundle layout directly.

The bundle payload version is encoded in the workload version. The
workload pkg version layers a per-bundle iteration counter on top:

| Channel       | Workload pkg version       | host.json `extensionBundle.id`                                      |
|---------------|----------------------------|---------------------------------------------------------------------|
| stable        | `4.35.0.1`                 | `Microsoft.Azure.Functions.ExtensionBundle`                         |
| preview       | `4.35.0-preview.1`         | `Microsoft.Azure.Functions.ExtensionBundle.Preview`                 |
| experimental  | `4.35.0-experimental.1`    | `Microsoft.Azure.Functions.ExtensionBundle.Experimental`            |

The 4th segment (stable) and the `.N` suffix after the channel label
(preview / experimental) are the workload-pkg iteration: bump when
republishing the same bundle payload with a workload-only fix. `func`
always reports the 3-part bundle version (`4.35.0`) in user-facing logs.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@4.35.0.1
# or by alias
func workload install bundles@4.35.0.1
```

Multiple bundle versions coexist via `--force` (Workload Spec §4.6).

## Build-time channel selection

The CDN bundle id used at pack time is driven by `$(BundleChannel)` in
`Directory.Version.props`:

```bash
dotnet pack ... /p:BundleChannel=preview
dotnet pack ... /p:BundleChannel=experimental
```

## Layout

The package places the bundle payload under `tools/any/` (Workload Spec
§5.3). After install:

```
<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<version>/
  tools/any/                  ← extension bundle root (bin/, extensions.json, ...)
  workload.json
```

`tools/any/` is the canonical content-workload payload path and the
contract between this package and the CLI's bundle resolver.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Bundles workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/bundles-workload-spec.md)
