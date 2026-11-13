# Azure Functions CLI v5 Preview 5 Bug Bash

**Version:** `5.0.0-preview.5`  
**[File a CLI v5 bug](https://github.com/Azure/azure-functions-core-tools/issues/new?template=03-v5-bug-report.yml)** · [CLI documentation](https://aka.ms/func-cli)

**Start with one local test, then explore.** This is a bug bash, not a certification checklist. Report confusing behavior and missing prerequisites as well as failures. Do not deploy to Azure, use customer data, or change authentication to unblock a test.

## What can I test?

| Category | Areas to explore |
| --- | --- |
| Recommended | Installation and version checks, setup, profiles, workloads, CLI update, and local execution |
| Optional regression | Quickstart browsing and scaffolding with an available template and the required local tools and dependencies |
| Stack-specific | Node.js, Python, and .NET isolated, with the prerequisites below |
| Exploratory | Go (Flex only); assigned Java, PowerShell, and platform-specific tests |
| Assigned prerequisites | Installer recovery, publisher trust, specific workload versions, and incompatible-bundle checks |

Use the prerequisites and limits in the detailed sections below.

## 1. Get ready

Choose a language stack to test. Use a disposable directory and keep your existing v4 installation unchanged.

| Stack | Local prerequisite |
| --- | --- |
| Node.js | Supported Node.js LTS and npm |
| Python | Supported Python 3, `venv`, and pip |
| .NET isolated | .NET 10 SDK for the baseline scenario |
| Go (exploratory, Flex) | Go 1.24+ and assigned compatible worker/packages |

Use PowerShell 7+ on Windows, or Bash on Linux/macOS. You do not need the .NET SDK for Node.js, Python, or Go tests.

### Configure the test environment

Set these in **every terminal used for CLI commands**, including any second terminal. The installers do not configure them.

**PowerShell**

```powershell
$env:FUNC_CLI_WORKLOADS_SOURCE = 'https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json'
$env:FUNC_CLI_QUICKSTART_MANIFEST_URL = 'https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json'
$env:FUNC_CLI_WORKLOADS_PRERELEASE = 'true'
```

**Bash / zsh**

```bash
export FUNC_CLI_WORKLOADS_SOURCE='https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json'
export FUNC_CLI_QUICKSTART_MANIFEST_URL='https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json'
export FUNC_CLI_WORKLOADS_PRERELEASE='true'
```

### Install Preview 5 without replacing v4

Use an **unused** test root. The commands pin the version, isolate CLI/workload state, and avoid PATH/profile changes.

**Windows / PowerShell**

```powershell
$BugBash = Join-Path $HOME 'func-bugbash-preview5'
$InstallPath = Join-Path $BugBash 'install'
$InstallerScript = Join-Path $BugBash 'install.ps1'
$env:FUNC_CLI_HOME = Join-Path $BugBash 'cli-home'
$env:FUNC_CLI_WORKLOADS_HOME = Join-Path $BugBash 'workloads-home'
New-Item -ItemType Directory -Path $BugBash -ErrorAction Stop | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/Azure/azure-functions-core-tools/v5.0.0-preview.5/install.ps1' -OutFile $InstallerScript -ErrorAction Stop
pwsh -NoProfile -File $InstallerScript -Version 5.0.0-preview.5 -InstallPath $InstallPath -SkipPath
if ($LASTEXITCODE -ne 0) { throw 'Preview 5 installation failed.' }
$InstalledVersion = (& (Join-Path $InstallPath 'func.exe') --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $InstalledVersion -cne '5.0.0-preview.5') { throw "Unexpected CLI version: $InstalledVersion" }
Set-Alias func5 (Join-Path $InstallPath 'func.exe')
func5 --verbose
Set-Location $BugBash
```

**Linux / macOS (Bash)**

```bash
BUGBASH_ROOT="$HOME/func-bugbash-preview5"
INSTALL_DIR="$BUGBASH_ROOT/install"
export FUNC_CLI_HOME="$BUGBASH_ROOT/cli-home"
export FUNC_CLI_WORKLOADS_HOME="$BUGBASH_ROOT/workloads-home"
mkdir -- "$BUGBASH_ROOT" || { echo 'Choose a new, unused test directory.' >&2; exit 1; }
curl -fsSL 'https://raw.githubusercontent.com/Azure/azure-functions-core-tools/v5.0.0-preview.5/install.sh' -o "$BUGBASH_ROOT/install.sh" || exit 1
bash "$BUGBASH_ROOT/install.sh" --version 5.0.0-preview.5 --install-path "$INSTALL_DIR" --skip-path || exit 1
INSTALLED_VERSION=$("$INSTALL_DIR/func" --version) || exit 1
if [ "$INSTALLED_VERSION" != '5.0.0-preview.5' ]; then echo "Unexpected CLI version: $INSTALLED_VERSION" >&2; exit 1; fi
func5() { "$INSTALL_DIR/func" "$@"; }
func5 --verbose
cd -- "$BUGBASH_ROOT"
```

**Stop if the version is not exactly `5.0.0-preview.5`.** Keep this terminal open. Your normal `func` may still point to v4; use `func5` in the examples.

Preview 5 installers target Windows x64/ARM64, Linux glibc x64/ARM64, and macOS Intel/Apple silicon. If the build for your platform is unavailable, skip that test and tell the coordinator. Do not substitute another version. Alpine/musl is not covered.

## 2. Run one local end-to-end test

Start with one of these examples. Use a fresh project name for each test. Choose an **HTTP trigger** when prompted and anonymous authentication if offered. You can inspect templates with `func5 new "PROJECT_DIRECTORY" --list`.

> If function generation fails, report it. You can still test initialization and host startup, but an empty app has no HTTP endpoint. For an HTTP invocation test, try an available Quickstart or a prepared app with an HTTP function. Listing templates does not prove function generation works.

### Node.js — JavaScript

```text
func5 setup --features node --non-interactive
func5 init node-app --stack node --language JavaScript
func5 new node-app --name hello
func5 run node-app --format plain
```

If the host starts and the function was created, call the URL it prints (typically `http://localhost:7071/api/hello`). On Windows use `curl.exe`; elsewhere use `curl`. Stop the host with Ctrl+C. Check npm dependencies and generated files. **Optional:** repeat with `--language TypeScript`, checking compilation and shutdown.

### Python

```text
func5 setup --features python --non-interactive
func5 init python-app --stack python
func5 new python-app --name hello
func5 run python-app --format plain
```

Start without an unrelated virtual environment active. Check `.venv` creation/reuse and `requirements.txt` installation. Invoke the printed HTTP endpoint and stop with Ctrl+C. For Windows ARM64 or target-profile scenarios, use only supported prerequisites assigned by the coordinator.

### .NET isolated

```text
func5 setup --features dotnet --non-interactive
func5 init dotnet-app --stack dotnet --language "C#"
func5 new dotnet-app --name Hello
func5 run dotnet-app --format plain
```

The baseline scenario targets `net10.0`. Check restore/build and the isolated-worker project. Invoke the printed HTTP endpoint and stop with Ctrl+C. **Optional:** F# initialization or an assigned .NET 11 scenario with the required SDK. Do not treat .NET in-process setup as a supported flow.

### Go — exploratory, Flex only

Only use assigned compatible worker/packages. Go initialization already includes an HTTP `hello` function; no `func new` step is required.

```text
func5 setup --features go --profile flex --non-interactive
func5 init go-app --stack go
func5 profile set flex go-app
func5 run go-app --profile flex --format plain
```

Invoke the printed HTTP endpoint. Check `go mod tidy`, build errors, response, and clean child-process shutdown. Skip if prerequisites are unavailable.

**Second terminal:** Use it to call the endpoint. If running CLI commands there, repeat the environment setup and invoke the same isolated Preview 5 executable. Keep logs outside any directory you might force-clean.

## 3. Choose another area to explore

You do **not** need to complete every example. Try `--help`, invalid input, noninteractive input, and Ctrl+C where relevant. Report unclear errors, hangs, unexpected changes, and missing options.

### Setup and profiles

```text
func5
func5 help
func5 help workload
func5 --version
func5 --verbose
func5 setup
func5 setup --features node --non-interactive
func5 setup --yes --non-interactive
func5 setup --features node --non-interactive --check --format json
func5 profile list
func5 profile show flex
func5 profile set flex "PROJECT_DIRECTORY"
```

- `--yes` skips selection; it does not install every Stack. Test unknown features/profiles and missing dependencies.
- In JSON check mode, expect newline-delimited JSON events rather than one final JSON document. Startup/parser errors may differ.
- Test profile changes only on disposable projects. An unsupported Stack/profile combination should fail before worker acquisition.

### Workloads

```text
func5 workload search node --prerelease --json
func5 workload list --all-versions --json
func5 workload update node --prerelease
func5 workload prune node
```

**Optional, destructive within isolated workload state:** install, repeat, force, and uninstall an assigned workload. Record inventory before and after; unrelated packages must remain intact.

```text
func5 workload install node --version 1.0.0-preview.2 --prerelease
func5 workload uninstall node --all-versions
```

The pinned Node version is for an optional regression test. Skip it unless assigned and available. Likewise, use the Host package/version below only for an assigned acquisition test:

```text
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version 4.1048.200-preview.3 --prerelease
```

`--version` on workload update identifies the installed version to replace, not the target. By default update stays within the installed major version. Prune keeps the highest installed version; it is not profile-aware. `--exact` requires a full package ID. Use an assigned local `.nupkg` as the positional install argument, not `--source`. Do not silently fall back to nuget.org.

### CLI update — use a separate disposable install

```text
func5 update
func5 update --prerelease
func5 update --version 5.0.0-preview.5
func5 update --version 5.0.0-preview.5 --yes
```

Test one case at a time and restore Preview 5 before continuing. A same-version success is a no-op, **not** a replacement test. Exact version selection can choose an older or preview version; unpinned selection must not downgrade. Test accept, decline, cancellation, and missing metadata only with assigned fixtures. Never edit a working install's `func.installation.json` to authorize an update. Script-managed installs use `schemaVersion: 1` and `updatePolicy: "in-place"`; externally managed installs should provide package-manager guidance instead of self-updating. Report recovery paths and partial failures accurately.

### Init, New, Run, and Quickstart

```text
func5 init --help
func5 new --help
func5 run --help
func5 quickstart list
func5 quickstart list --stack python
func5 quickstart info "TEMPLATE_ID"
func5 quickstart quickstart-app --stack python --template "TEMPLATE_ID"
```

Use IDs returned by `quickstart list`. Follow the generated README to find the function app directory and prepare local dependencies; do not run cloud deployment steps. Test a missing ID, combined filters, and a populated disposable destination. Optional: `--fetch http` and `--fetch git` in separate empty directories (Git must be installed for Git mode).

`start` is an alias for `run`. Test paths with spaces and an explicit directory versus running inside it. Pass the project directory, not `host.json`. For `func new`, test `--list` and help even if generation is unavailable. Do not interpret listing as proof of generation.

**Optional automation test:** In a disposable project, replace `TEMPLATE_ID` with an available function template ID and supply any additional required template options.

```text
func5 new "PROJECT_DIRECTORY" --template "TEMPLATE_ID" --name hello --non-interactive
```

Missing required input must fail without prompting. If the required template is unavailable, skip this test and report the missing prerequisite.

### Terminal and output behavior

- **Ctrl+C:** prompts, downloads, local runs, and dashboard exit without leaked processes.
- **Non-TTY:** redirected stdin/stdout should not hang or accidentally prompt.
- **Output:** try `func5 run "PROJECT_DIRECTORY" --format compact`, `plain`, and `json`; JSON output should be one event per line.
- **Color:** try `NO_COLOR=1 func5 help | cat` (Bash), or set `$env:NO_COLOR = '1'` before `func5 help` (PowerShell).
- **Telemetry opt-out:** `FUNC_CLI_TELEMETRY_OPTOUT=1` and legacy `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT=1` should not change normal command output.

## 4. Preview 5 focus areas and limitations

If you have an assigned focus area, prioritize: installer upgrade/coexistence, workload publisher trust, setup and npm networking, Python platform support, .NET 11, Host/worker acquisition, CLI update, and terminal recovery. Ask the coordinator if a required Stack/platform combination is unclear.

Useful optional checks:

- **Installer upgrade:** force-upgrade an earlier v5 disposable install; check version, workload preservation, wrapper permissions and argument forwarding. On Unix, inspect `0755` permissions, restrictive `umask`, and replacement of old `0700` wrappers.
- **Profiles/bundles:** unsupported target-profile combinations should fail before worker acquisition. With assigned incompatible-bundle fixtures, listing may remain available while generation is rejected without writing files.
- **Init/Quickstart safety:** test adoption/healing and `--force` with sentinel files in disposable destinations only. Decline or cancellation must preserve files. Never test destructive behavior in an existing project.
- **Failure/recovery:** use only supplied installer-failure fixtures; report whether the previous installation survived or where recovery data was retained.

**Known limits:** Preview 5 is not production-ready or at v4 parity. The Templates redesign is not included in this preview. Java and PowerShell are exploratory unless assigned supported fixtures; Go is exploratory and Flex-only. npm testing requires the exact supplied package. General prerelease selection does not enable every preview bundle/template channel. Update replacement/recovery is under test, not a proven guarantee.

## 5. Report bugs

**[File a CLI v5 bug](https://github.com/Azure/azure-functions-core-tools/issues/new?template=03-v5-bug-report.yml)** for failures, confusing behavior, poor errors, missing documentation, or naming issues. If unsure, file it.

Include:

1. `func5 --verbose` output and exact command.
2. OS, architecture, shell, Stack, and relevant Host/runtime or workload versions.
3. Expected versus actual behavior, exit code, and minimal reproduction.
4. Relevant logs, profile, and template ID when applicable.

Remove secrets and customer information. Tell the coordinator about unavailable prerequisites or builds. When comparing previews, say which version you tested.

## 6. Cleanup and troubleshooting

Stop only your test processes (Ctrl+C). Save issue links and useful logs. Close the terminal to discard session-only variables and the `func5` alias/function. Inspect your disposable test root before removing it; **do not delete v4, shared caches, your normal `.azure-functions` directory, or unrelated projects**. Undo only PATH/profile changes you deliberately made.

**Workload not found or wrong alias:** Confirm `FUNC_CLI_WORKLOADS_SOURCE` and `FUNC_CLI_WORKLOADS_PRERELEASE` in the current terminal. Remove a conflicting `--source` override; use the designated prerelease feed, not nuget.org.

**Quickstart templates missing:** Confirm `FUNC_CLI_QUICKSTART_MANIFEST_URL` in the current terminal. If results remain stale, stop the CLI and move only the `quickstart` cache under your isolated `FUNC_CLI_HOME` to a uniquely named backup; retry `func5 quickstart list`. Do not reset shared/default CLI state.

**Missing build or fixture:** Skip that subcase, record what was unavailable, and continue unrelated tests. Do not substitute a different CLI build or change authentication.
