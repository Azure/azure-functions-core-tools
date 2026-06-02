# Azure Functions CLI – Extension Bundles workload

This package ships the Azure Functions extension bundle payload for the
`func` CLI. It is a content workload: the package carries files only,
with no entry-point assembly and no runtime services. `func start`
resolves the install directory by package id and reads the bundle
layout directly.

The bundle payload version is encoded directly in the workload pkg
version (1:1 mapping; this is a content-only workload, so there is no
separate workload code to version).

| Channel       | Workload pkg version   | host.json `extensionBundle.id`                                      |
|---------------|------------------------|---------------------------------------------------------------------|
| stable        | `4.35.0`               | `Microsoft.Azure.Functions.ExtensionBundle`                         |
| preview       | `4.35.0-preview`       | `Microsoft.Azure.Functions.ExtensionBundle.Preview`                 |
| experimental  | `4.35.0-experimental`  | `Microsoft.Azure.Functions.ExtensionBundle.Experimental`            |

The prerelease label, when present, names the channel. `func` always
reports the 3-part bundle version (`4.35.0`) in user-facing logs.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@4.35.0
# or by alias
func workload install bundles@4.35.0
```

Multiple bundle versions coexist via `--force`.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Bundles workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/bundles-workload-spec.md)
