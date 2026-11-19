# Azure Functions CLI Abstractions 1.0.0

#### Changes

- **Compatibility:** Abstractions now targets `net11.0`. Workload authors must retarget their workloads to `net11.0` and rebuild against the updated contracts for the .NET 11-based Azure Functions CLI.
- Removed the `Microsoft.Extensions.DependencyInjection.Abstractions` package reference because it is now part of the `net11.0` shared framework.
- Added `FunctionsProject.Language` virtual property so workloads can report the detected project language to the CLI (used by `func init` adopt mode). (#5300)

