# Setup and Test Workflow

The live bug-bash wiki is authoritative:

<https://github.com/Azure/azure-functions-core-tools/wiki/Azure-Functions-CLI-(v5)-Bug-Bash>

Refresh it before setup, scenario selection, troubleshooting, reporting, and
cleanup. Do not use a PR snapshot or a remembered preview number.

## 1. Read-only detection

Run this phase before proposing any setup. It must not create directories,
download files, set variables, change `PATH`, invoke installers, or acquire
workloads.

PowerShell:

```powershell
$PSVersionTable.PSVersion
[System.Runtime.InteropServices.RuntimeInformation]::OSDescription
[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Get-Command func, gh, dotnet, node, python, python3, go -CommandType Application -ErrorAction SilentlyContinue
```

Bash on Linux/macOS:

```bash
uname -s
uname -m
printf '%s\n' "${SHELL##*/}"
for tool in func gh dotnet node python3 python go; do command -v "$tool" 2>/dev/null || true; done
```

On Linux, determine compatibility without changing the machine:

```bash
test ! -e /etc/alpine-release
getconf GNU_LIBC_VERSION 2>/dev/null || ldd --version 2>&1 | head -n 1
```

The documented target set is Windows x64/ARM64, Linux glibc x64/ARM64, and
macOS Intel/Apple silicon. Alpine/musl is unsupported. If architecture, libc,
or a published build is outside the live guide, skip rather than substitute.

Check only prerequisites for the stack the participant selected. The live guide
currently separates baseline Node.js, Python, .NET isolated, and exploratory Go
requirements. Java, PowerShell, special platform cases, .NET 11, publisher
trust, recovery, and pinned package versions require assigned fixtures or
instructions.

Inventory normal `func` paths and versions, but treat v4 on `PATH` as expected
coexistence. Do not repair `PATH`. Check only these environment names:

- `FUNC_CLI_WORKLOADS_SOURCE`
- `FUNC_CLI_QUICKSTART_MANIFEST_URL`
- `FUNC_CLI_WORKLOADS_PRERELEASE`
- `FUNC_CLI_HOME`
- `FUNC_CLI_WORKLOADS_HOME`

Compare against the current wiki and approved disposable paths in memory.
Retain only `set`, `missing`, or `mismatch`, never the values.

## 2. Plan and consent

Select:

- a new, unused disposable bug-bash root;
- the install directory under that root;
- isolated CLI and workload home directories under that root;
- a separate evidence directory outside any project destination that a
  `--force` or recovery scenario may clean.

Present the exact installer block from the live wiki. It should pin the current
preview and use the documented skip-PATH switch. Explain that the installer
does not configure the three feed/template settings. All five settings are
session-only and must be set in every terminal used for CLI commands.

Get explicit consent before creating the root, downloading the installer,
executing it, or setting session variables. Never edit a shell profile or
persist environment settings.

## 3. Install and verify coexistence

Run the exact platform block from the live wiki in one user-visible terminal.
The root must be unused. If it already exists, choose a different root rather
than cleaning it automatically.

The wiki may define `func5` as a convenience alias/function in that terminal.
For agent shell calls, evidence capture, and second-terminal commands, use the
literal executable path instead. Aliases and functions are not inherited by
child processes, and agent tool calls start fresh shells.

For every fresh agent shell call:

1. set all five approved values in that same process;
2. assign the literal isolated executable path;
3. run the requested command through that path;
4. do not modify normal `PATH`.

Verify:

- the literal executable exists under the approved install path;
- `--version` equals the exact live-wiki version;
- `--verbose` identifies the intended build, runtime, OS, and architecture;
- help renders through the same executable;
- all five session settings match their expected values.

Stop on a missing build or any version mismatch. Do not continue with another
preview. Normal `func` may still resolve to v4 and should remain untouched.

## 4. Run one local E2E first

Choose one supported stack from the live guide and use a fresh project name.
Track these as distinct outcomes:

1. setup completed for the selected stack;
2. project initialization completed;
3. templates could be listed;
4. an actual function was generated;
5. the host started;
6. a real HTTP endpoint existed and responded;
7. Ctrl+C stopped the host and owned child processes cleanly.

Template listing does not prove generation. An empty app can start without
providing an HTTP endpoint. Do not claim an invocation test unless a generated
or prepared HTTP function actually responded.

Use the current `--format` spelling from the wiki. JSON output is newline-
delimited events, not one final JSON document. Keep host/dashboard,
cancellation, prompt, and non-TTY tests visible and interactive.

Go remains exploratory and Flex-only. Initialization already generates its HTTP
`hello` function, so do not add a `func new` step. Use only assigned compatible
worker/package prerequisites.

In a second terminal, repeat the five session settings and invoke the same
literal executable. Keep logs and evidence outside project destinations that
may be force-cleaned.

## 5. Optional exploration

Fetch current commands from the wiki after the participant chooses an area.

### Setup and profiles

- Test only disposable projects and approved target profiles.
- Record the target-profile name, never shell profile contents.
- Unsupported stack/profile combinations should fail before worker acquisition.
- In setup JSON check mode, expect NDJSON events.

### Workloads

- Inventory isolated workload state before and after mutation.
- Pinned versions, Host packages, local packages, trust, and destructive
  install/uninstall cases require the exact assigned fixture.
- Do not fall back to nuget.org or replace an unavailable special version.

### CLI update

- Use a separate disposable CLI install so the main Preview test root remains
  stable.
- Restore the exact current preview before continuing other scenarios.
- A successful same-version update is a no-op, not proof of replacement.
- Recovery, missing metadata, decline, cancellation, and trust cases require
  assigned fixtures. Never edit a working installation's metadata to authorize
  an update.

### Init, new, run, and Quickstart

- Use template IDs returned by current list commands.
- Distinguish listing, generation, startup, and invocation.
- Follow generated local-development instructions, but skip cloud deployment.
- Test `--force`, adoption, healing, or populated destinations only with
  sentinel files in disposable directories.
- Quickstart recovery may move only the isolated quickstart cache to a uniquely
  named backup after consent. Never reset shared/default CLI state.

### Assigned focus areas

Installer upgrade/recovery, publisher trust, special workload/Host versions,
Java, PowerShell, Windows ARM64 target-profile cases, .NET 11, incompatible
bundles, and failure fixtures remain assigned. If a fixture is unavailable,
record that and continue unrelated tests.

Never deploy to Azure or change authentication to unblock a scenario.

## 6. Cleanup

Re-read the live cleanup section. Stop only processes started for the test.
Save issue links and useful evidence before proposing deletion.

Closing the terminal discards session-only variables and the `func5`
alias/function. Inspect and resolve every deletion target before consent.
Cleanup may remove only:

- the approved disposable bug-bash root;
- the separate disposable update root;
- owned project sandboxes;
- isolated cache backups created during recovery;
- evidence after explicit confirmation that it is no longer needed.

Preserve the normal v4 installation, normal `.azure-functions`, shared caches,
shell profiles, unrelated projects, and any path that cannot be confidently
attributed to this session. Never delete a broad/root directory or an unresolved
variable.
