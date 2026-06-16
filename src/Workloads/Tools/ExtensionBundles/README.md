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

## Channel selection in `func setup` and `func init`

`func setup` installs the **stable** bundle by default. When a project's
`host.json` opts into a channel via `extensionBundle.id` (the `.Preview` or
`.Experimental` id above), setup installs the matching channel for both the
bundle and the script-stack templates (Node, Python). If that channel has
nothing published, setup falls back to the stable channel and prints a
warning rather than failing; the warning points at
`func workload search bundles --prerelease` so you can see what is available.

`func init -s <stack> -c preview|experimental` scaffolds a `host.json` pinned
to the chosen channel. If no bundle workload for that channel is installed, it
prints a hint to run `func workload search bundles --prerelease`.

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
