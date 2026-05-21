# Azure.Functions.Cli.Workloads.Workers.Go

## 1.0.0-preview.1

- Adopt single-axis `$(WorkerVersion)` version scheme. Manually maintained
  (no upstream Go worker NuGet) and drives the workload pkg version.
  Matches the Node and Python worker workload version layout. Ships as a
  preview while the Go worker integration stabilises.
