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

Preview. The project initializer is currently a stub; full scaffolding lands
in a follow-up release.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
