# Azure Functions CLI – Go worker workload

Content workload that ships the Go worker assets (a static `worker.config.json`
plus any supporting files) consumed by the Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Go
# or by alias
func workload install go-worker
```

## Version scheme

Mirrors the Node and Python worker workloads: `$(WorkerVersion)` (three-part
SemVer) + `$(WorkerChannel)` (`stable` \| `preview` \| `experimental`) drive
`$(VersionPrefix)`. Go has no upstream worker NuGet, so `$(WorkerVersion)` is
maintained manually in `Directory.Version.props` alongside any change to the
static `worker.config.json` payload.

| Channel      | Workload pkg version |
|--------------|----------------------|
| stable       | `1.0.0`              |
| preview      | `1.0.0-preview`      |
| experimental | `1.0.0-experimental` |

```bash
dotnet pack ... /p:WorkerChannel=preview
```

## Status

Preview. Go has no upstream worker NuGet; the workload ships a static native
`worker.config.json` from `content/workers/native/` that points the host at a
user-built Go executable (`bin/app`). The host discovers the Go worker via
the `workers/native/` path, which matches the layout used by
`feature/go-support`.
