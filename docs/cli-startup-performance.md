# Measuring CLI startup

The local harness uses **BenchmarkDotNet 0.15.8** to measure `func --help` and
`func version` from a prepared process launch through exit. It records
observations, **not an approved baseline or CI budget**.
CI collects these observations without enforcing a latency threshold.

Use PowerShell 7.4 or newer and a native, single-file published v5 executable.
Build outside the measurement:

```powershell
dotnet restore src\Func\Func.csproj -r win-x64
dotnet publish src\Func\Func.csproj -c Release -r win-x64 --no-restore -p:CI=true -o out\startup-cli
dotnet restore eng\Performance\Startup\Performance.Startup.csproj
dotnet build eng\Performance\Startup\Performance.Startup.csproj -c Release --no-restore -p:CI=true
.\eng\scripts\measure-cli-startup.ps1 -ExecutablePath .\out\startup-cli\func.exe -OutputPath .\out\startup-results.json
```

On Linux/macOS, substitute the matching native RID, use the `func` executable,
and use your shell's path separators. The self-contained executable carries its
runtime; the build SDK does not select a different runtime when measuring it.
Keep the published artifact and its build provenance. The report records its
SHA-256, product metadata (when available), CLI version, host OS/architecture,
PowerShell version, protocol and raw samples. The repository-selected .NET SDK
is also required to build and run the BenchmarkDotNet host; it does not replace
the self-contained target CLI's runtime.

Defaults are three series of five launches per command. Override `-Series`,
`-SamplesPerSeries` or `-TimeoutSeconds` for experiments. These are exploratory
counts, not statistically qualified policy. Every launch is a fresh process.
Within each command/series, the first is labeled `first-in-series`; the remainder
are `subsequent`. Each command/series starts with separate, known state and a
fresh BenchmarkDotNet host. The **Monitoring** strategy uses one invocation per
iteration, five iterations by default, one host launch per command/series, and
no warmup iterations. BenchmarkDotNet orders its parameter cases; command order
no longer alternates between series. An untimed version preflight occurs first and is
recorded; it can warm OS caches. Neither category guarantees cold machine caches,
which are not cleared.

The harness owns temporary CLI/workload/user/cache directories, uses an isolated
working directory without a Functions project, and clears unrelated inherited
environment settings and credentials from the child. No installed workloads are
present. A fresh `.version-check` containing `0.0.0` controls the existing updater
cache path without triggering a newer-version notice. Thus this fixture is not a
literally empty directory. Telemetry is opted out. These controls do **not** prove
packet-level network silence. Live network and telemetry-on experiments remain
separate work.

Output is UTF-8, redirected, and color disabled; stdin is closed. This measures a
noninteractive launch, not terminal rendering. The harness drains both output
streams concurrently, enforces a liveness timeout, and cleans up owned processes
and directories. A nonzero exit, timeout, unexpected output, or stderr invalidates
the run and produces a terminating error. A final executable-hash mismatch
invalidates every sample and leaves summaries empty, even if all measured
commands succeeded. JSON retains earlier samples and diagnostics. An existing
output file is never overwritten. `budgetStatus` is
always `not-evaluated`; successful measurement does not mean a performance pass.

Run the focused harness checks, including actual CLI measurements:

```powershell
dotnet restore test\Performance\Fixtures\Performance.Startup.Fixture.csproj
dotnet publish test\Performance\Fixtures\Performance.Startup.Fixture.csproj -c Release --no-restore -p:CI=true -o out\startup-fixture
.\test\Performance\MeasureCliStartup.Tests.ps1 -ExecutablePath .\out\startup-cli\func.exe `
    -FixtureExecutablePath .\out\startup-fixture\Azure.Functions.Cli.Performance.Startup.Fixture.exe
```

The tests use real child processes and synthetic summary inputs; they do not
assert a latency threshold on a shared machine. Working-directory isolation is
checked using an owned marker file through direct and linked paths, with a
wrong-directory negative control, rather than comparing platform-dependent path
spellings. Environment fields are checked separately for useful diagnostics.
Windows validation does not
qualify Linux, macOS or ARM64.
The synthetic CLI fixture intentionally passes version preflight, then forces
nonzero exit, unexpected stderr, wrong output and timeout inside generated
BenchmarkDotNet hosts. Tests require failed reports, retained diagnostics and
removed owned state; framework failure cannot be accepted as a performance pass.
The changed-artifact regression runs healthy measurements using an owned CLI
copy, then changes that copy at final hash verification through a test-scoped
hash-command override. It requires invalid samples, empty summaries, retained
diagnostics and removed temporary state without changing the original CLI.
On Unix, omit `.exe` from the fixture executable name.

## Reporting-only CI

[CLI startup measurements](../.github/workflows/cli-startup.yml) follows the
repository's GitHub Actions installer-test pattern. It runs on pull requests
targeting `vnext` and pushes to `vnext` when CLI sources, build inputs, the harness,
its tests, or the workflow change. It also supports manual dispatch. Documentation
changes alone do not start a measurement. Branch protection is not configured by
this workflow.

Each Windows 2022, Ubuntu 24.04, and macOS 15 hosted runner selects the SDK through
`global.json`, derives a RID from its native process/OS architecture, restores
and publishes the CLI in Release with CI warning policy, runs the harness tests,
then collects the default 30 samples. Test execution warms the machine further;
the categories describe state within each series, not first launch on a clean
runner. Builds, tests and measurements run sequentially within each job.

The benchmark host prepares the process in `IterationSetup`, measures
`StartupProcess.StartAndWait`, then captures/qualifies output and disposes the
process in `IterationCleanup`. Setup, validation, fixture deletion, framework
host startup and generated-project builds are not part of the measured workload.
The workload includes small method/guard/Stopwatch overhead around process
start-to-exit. `elapsedMs` comes from BenchmarkDotNet's actual workload iteration;
`innerElapsedMs` records the nested process timer for boundary diagnostics.
Each framework iteration must match exactly one qualified child observation and
one operation, with the expected command/series/index. Missing, duplicate,
invalid or incomplete framework results fail the run.

The JSON report's schema and first/subsequent summary remain compatible. Adjacent
`<OutputPath>.benchmarkdotnet/` artifacts retain the request, child observations,
framework logs, JSON/CSV/HTML/Markdown exports and actual iteration mapping.
Existing report files and artifact directories are never overwritten. The
framework's default pooled mean table is supplementary: the Actions summary
continues to use category medians, not pooled means.

This changes measurement orchestration, caller-runtime conditions and ordering
from the earlier custom-script protocol. Historical custom-script CI baselines
remain evidence for that protocol, not automatically qualified BenchmarkDotNet
baselines. Collect and review native BenchmarkDotNet observations before
rebaselining or deciding any warning/failure threshold.

The workflow rejects architecture emulation within the PowerShell process. It
does not promise all six shipped RIDs: hosted runner architecture can differ by
repository/runner availability. The actual RID, runner label/image, selected SDK,
source revision and workflow run are recorded in `provenance.json`. The executable
hash and reported CLI version are in `measurements.json`. PR checkout revisions
may be GitHub's merge revision rather than the branch head; provenance records
the actual checked-out HEAD.

The `cli-startup-<runner>-<attempt>` artifact retains provenance, measurements and
build/test logs for 30 days. The Actions run's **Summary** page also displays a
table per platform with valid sample counts, median/mean/min/max milliseconds,
target RID, SDK, source revision, attempt, measurement status and cleanup status.
No download is needed for this overview. Failed reports are labeled as partial;
missing reports show an explicit no-results message. Harness test outcomes remain
in the **Validate startup harness** step logs. Raw data and executable identity
remain in the artifacts.

BenchmarkDotNet is configured not to change the machine's power plan. Its
normal benchmark-host preparation still differs from the earlier PowerShell
measurement loop. Neither framework statistics nor additional instrumentation
eliminates shared-runner load or OS-cache variability.

Summary generation and upload are attempted even after failure or cancellation;
early setup failures may leave no files, and hosted job termination can prevent
cleanup/upload. Restore, publish, functional tests and invalid measurements fail
the job; **slow successful measurements do not**. There is no automatic baseline
refresh, retry-until-green policy or approved budget in this change.

Native CI runs must be inspected after publication before claiming cross-platform
qualification. Fixed workload fixtures, statistical policy, reviewed baselines and
regression enforcement remain later steps.
