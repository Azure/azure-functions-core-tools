# Azure Functions CLI – Node.js worker workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

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

## Pack details

Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.NodeJsWorker` NuGet package and packed under
`tools/any/` in the resulting workload NuGet.
