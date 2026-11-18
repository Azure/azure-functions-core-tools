# Azure.Functions.Cli.Workloads.Java

## 1.0.0-preview.2

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated Java applications are unchanged.
- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding, project detection, and Maven build behavior unchanged. (#5707)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
