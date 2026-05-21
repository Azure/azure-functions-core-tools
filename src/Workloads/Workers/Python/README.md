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

Preview. Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.PythonWorker` NuGet package (its `tools/` payload)
and packed under `content/workers/python/` in the resulting workload NuGet.
