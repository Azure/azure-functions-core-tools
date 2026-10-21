# Setup and Test Workflow

The live bug-bash wiki is authoritative:

<https://github.com/Azure/azure-functions-core-tools/wiki/Azure-Functions-CLI-(v5)-Bug-Bash>

Refresh it before setup, scenario selection, troubleshooting, and cleanup.
Commands and preview details can change during the event.

## 1. Detect the participant environment

Read-only PowerShell checks:

```powershell
$PSVersionTable.PSVersion
[System.Runtime.InteropServices.RuntimeInformation]::OSDescription
[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Get-Command func, gh, dotnet, node, python, python3, go -ErrorAction SilentlyContinue
```

Read-only bash/zsh checks:

```bash
uname -s
uname -m
printf '%s\n' "${SHELL##*/}"
for tool in func gh dotnet node python3 python go; do command -v "$tool" 2>/dev/null || true; done
```

Do not run `env`, `set`, `Get-ChildItem Env:`, or a history command. Check only
the bug-bash variable names listed by the live wiki, and retain only whether
each is `set`, `missing`, or `mismatch`.

Before installation, record:

- the resolved `func` path and `func --verbose` output if one already exists;
- whether `~/.azure-functions` already exists;
- the active shell and the applicable profile path;
- whether the three required bug-bash variables are already set;
- the user-approved sandbox path.

Do not copy a profile file into evidence. If profile tracking is useful, record
only its path, existence, and last-write time. Search later only for the exact
line or marker documented by the current installer.

## 2. Check only relevant prerequisites

Use the live wiki's current versions and requirements. Check a runtime only if
the user selected its stack. Report a missing prerequisite and the affected
scenario; do not install it without a new consent step.

PowerShell 7 is sufficient to run the repository's evidence helper on Windows
or macOS. A macOS participant using only bash/zsh may run the scenarios without
the helper; collect the same evidence fields manually.

## 3. Install with an explicit consent gate

Present:

1. the exact installer command copied from the live wiki;
2. its remote source URL and the fact that it executes downloaded code;
3. the documented install directory;
4. any `PATH`, profile, or bug-bash variable changes it may make;
5. the pre-existing installation state that cleanup must preserve.

Run the command only after an explicit confirmation. Do not translate the
official PowerShell installer into shell or the official shell installer into
PowerShell. Preserve its exit code and output.

If the installer fails, stop and diagnose that failure before manually changing
the environment. Any manual environment or profile change requires another
explicit confirmation and must use the exact values from the live wiki.

## 4. Verify the installed preview

Run:

```text
func --version
func --verbose
func help
```

Then verify the three required bug-bash variables against the live wiki. Compare
values in memory and report only status. Never write the values to the report.

If multiple `func` commands exist:

- show each resolved executable path;
- identify which one produced the reported version;
- update the current process `PATH` only after consent;
- persist a profile change only after separate consent.

Do not begin scenarios until the resolved executable is the intended v5 preview
and required configuration is present.

## 5. Select and execute scenarios

Offer only the groups relevant to the participant:

- top-level help, version, invalid input, cancellation, color, and non-TTY;
- setup, profiles, and workload lifecycle;
- init, new, run/start, and output modes;
- quickstart discovery and scaffolding;
- end-to-end Node, Python, .NET, Go, or other stacks currently listed;
- exploratory usability, documentation, or accessibility feedback.

Fetch the exact current commands and expected behavior from the wiki after the
user chooses. Create a fresh directory for each stack so state does not leak
between scenarios.

Before each scenario:

1. state the exact command and expected behavior;
2. identify mutations such as workload installation or generated files;
3. get consent if the scenario changes machine-wide state;
4. run it from the approved sandbox;
5. capture the exit code and separate stdout/stderr when practical;
6. note the smallest sequence that reproduces unexpected behavior.

Run cancellation, prompts, TUI, and `func run` in a visible interactive
terminal. Do not use the finite-command evidence helper for a process expected
to keep running.

## 6. Clean up

Re-read the wiki cleanup section. Compare its current instructions with the
pre-install inventory and propose exact targets before deleting anything.

Cleanup can include:

- workloads installed during the session;
- the v5 preview installation if it did not predate the session;
- only the installer-added profile lines;
- only the user-approved sandbox;
- local evidence after the user confirms it is no longer needed.

Never recursively delete a home directory, repository root, profile directory,
or unresolved variable. Resolve and display each target first, then obtain
confirmation. If a pre-existing installation or setting cannot be separated
confidently from the bug-bash changes, leave it in place and explain why.
