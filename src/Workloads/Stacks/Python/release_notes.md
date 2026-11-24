# Azure.Functions.Cli.Workloads.Python

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated Python applications are unchanged.
- Reject Python 3.9 virtual environments when running a Functions app. (#5959)
- Add a workload template root with placeholder scaffolding for future Python templates. (#5960)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding and options unchanged. (#5707)
- Package through the shared workload SDK with a generated manifest instead of a checked-in manifest. (#5523)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
