# Azure Functions CLI – Python worker workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Version scheme

The workload pkg version maps 1:1 to the Python worker payload it carries.
One prop in `Directory.Version.props` drives `$(VersionPrefix)`:

| Prop               | Meaning                                                                   |
|--------------------|---------------------------------------------------------------------------|
| `$(WorkerVersion)` | `Microsoft.Azure.Functions.PythonWorker` version. Bare three-part SemVer. |

Bump `$(WorkerVersion)` whenever the upstream worker package is bumped.
`$(WorkerVersion)` also pins the restored
`Microsoft.Azure.Functions.PythonWorker` pkg via `VersionOverride`, so the
two versions cannot drift.

Unlike the bundles workload, there is no channel axis: the upstream
PythonWorker NuGet doesn't ship preview/experimental SKUs.

## Pack details

Worker assets are pulled at pack time from the restored
`Microsoft.Azure.Functions.PythonWorker` NuGet package (its `tools/` payload)
and packed under `tools/any/` in the resulting workload NuGet.
