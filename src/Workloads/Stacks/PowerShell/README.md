# Azure Functions CLI – PowerShell workload

This workload extends the Azure Functions CLI (`func`) with PowerShell project
support: `func init --stack powershell` scaffolding, language-specific options,
and (via the workload model) any future PowerShell-only commands.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.PowerShell
# or by alias
func workload install powershell
```

## Status

Preview. The public initializer scaffolds the profile and settings, supports
runtime versions `7.4` (default) and `7.6`, and optionally resolves the Az module
major version with `--managed-dependencies` (default false).

The workload also bundles a native `basic` project archive from the same owned
sources. It retains runtime and bundle options; the owned requirements source
is reserved for a trusted conditional create-file preparation effect.
The native project remains staged until owned managed-dependency preparation
is composed for public execution. No separate template feed or PowerShell
item catalog is introduced.

Controlled native-project fixtures compose the owned managed-dependency
preparation helper with the shared create-only effect workflow. They check
preview, exact expanded requirements, lookup diagnostics and source/occupancy
refusals. This is a test-owned bridge, not a registered native project-session
hook or a public `func init` switch. Additional fixtures supply the same
owned effect capability through the shared init-runner preparation callback,
including preview-only file plans and retained native configuration on late
requirements occupancy.

The opt-in CLI-owned provider now performs the same composition using a
source-linked helper and original requirements resource. Its immutable preview
freezes the resolved optional Az content and diagnostic; completion applies
that exact create-only plan after source and project configuration rechecks.
No physical-workload DI service transfer or public command switch is required.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
