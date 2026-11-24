# Azure.Functions.Cli.Workloads.Go

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated Go applications are unchanged.
- Replace the Go template placeholder with workload-included Go project, HTTP-trigger, and timer-trigger templates; generated projects require Go 1.25 or later. (#5960, #5992)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding and options unchanged. (#5707)
- Package through the shared workload SDK with a generated manifest instead of a checked-in manifest. (#5523)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
