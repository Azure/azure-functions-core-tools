# Azure.Functions.Cli.Workloads.PowerShell

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated PowerShell applications are unchanged.
- Replace the PowerShell template placeholder with workload-included PowerShell project, HTTP-trigger, and timer-trigger templates; managed dependencies remain opt-in. (#5960, #5994)
- Default PowerShell projects to runtime version 7.6; 7.4 reached end of life and is no longer accepted. (#5994)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding, managed-dependency options, and runtime-version selection unchanged. (#5707)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
