# Azure.Functions.Cli.Workloads.Node

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated JavaScript and TypeScript applications are unchanged.
- Bundle JavaScript and TypeScript project, HTTP trigger, and timer trigger templates. HTTP triggers support configurable authorization levels, and local projects default to Azurite storage. (#5995)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.2

- `func init` adopt mode now reports the project language (JavaScript or TypeScript) to the CLI so it gets written to `.func/config.json`.
- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding and options unchanged. (#5707)
- Package through the shared workload SDK with a generated manifest instead of a checked-in manifest. (#5523)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
