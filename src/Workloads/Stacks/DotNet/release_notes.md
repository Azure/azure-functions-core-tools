# Azure.Functions.Cli.Workloads.DotNet

## 1.0.0-preview.3

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated applications still default to `net10.0`.
- Support `func init --target-framework net11.0` for C# projects while retaining `net10.0` as the default. Reject .NET 11 F# initialization with the same guidance as v4.
