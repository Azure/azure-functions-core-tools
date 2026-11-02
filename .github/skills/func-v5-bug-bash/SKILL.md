---
name: func-v5-bug-bash
description: 'Use when someone wants to join or run the Azure Functions CLI v5 bug bash, install or configure the published preview on Windows, Linux, or macOS, select or execute test scenarios, collect diagnostics or screenshots, report bugs or feedback, or clean up the isolated bug-bash environment.'
license: MIT
compatibility: 'Windows x64/ARM64 with PowerShell 7+, Linux glibc x64/ARM64 with Bash, or macOS Intel/Apple silicon with Bash. Alpine/musl is unsupported. PowerShell 7 is optional on Linux/macOS for deterministic evidence capture. GitHub CLI 2.90+ is optional for personal skill installation and issue submission.'
---

# Run the Azure Functions CLI v5 Bug Bash

Use the live [bug-bash wiki](https://github.com/Azure/azure-functions-core-tools/wiki/Azure-Functions-CLI-(v5)-Bug-Bash)
as the source of truth for the exact preview version, installers, configuration,
prerequisites, scenarios, issue form, and cleanup. Refresh it at the start and
before setup, reporting, or cleanup. Do not substitute a PR snapshot or copy the
full guide into this skill.

This skill tests the published preview in isolated disposable state. It is not
the repository's `setup-local-cli` source-build workflow.

## Safety and isolation

- Separate read-only detection from setup. A request to join the bug bash is not
  consent to install, set variables, acquire workloads, create projects, or
  delete anything.
- Before every mutation, show the exact command, paths, and expected effect, then
  get explicit consent.
- Preserve the participant's normal v4 `func`, `.azure-functions` directory,
  shared caches, profiles, and unrelated projects. It is expected that normal
  `func` may continue to resolve to v4. Do not repair or reorder `PATH`.
- Use the wiki's unused disposable root, isolated CLI/workload homes, pinned
  installer, and skip-PATH option. Keep settings and any `func5` alias/function
  in the current terminal only. Never edit shell profiles or persist variables.
- Agent shell calls are fresh processes. Repeat the five approved session
  settings in every call that needs them and invoke the isolated executable by
  its literal path. A shell alias or function is not inherited by child
  processes and must never be passed to the evidence helper.
- Inspect only `FUNC_CLI_WORKLOADS_SOURCE`,
  `FUNC_CLI_QUICKSTART_MANIFEST_URL`, `FUNC_CLI_WORKLOADS_PRERELEASE`,
  `FUNC_CLI_HOME`, and `FUNC_CLI_WORKLOADS_HOME`. Compare values in memory and
  report only `set`, `missing`, or `mismatch`.
- Use synthetic data in assigned disposable directories. Never deploy to Azure,
  change authentication to unblock a test, or use customer data.
- Never collect or upload secrets, tokens, keys, connection strings, customer
  data, full environments, broad command history, profile contents, or
  unrelated files.
- Screenshots must be user-initiated and limited to a selected window or region.
  Never silently capture the desktop.

## Workflow

1. **Refresh authoritative instructions.** Read the live wiki and identify the
   current exact version, platform support, selected-stack prerequisites,
   installer block, five session settings, first local E2E, optional focus
   areas, issue-form link, and cleanup guidance.
2. **Detect without mutating.** Identify OS, architecture, shell, Linux libc,
   existing `func` resolutions, the unused-root requirement, and only the
   prerequisites for the selected stack. Alpine/musl is out of scope. Inventory
   the five approved settings by status only.
3. **Plan isolated setup.** Select a new disposable root and evidence location
   outside any project destination that may be force-cleaned. Present the exact
   wiki commands, including the pinned version and skip-PATH option. Get consent
   before downloading or executing the installer or setting session variables.
4. **Install and verify in one terminal.** Use the exact live commands. Execute
   `--version`, `--verbose`, and help through the selected isolated executable,
   and enforce an exact match with the wiki version. Do not treat v4 on `PATH`
   as a failure. Stop if the selected build or version is unavailable.
5. **Run one local E2E first.** Use one selected supported stack and a fresh
   project. Distinguish template listing, successful function generation, empty
   host startup, real HTTP invocation, and clean shutdown. Run interactive,
   cancellation, dashboard, and long-lived host tests in a visible terminal.
6. **Explore optional areas.** Route setup/profile, workload, update,
   init/new/run, Quickstart, output, and recovery checks according to the live
   guide. Use a separate disposable CLI install for update coverage. Assigned
   fixtures and special versions stay assigned; do not improvise replacements.
7. **Capture finite evidence.** Invoke
   [the evidence helper](scripts/capture-evidence.ps1) with the literal isolated
   CLI path, exact expected version, expected install path, and expected five
   settings when PowerShell 7 is available. Otherwise use the reporting
   workflow's Bash-only capture pattern. Neither path may fall back to `PATH`, a
   `func5` alias, or shell evaluation. Follow
   [the reporting workflow](references/reporting.md).
8. **Preview the report.** Discover the current title prefix and default labels
   from the issue form linked by the live wiki on the repository's default
   branch. Show the complete proposed title, body, labels, evidence, redactions,
   and attachment list. Require explicit confirmation before opening a form,
   creating an issue, or uploading anything.
9. **Submit only after approval.** Prefer the exact wiki-linked issue form.
   Authenticated `gh` may open that form or create the equivalent issue after a
   separate confirmation. Verify the final labels and attachments. Never
   auto-publish.
10. **Clean up narrowly.** Stop only owned test processes. Re-read the wiki,
    inspect exact targets, and remove only approved disposable state. Preserve
    v4, normal `.azure-functions`, shared caches, unrelated projects, and
    evidence until the user says it is no longer needed. Closing the terminal
    discards session-only settings and aliases.

Use [setup and scenario guidance](references/workflow.md) for the detailed
runbook. Use [personal installation guidance](references/personal-install.md)
when a participant needs this skill outside a checkout of the temporary branch.

## Completion

Report:

- exact preview version and literal executable path;
- platform, architecture, libc when relevant, and shell;
- status of the five approved session settings;
- selected stack and scenarios, including whether generation, host startup,
  HTTP invocation, and shutdown each succeeded;
- relevant Host/workload/runtime versions, target profile, and template ID;
- local evidence/report paths;
- issue URL and verified labels, or why no issue was submitted;
- cleanup performed and intentionally retained state.
