# Azure Functions CLI – Extension Bundles workload

This package ships the Azure Functions extension bundle payload for the
`func` CLI. It is a **content workload** (Workload Spec §3, §5.3): the
package carries files only, with no entry-point assembly and no runtime
services. `func start` resolves the install directory by package id and
reads the bundle layout directly.

The bundle version is the workload version: installing
`Azure.Functions.Cli.Workloads.ExtensionBundles@4.35.0` makes extension
bundle `4.35.0` available to projects.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@4.35.0
# or by alias
func workload install bundles@4.35.0
```

Multiple bundle versions coexist via `--force` (Workload Spec §4.6).

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
