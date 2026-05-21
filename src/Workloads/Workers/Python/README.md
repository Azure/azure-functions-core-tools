# Azure Functions CLI – Python worker workload

Content workload that ships the Python language worker payload consumed by the
Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Python
# or by alias
func workload install python-worker
```

## Version scheme

The workload pkg version maps 1:1 to the Python worker payload it carries.
Two props in `Directory.Version.props` drive `$(VersionPrefix)`:

| Prop               | Meaning                                                                                |
|--------------------|----------------------------------------------------------------------------------------|
| `$(WorkerVersion)` | `Microsoft.Azure.Functions.PythonWorker` version. Bare three-part SemVer.              |
| `$(WorkerChannel)` | `stable` \| `preview` \| `experimental`. Selects the workload pkg's prerelease label.  |

| Channel      | Workload pkg version |
|--------------|----------------------|
| stable       | `4.43.0`             |
| preview      | `4.43.0-preview`     |
| experimental | `4.43.0-experimental`|

Bump `$(WorkerVersion)` whenever the upstream worker package is bumped.
`$(WorkerVersion)` also pins the restored
`Microsoft.Azure.Functions.PythonWorker` pkg via `VersionOverride`, so the
two versions cannot drift.

## Build-time channel selection

```bash
dotnet pack ... /p:WorkerChannel=preview
dotnet pack ... /p:WorkerChannel=experimental
```

## Status

Preview. Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.PythonWorker` NuGet package (its `tools/` payload)
and packed under `content/workers/python/` in the resulting workload NuGet.
