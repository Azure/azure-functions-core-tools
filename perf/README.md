# CLI command performance

Crank runs a BenchmarkDotNet harness that launches the **published,
runtime-specific, self-contained, single-file `func` executable** once per
operation. The initial command is `func version`, on Linux x64.

## Run locally

Install the SDK selected by the repository's `global.json`, PowerShell 7, and
Git. Run on an x64 machine matching the target OS. The runner restores the
pinned Crank tools and builds the CLI; no globally installed `func` is used.

```powershell
pwsh ./perf/run.ps1

# Quick end-to-end qualification, not a useful performance sample:
pwsh ./perf/run.ps1 -Job dry -OutputDirectory ./out/perf-smoke

# Select a command:
pwsh ./perf/run.ps1 -Command version -OutputDirectory ./out/perf-version
```

Use a fresh output directory per run. The default is `out/perf`. The runtime
defaults to the current OS (`win-x64` or `linux-x64`); pass `-Runtime` to set it
explicitly. Only `linux-x64` is part of the initial CI matrix. Use `-Port` if the default loopback agent
port 5010 is occupied. The agent runs only for the invocation and is terminated
in cleanup; do not expose it to other machines. Its temporary workspace is also
removed in cleanup. Crank's SDK installer requires a temporary path without
spaces; set `TEMP`/`TMP` accordingly if your system's temp directory has spaces.

## Measurement contract

Each operation includes process creation, startup, command execution, output
draining/validation, and process shutdown. Setup and unmeasured smoke execution
precede BenchmarkDotNet warmup. Each child has a ten-second timeout; failures,
invalid output, and missing timing results fail the run.

The CLI uses an isolated home and empty workload/project directories,
noninteractive redirected I/O, and disabled telemetry. Its update cache is
seeded with `0.0.0`, preventing an unrelated GitHub update check from dominating
the measurement. These are **fresh-process, warm-filesystem-cache** timings,
not cold-cache or first-run measurements. They do not represent installations
with workloads. Crank's in-process setting applies to the harness, not `func`.

`results/results.json` contains Crank's combined BenchmarkDotNet results.
The downloaded Markdown report shows mean, error, and standard deviation;
JSON timing values use BenchmarkDotNet's nanosecond units.
`results/provenance.json` records the commit, executable hash and size, RID,
SDK/tool versions, selected commands, and measurement settings. Agent, build,
benchmark, and controller logs are retained for diagnosis.

Do not interpret harness allocation or process counters as CLI memory usage.
Shared CI machines introduce noise: the pipeline collects observations and
does not enforce regression thresholds or compare against a historical baseline.

## Add a command

Add one `CommandCase` to `CommandCase.All` in `CommandCase.cs`, with a unique
name, an argument array, a stdout validation regex, and optionally an expected
exit code. The shared launcher and pipeline discover every case automatically;
`-Command` restricts a run to one case. Add fixture/setup support before adding
commands that need an initialized project or workloads: the initial environment
is intentionally empty. Long-running commands such as `start` require a
different measurement contract and should not be added as finite command cases.

The standalone build files and NuGet configuration in this folder let Crank
build the uploaded harness without importing product-only engineering targets.
The published CLI is staged as harness content, uploaded without rebuilding it,
and executed by its exact path. Linux executable permissions are restored in
unmeasured setup after upload.

## CI registration

Create an Azure DevOps pipeline pointing to `eng/ci/performance.yml` on `vnext`,
using the same repository and 1ES resource/pool permissions as `public-build.yml`.
Adding YAML alone does not register or enable a pipeline.

The pipeline runs nightly at 08:00 UTC on `vnext` and can be queued manually.
There are no push or PR triggers. It publishes `perf_linux_x64` with the
results and logs, including diagnostics on failure. A manual run measures the
selected source revision. No external result database is required.
