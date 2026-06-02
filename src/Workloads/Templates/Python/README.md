# Azure Functions CLI: Python templates workload

This package ships function-scaffold templates for Python Azure
Functions projects (**v2 programming model only**), consumed by the
`func` CLI's `func new` command. It is a content workload: the package
carries template files only, with no entry-point assembly and no
runtime services. `func new` resolves the install directory by package
id and reads the template payload directly.

The channel a workload was built from is encoded in the pkg version's
prerelease label, and that channel maps 1:1 to an extension-bundle id:

| Channel       | Workload pkg version   | Bundle id                                                          |
|---------------|------------------------|--------------------------------------------------------------------|
| stable        | `1.0.0`                | `Microsoft.Azure.Functions.ExtensionBundle`                        |
| preview       | `1.0.0-preview`        | `Microsoft.Azure.Functions.ExtensionBundle.Preview`                |
| experimental  | `1.0.0-experimental`   | `Microsoft.Azure.Functions.ExtensionBundle.Experimental`           |

The bundle id is both the upstream extension bundle the workload's
templates were snapshotted from and the value `func new` expects in
the project's `host.json` `extensionBundle.id` at scaffold time.
Channel selection at `func new` is implicit, derived from the
project's `host.json` bundle id.

## Bundle compatibility

This workload declares a minimum compatible extension-bundle version
(`[4.0.0,)`). `func new` validates the project's resolved bundle
version against this constraint and surfaces incompatibilities as a
warning or error.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.Python@1.0.0
# or by alias
func workload install python-templates@1.0.0
```

Preview / experimental channels resolve via the matching prerelease
label:

```bash
func workload install python-templates@1.0.0-preview
func workload install python-templates@1.0.0-experimental
```

Multiple templates versions coexist via `--force`.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
