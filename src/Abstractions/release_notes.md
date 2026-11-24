# Azure Functions CLI Abstractions

## 5.0.0-preview.7

#### Changes

- **Compatibility:** Abstractions now targets `net11.0`. Workload authors must retarget their workloads to `net11.0` and rebuild against the updated contracts for the .NET 11-based Azure Functions CLI.
- Removed the `Microsoft.Extensions.DependencyInjection.Abstractions` package reference because it is now part of the `net11.0` shared framework.
- Added the `IProjectStack` contract for stack and language metadata. (#5707)
- Added `FunctionsProject.Language` virtual property so workloads can report the detected project language to the CLI (used by `func init` adopt mode). (#5300)
- Made the bundle, project, and worker result record hierarchies closed. (#5957)
