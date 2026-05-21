# Azure Functions CLI – Node.js worker workload

Content workload that ships the Node.js language worker payload consumed by the
Azure Functions CLI host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Workers.Node
# or by alias
func workload install node-worker
```

## Version scheme

The workload pkg version maps 1:1 to the Node.js worker payload it carries.
One prop in `Directory.Version.props` drives `$(VersionPrefix)`:

| Prop               | Meaning                                                                   |
|--------------------|---------------------------------------------------------------------------|
| `$(WorkerVersion)` | `Microsoft.Azure.Functions.NodeJsWorker` version. Bare three-part SemVer. |

Bump `$(WorkerVersion)` whenever the upstream worker package is bumped.
`$(WorkerVersion)` also pins the restored
`Microsoft.Azure.Functions.NodeJsWorker` pkg via `VersionOverride`, so the
two versions cannot drift.

Unlike the bundles workload, there is no channel axis: the upstream
NodeJsWorker NuGet doesn't ship preview/experimental SKUs.

## Status

Preview. Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.NodeJsWorker` NuGet package and packed under
`content/workers/node/` in the resulting workload NuGet.
