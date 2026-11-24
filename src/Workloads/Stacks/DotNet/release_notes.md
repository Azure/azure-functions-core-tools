# Azure.Functions.Cli.Workloads.DotNet

## 1.0.0-preview.4

- Retarget the stack workload to `net11.0` for the .NET 11-based Azure Functions CLI; generated applications still default to `net10.0`.
- Bundle .NET project and function templates in the stack workload. (#5936, #5938)
- Surface .NET build errors during `func run` and suppress successful output-path lookup messages for `--no-build`. (#5954, #5839)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.7 or later for the .NET 11 runtime and contracts.

## 1.0.0-preview.3

- Support `func init --target-framework net11.0` for C# projects while retaining `net10.0` as the default. Reject .NET 11 F# initialization with the same guidance as v4.
