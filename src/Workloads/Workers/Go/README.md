# Azure Functions CLI – Go worker workload

Content workload that ships the Go worker assets (a static `worker.config.json`
plus any supporting files) consumed by the Azure Functions CLI host. The host
launches a user-built Go executable (`bin/app`) at runtime.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Go
# or by alias
func workload install go-worker
```

Ships as a NuGet prerelease while the Go worker integration stabilises.
`func workload install` won't pick it up without an explicit version pin or
`--prerelease`.

## Status

Preview.
