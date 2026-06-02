# Azure Functions CLI – Go worker workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Version scheme

Mirrors the Node and Python worker workloads: a single `$(WorkerVersion)`
(three-part SemVer) drives `$(VersionPrefix)`. Go has no upstream worker
NuGet, so `$(WorkerVersion)` is maintained manually in
`Directory.Version.props` alongside any change to the static
`worker.config.json` payload.

Ships as a NuGet prerelease (`1.0.0-preview.1`) via `$(VersionSuffix)`
while the Go worker integration stabilises. `func workload install` won't
pick up prereleases without an explicit version pin or `--prerelease`.

## Pack details

Go has no upstream worker NuGet; the workload ships a static native
`worker.config.json` under `tools/any/` that points the host at a user-built Go
executable (`bin/app`).
