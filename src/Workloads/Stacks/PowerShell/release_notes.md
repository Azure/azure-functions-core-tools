# Azure.Functions.Cli.Workloads.PowerShell

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated PowerShell applications are unchanged.
- Add a workload template root with placeholder scaffolding for future PowerShell templates. (#5960)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding, managed-dependency options, and runtime-version selection unchanged. (#5707)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
