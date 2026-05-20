# Azure Functions CLI – Go worker workload

Content workload that ships the Go worker assets (a static `worker.config.json`
plus any supporting files) consumed by the Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Go
# or by alias
func workload install go-worker
```

## Status

Preview. Go has no upstream worker NuGet; the workload ships a static native
`worker.config.json` from `content/workers/go/` that points the host at a
user-built Go executable (`bin/app`).
