# Azure.Functions.Cli.Workloads.Workers.Python

## 4.43.0

- Adopt two-axis version scheme: `$(WorkerVersion)` mirrors
  `Microsoft.Azure.Functions.PythonWorker`, `$(WorkerChannel)` selects the
  prerelease label (`stable` | `preview` | `experimental`). Workload pkg
  version now tracks the worker payload version 1:1.

## 1.0.0-preview.1

- Initial scaffold of the Python worker content workload.
