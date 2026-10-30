# Azure Functions CLI v5 Preview 4 Bug Bash

Start with one [end-to-end flow](#end-to-end-flows), then explore commands and
the [Preview 4 areas to watch](#preview-4-areas-to-pay-extra-attention-to).
Test the documented flows and explore beyond them too. This is a local,
two-hour bug bash, not a certification checklist.

## Quick Reference

- CLI docs: https://aka.ms/func-cli
- File a CLI v5 bug: https://github.com/Azure/azure-functions-core-tools/issues/new?template=04-v5-bug-report.yml
- Bug-bash version: `5.0.0-preview.4`

## Required bug-bash environment

**Set these variables in every terminal used for the bug bash.**
Do this before setup, workload, or project commands.

### Bash / zsh

```bash
export FUNC_CLI_WORKLOADS_SOURCE="https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
export FUNC_CLI_QUICKSTART_MANIFEST_URL="https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json"
export FUNC_CLI_WORKLOADS_PRERELEASE="true"
```

### PowerShell

```powershell
$env:FUNC_CLI_WORKLOADS_SOURCE = "https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
$env:FUNC_CLI_QUICKSTART_MANIFEST_URL = "https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json"
$env:FUNC_CLI_WORKLOADS_PRERELEASE = "true"
```

These variables apply to the current shell/session. Set them again after opening
a new terminal. The Preview 4 installers do **not** set them; configure them manually.

## Prerequisites

Install only the tools for the stack you plan to test:

| Stack | Tools |
| --- | --- |
| Node.js | A supported Node.js LTS release and npm. |
| Python | A Python 3 version supported by your worker, with `venv` and pip. |
| .NET isolated | .NET 10 SDK. |
| Go, exploratory | Go 1.24+ and the assigned Flex-compatible worker/packages. |

Use PowerShell 7+ on Windows, or Bash on Linux/macOS, for installation. You do
not need the .NET SDK for the Node.js, Python, or Go flows.

- Use a new, disposable test directory and the isolated state paths below.
  Keep logs outside folders you may force-clean.
- Preserve your existing v4 install. Keep tests local; do not deploy to Azure.
- Do not use customer data or secrets. Do not change authentication to unblock a test.
- Verify the version before testing. Never substitute another CLI build or feed.
- If a required build, package, runtime, or fixture is unavailable, skip that
  subcase and tell the coordinator what is missing. Continue unrelated tests.

## Install Preview 4

Choose an unused directory; change the example name if it already exists.
These commands use the official Preview 4-tagged installer and pin the CLI version.
They skip PATH/profile changes. The session-only `func5` name below calls the
test binary without replacing your normal `func` command.

**PowerShell / Windows**

```powershell
$BugBash = Join-Path $HOME 'func-bugbash-preview4'
$InstallPath = Join-Path $BugBash 'install'
$env:FUNC_CLI_HOME = Join-Path $BugBash 'cli-home'
$env:FUNC_CLI_WORKLOADS_HOME = Join-Path $BugBash 'workloads-home'
New-Item -ItemType Directory -Path $BugBash -ErrorAction Stop | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/Azure/azure-functions-core-tools/v5.0.0-preview.4/install.ps1' -OutFile (Join-Path $BugBash 'install.ps1') -ErrorAction Stop
& (Join-Path $BugBash 'install.ps1') -Version 5.0.0-preview.4 -InstallPath $InstallPath -SkipPath
if ($LASTEXITCODE -ne 0) { throw 'Preview 4 installation failed.' }
& (Join-Path $InstallPath 'func.exe') --version
Set-Alias func5 (Join-Path $InstallPath 'func.exe')
func5 --verbose
Set-Location $BugBash
```

**Bash / Linux or macOS**

```bash
BUGBASH_ROOT="$HOME/func-bugbash-preview4"
INSTALL_DIR="$BUGBASH_ROOT/install"
export FUNC_CLI_HOME="$BUGBASH_ROOT/cli-home"
export FUNC_CLI_WORKLOADS_HOME="$BUGBASH_ROOT/workloads-home"
mkdir -- "$BUGBASH_ROOT" || exit 1
curl -fsSL 'https://raw.githubusercontent.com/Azure/azure-functions-core-tools/v5.0.0-preview.4/install.sh' -o "$BUGBASH_ROOT/install.sh" || exit 1
bash "$BUGBASH_ROOT/install.sh" --version 5.0.0-preview.4 --install-path "$INSTALL_DIR" --skip-path || exit 1
"$INSTALL_DIR/func" --version
func5() { "$INSTALL_DIR/func" "$@"; }
func5 --verbose
cd -- "$BUGBASH_ROOT"
```

**Stop if the installed version is not exactly `5.0.0-preview.4`.**
Keep this terminal for testing. `func --version` / `func --verbose` are equivalent
checks only when `func` points to this install; your normal `func` may remain v4.

Installer targets are Windows x64 / ARM64, Linux glibc x64 / ARM64, and macOS
Intel / Apple silicon. Skip a platform whose required Preview 4 build is unavailable.
Do not treat Alpine/musl or every platform/stack combination as supported.

## End-to-end flows

Use a fresh project directory per flow, under your bug-bash root. Setup installs
the selected stack and its dependencies. If anything needed is unavailable,
skip only that flow; do not substitute another CLI version.

For Node.js, Python, and .NET, choose the HTTP trigger when `new` prompts and use
anonymous authentication for this local test. You can inspect choices first with
`func5 new "PROJECT_DIRECTORY" --list`. From a second terminal, invoke the URL
printed by the host, then return to the first terminal and press Ctrl+C.
Use `curl.exe` instead of `curl` on Windows.

### Node.js

```text
func5 setup --features node --non-interactive
func5 init node-app --stack node --language JavaScript
func5 new node-app --name hello
func5 run node-app --output plain
```

Invoke: `curl http://localhost:7071/api/hello`

Check the JavaScript layout and npm dependencies. Repeat in a sibling directory
with `--language TypeScript`; check compilation, invocation, and clean shutdown.

### Python

```text
func5 setup --features python --non-interactive
func5 init python-app --stack python
func5 new python-app --name hello
func5 run python-app --output plain
```

Invoke: `curl http://localhost:7071/api/hello`

Start without an unrelated virtual environment active. Run creates/reuses the
project's `.venv` and installs `requirements.txt`; check dependency errors and
virtual-environment reuse. Python is unavailable on Windows ARM64 and is rejected
by Windows target profiles; neither restriction excludes local Windows x64 testing.

### .NET isolated

```text
func5 setup --features dotnet --non-interactive
func5 init dotnet-app --stack dotnet --language "C#"
func5 new dotnet-app --name Hello
func5 run dotnet-app --output plain
```

Invoke: `curl http://localhost:7071/api/Hello`

The default project targets `net10.0`. Check restore/build, the isolated-worker
project, and local invocation.
F# init is a separate variation; the .NET quickstart provider supports C# only.
Do not test .NET in-process setup as a supported flow.

### Go

**Exploratory, Flex only.** Use the assigned compatible Go worker/packages.
Skip this flow if they are unavailable.

```text
func5 setup --features go --profile flex --non-interactive
func5 init go-app --stack go
func5 profile set flex go-app
func5 run go-app --profile flex --output plain
```

Go init already creates an anonymous HTTP `hello` function in `main.go`; it does
not need a `func new` step. Do not assume a Go new/quickstart template provider.
Run uses Go 1.24+, runs `go mod tidy`, and builds your app before launching it.
Invoke `curl http://localhost:7071/api/hello`, then stop with Ctrl+C.
Check module/build errors, your response, and that the child app exits too.

## Commands to exercise

Choose commands relevant to your stack; you do not need to finish every example.
For each command, try no arguments, `--help`, invalid input, Ctrl+C during work,
and redirected input/output where applicable. Look for discoverable help,
actionable errors, clean cancellation, and no hangs or stack traces.
Replace quoted `UPPER_CASE` placeholders with values from output or your assigned fixture.

### Top-level

```text
func5
func5 help
func5 help workload
func5 version
func5 --version
func5 --verbose
```

Try an unknown command too. Version output must identify Preview 4, not an older
install found elsewhere on PATH.

### Setup

```text
func5 setup
func5 setup --features node --non-interactive
func5 setup --yes --non-interactive
func5 setup --features node --non-interactive --check --output json
```

- Use `node`, `python`, `dotnet`, or `go` explicitly for a stack setup. Try
  comma-separated features and repeated `--profile` values if assigned.
- `--yes` skips the picker; it does not install every stack. Without features or
  a configured runtime, noninteractive setup defaults to host plus extension bundle.
- Check output is NDJSON: one JSON event per line, not one final JSON document.
  Do not mix in completion/advisory text or mark setup complete in check mode.
  Metadata caches may change; early parser/startup errors are not guaranteed
  to use the setup JSON format.
- Try cancellation and a missing dependency. Optional skips must be visible;
  a successful setup is not a substitute for running your app.

### Profiles

```text
func5 profile list
func5 profile show flex
func5 profile set flex "PROJECT_DIRECTORY"
```

Set profiles only on disposable projects. Try an unknown profile and confirm
existing configuration survives an error. Python with `--profile windows-consumption`
and Go with a non-Flex profile must reject before downloading a worker.
These are Azure target-profile restrictions, not the developer machine's OS.

### Workloads

```text
func5 workload search node --prerelease --json
func5 workload install node --version 1.0.0-preview.2 --prerelease
func5 workload list --all-versions --json
func5 workload update node --prerelease
func5 workload prune node
func5 workload uninstall node --all-versions
```

- Try aliases `node`, `python`, `dotnet`, and `go`; repeat an install, then try
  `--force`. Try an unknown package and uninstalling a package that is not installed.
- Use separate seeded states for multi-version update, prune, and uninstall checks.
  Check inventory before/after: unrelated packages and ownership must be preserved.
- Workload update stays within the installed major version by default. Its
  `--version` selects the **installed version to replace**, not the desired target.
  Prune keeps the highest installed version; it is not profile-aware retention.
- `--exact` requires the full package ID, not an alias. Pass an assigned local
  `.nupkg` as the positional install argument, not as `--source`.
- `--source` overrides the environment variable. Use the required feed, not
  nuget.org or a repository restore setting.

If assigned a Host acquisition check, use its independent version, not the CLI version:

```text
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version 4.1048.200-preview.3 --prerelease
```

Check inventory and launch a prepared app afterward; Host alone is not a full stack setup.

### Update

Preview 4 includes `func update`. Run one case at a time in a disposable install;
verify the result and restore Preview 4 before returning to the other tests.

| Command | What to try |
| --- | --- |
| `func5 update` | Latest stable selection; a stable release may be unavailable. Errors should explain what to do. |
| `func5 update --prerelease` | Include preview releases. |
| `func5 update --version 5.0.0-preview.4` | Exact target selection; an already-current install is a successful no-op. |
| `func5 update --version 5.0.0-preview.4 --yes` | Skip confirmation, not installation checks. |

- **Updates are metadata-gated.** Script installs write `func.installation.json` with
  `schemaVersion: 1` and `updatePolicy: "in-place"`, permitting in-place update.
- An externally managed install with policy `"external"` should exit successfully
  with installer/package-manager guidance, without resolving an update or changing files.
- Supplied missing, malformed, oversized, or unsupported metadata fixtures should fail
  with a nonzero exit and reinstall guidance, without resolving an update or changing files.
  Never edit a working install's metadata to authorize updates.
- Test accept, decline, and Ctrl+C. Interactive confirmation defaults to Yes;
  unavailable input must safely decline, not hang. Update must not prompt for stack setup.
- Exact `--version` can select a preview without `--prerelease`, or an intentional
  older target. Unpinned selection must not downgrade. Use only an assigned target.

A same-version no-op does not test replacement. Skip replacement subcases without
the required target/fixtures. Published download, replacement, and recovery are
part of this bug bash, not something already guaranteed to work.

### Init / New / Run

```text
func5 init --help
func5 new --help
func5 run --help
```

Use the [stack flows](#end-to-end-flows) below. `start` is an alias for `run`.
Try both an explicit project directory and running from inside it, including
relative/absolute paths and paths with spaces. Pass a directory, not `host.json` itself.
An uninitialized project should produce useful guidance.

Try `new --list`, then use a listed template ID and its `--help` for required
options. For automation, supply `--template`, `--name`, any required template
options, and `--non-interactive`; missing input must fail rather than prompt.

### Quickstart

Use a stack you have set up; these filter examples use Python. Keep the dev
manifest variable from [environment setup](#required-bug-bash-environment).
Choose an HTTP template whose local prerequisites you have.

```text
func5 quickstart list
func5 quickstart list --stack python
func5 quickstart list --stack python --resource http --iac bicep --search http
func5 quickstart info "TEMPLATE_ID"
func5 quickstart quickstart-interactive
func5 quickstart quickstart-app --stack python --template "TEMPLATE_ID"
func5 run "FUNCTION_APP_DIRECTORY" --output plain
```

Replace `TEMPLATE_ID` with a listed ID. Follow the generated README to locate
`FUNCTION_APP_DIRECTORY`, the folder containing `host.json`, and prepare local
dependencies. Invoke the printed HTTP endpoint and stop with Ctrl+C.
Do not run cloud deployment steps.

Try an unknown ID, combined filters, no installed provider, and a populated
destination. An explicit template skips template selection, not every possible
stack/language choice. Supply `--stack` / `--language` when needed.
Try `--fetch http` and `--fetch git` in separate empty directories; the default
is `auto`, not forced Git. Git mode needs Git installed.

## Cross-cutting things to test

| Check | Look for |
| --- | --- |
| Ctrl+C | Prompt cancellation, downloads, setup, and local runs stop cleanly; no leaked test processes or stack traces. |
| `NO_COLOR` | Readable output without color/ANSI escapes. |
| Redirected/non-TTY use | No key-reader errors, accidental prompt loops, or hangs; missing required input gets useful guidance. |
| Help and errors | Options are discoverable; errors say what to do next. |
| Telemetry opt-out | `FUNC_CLI_TELEMETRY_OPTOUT=1` remains supported; ordinary command output should not change. The legacy `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT=1` is also supported. |

**Bash / zsh:** `NO_COLOR=1 func5 help | cat`

**PowerShell:** `$env:NO_COLOR = '1'; func5 help | Out-String`

For a prepared project, try `func5 run "PROJECT_DIRECTORY" --output compact`,
`--output plain`, and `--output json`. The default is auto-detected.
JSON mode should emit one JSON event per line. Redirect stdout and stderr separately;
on Bash, try `func5 setup --features node --non-interactive </dev/null >setup.log 2>setup.err`.
Check prompt behavior with stdin redirected too, not only stdout.

## Preview 4 areas to pay extra attention to

- **Install/upgrade:** repeat the pinned installer with `-Force` / `--force` on
  a separate earlier-v5 test install (Preview 3 is a useful baseline). Check the
  new version and preserved workload state. Force permits replacement; it does not select a version.
- **Wrappers/coexistence:** the session alias above does not test generated wrappers.
  Invoke those by full path; check v4 routing separately. On Unix, expect `0755`
  after clean install, forced upgrade, `umask 077`, missing-wrapper recreation,
  and replacement of an old `0700` wrapper. Check argument forwarding too.
- **Setup:** test explicit stacks, empty/configured projects, multiple profiles,
  noninteractive input, NDJSON checks, optional skips, cancellation, and partial failures.
- **Workloads:** compare inventories across lifecycle operations; preserve unrelated packages.
- **CLI update:** test [metadata gating](#update), confirmation, and an assigned real
  replacement, not only a no-op.
- **Profiles/bundles:** reject unsupported runtime/profile pairs before worker
  acquisition. With assigned incompatible-bundle fixtures, `new --list` stays
  available, but creation fails without writing generated files.
- **Init/new/quickstart:** try adoption of an existing project and repair of
  missing language configuration without losing other settings. Use sentinel
  files to test `--force` only in disposable init/quickstart destinations:
  interactive cleanup defaults to No; decline/Ctrl+C preserves files.
  With input unavailable, explicit `--force` authorizes cleanup.
- **Terminal/recovery:** vary stdin and stdout independently and cancel the dashboard.
  Use only supplied disposable installer-failure fixtures; failures should preserve
  the prior install or clearly report a retained recovery location, not false success.

## Known issues and limitations

- This preview is not production-ready or at v4 feature parity. Test locally only.
- Each platform scenario requires its Preview 4 build; skip it if unavailable.
- npm testing is unavailable unless the exact Preview 4 package is supplied.
  Do not assume it provides a `func5` coexistence command.
- Java and PowerShell are not complete supported customer journeys; limit them to
  assigned, manually provisioned exploratory tests.
- Go is exploratory and Flex only; it needs Go 1.24+ and compatible worker/packages.
- Setup can skip optional dependencies. General prerelease selection does not
  opt into every preview bundle/template channel. Record what was actually selected.
- Update replacement/recovery is under test, not a proven guarantee.

## Reporting bugs

[File a CLI v5 bug](https://github.com/Azure/azure-functions-core-tools/issues/new?template=04-v5-bug-report.yml)
for bugs, confusing behavior, bad errors, papercuts, missing documentation,
naming issues, or unexpected behavior.

Include:

- `func5 --verbose` output (or `func --verbose` for a verified v5 command).
- Exact command, OS / architecture / shell, and stack/runtime version where relevant.
- Expected result, actual result, exit code, and a minimal repro.
- Relevant logs/stack trace; workload versions, profile, and template ID when involved.

Redact secrets and customer information. If you're unsure whether something is a
bug, file it anyway. Duplicates are easier to close than missing feedback.
Tell the coordinator about unavailable prerequisites. When comparing previews,
say whether the behavior is new.

## Cleanup

1. Stop only your test processes with Ctrl+C. Save issue links and useful logs first.
2. In the same isolated environment, use `func5 workload list` and
   `func5 workload uninstall "WORKLOAD_ID" --all-versions` to exercise removal if desired.
3. Close the test terminal to discard its variables and `func5` alias/function.
   The examples did not change PATH or shell profiles.
4. Remove only the test root you created, after checking its contents. Do not delete
   your normal `.azure-functions` directory, shared NuGet caches, v4, or unrelated projects.
   If you deliberately tested PATH/profile changes, undo only those exact test entries.

## Troubleshooting

### Workload/package resolves incorrectly or cannot be found

If an alias resolves to the wrong package or reports a missing `FuncCliWorkload`
package type, or a package cannot be found, check both workload variables:

```bash
export FUNC_CLI_WORKLOADS_SOURCE="https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
export FUNC_CLI_WORKLOADS_PRERELEASE="true"
```

```powershell
$env:FUNC_CLI_WORKLOADS_SOURCE = "https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
$env:FUNC_CLI_WORKLOADS_PRERELEASE = "true"
```

Remove a conflicting `--source` override, or point it to this exact feed, and retry.

### Quickstart templates are missing

Set the dev manifest URL in the same terminal:

```bash
export FUNC_CLI_QUICKSTART_MANIFEST_URL="https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json"
```

```powershell
$env:FUNC_CLI_QUICKSTART_MANIFEST_URL = "https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json"
```

If cached results persist, stop the CLI and move only the `quickstart` cache
directory inside your test `FUNC_CLI_HOME` to a uniquely named backup.
For these examples that is `cli-home\quickstart` on Windows or `cli-home/quickstart`
on Linux/macOS, under your test root. Retry `func5 quickstart list`.
Do not reset workload state or delete the shared/default CLI home.
