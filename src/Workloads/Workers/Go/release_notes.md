# Azure.Functions.Cli.Workloads.Workers.Go

## 1.0.0

- Adopt two-axis version scheme: manually maintained `$(WorkerVersion)` (no
  upstream Go worker NuGet) plus `$(WorkerChannel)` (`stable` | `preview` |
  `experimental`) drive the workload pkg version. Matches the Node and
  Python worker workload version layout.

## 1.0.0-preview.1

- Initial scaffold of the Go worker content workload.
