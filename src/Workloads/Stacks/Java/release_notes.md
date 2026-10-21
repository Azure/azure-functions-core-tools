# Azure.Functions.Cli.Workloads.Java

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding, project detection, and Maven build behavior unchanged. (#5707)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
