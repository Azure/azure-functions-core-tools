# Azure Functions CLI – Node.js worker workload

Content workload that ships the Node.js language worker payload consumed by the
Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Node
# or by alias
func workload install node-worker
```

## Status

Preview. Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.NodeJsWorker` NuGet package and packed under
`content/workers/node/` in the resulting workload NuGet.
