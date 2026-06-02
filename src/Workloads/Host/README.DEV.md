# Azure Functions CLI - Host workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Pack details

Release packaging should be performed with a RID-specific self-contained payload under `tools/any/` by passing
`-p:PackRidSpecificHostWorkload=true -r <rid> -p:SelfContained=true`, which also suffixes the package id with the RID.
The self-contained executable must be placed at `tools/any/Azure.Functions.Cli.Workloads.Host` on Unix-like platforms and
`tools/any/Azure.Functions.Cli.Workloads.Host.exe` on Windows.

## Local CLI/host iteration

For local CLI/host iteration, set `FUNC_HOST_CONTENT_ROOT` to a built host
content root that contains `Azure.Functions.Cli.Workloads.Host(.exe)` and
`workers/workers.txt`. The CLI then skips workload resolution/installation and
launches that local content root through the normal start pipeline.
