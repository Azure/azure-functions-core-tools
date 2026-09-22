# Azure Functions CLI - Host workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Pack details

Release packaging creates RID-specific, self-contained packages by passing `-p:PackAllRids=true -p:SelfContained=true`. Each self-contained payload is placed
under `tools/<rid>/`, and its package id is suffixed with that RID. For example, the Windows x64 package contains
`tools/win-x64/Azure.Functions.Cli.Workloads.Host.exe`.

## Local CLI/host iteration

For local CLI/host iteration, set `FUNC_HOST_CONTENT_ROOT` to a built host
content root that contains `Azure.Functions.Cli.Workloads.Host(.exe)` and
`workers/workers.txt`. The CLI then skips workload resolution/installation and
launches that local content root through the normal start pipeline.
