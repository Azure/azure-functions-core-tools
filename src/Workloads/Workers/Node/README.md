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

Preview. Worker assets are sourced from the
`Microsoft.Azure.Functions.NodeJsWorker` NuGet package. The `content/` folder
is the placeholder where the worker payload is staged before packing.
