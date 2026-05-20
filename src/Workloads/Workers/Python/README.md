# Azure Functions CLI – Python worker workload

Content workload that ships the Python language worker payload consumed by the
Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Python
# or by alias
func workload install python-worker
```

## Status

Preview. Worker assets are sourced from the
`Microsoft.Azure.Functions.PythonWorker` NuGet package. The `content/` folder
is the placeholder where the worker payload is staged before packing.
