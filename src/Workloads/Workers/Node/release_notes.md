# Azure.Functions.Cli.Workloads.Workers.Node

## 3.13.0

- Adopt two-axis version scheme: `$(WorkerVersion)` mirrors
  `Microsoft.Azure.Functions.NodeJsWorker`, `$(WorkerChannel)` selects the
  prerelease label (`stable` | `preview` | `experimental`). Workload pkg
  version now tracks the worker payload version 1:1.

## 1.0.0-preview.1

- Initial scaffold of the Node worker content workload.
