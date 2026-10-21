---
name: func-v5-bug-bash
description: 'Use when someone wants to join or run the Azure Functions CLI v5 bug bash, install or configure the published preview on Windows or macOS, select or execute test scenarios, collect diagnostics or screenshots, report bugs or feedback, or clean up the bug-bash environment.'
license: MIT
compatibility: 'Windows or macOS with network access and PowerShell 7 or bash/zsh. GitHub CLI 2.90+ is optional for personal skill installation and issue submission.'
---

# Run the Azure Functions CLI v5 Bug Bash

Use the live [bug-bash wiki](https://github.com/Azure/azure-functions-core-tools/wiki/Azure-Functions-CLI-(v5)-Bug-Bash)
as the source of truth for prerequisites, installation commands, required
environment configuration, test scenarios, and cleanup. Read it at the start
of the session and again before giving setup or cleanup commands. Do not copy
an old preview version, installer source, feed URL, runtime minimum, or scenario
list from memory.

This skill is for installing and testing the published bug-bash preview. Do not
substitute the repository's `setup-local-cli` skill, which builds a contributor
checkout and a local workloads feed.

## Safety rules

- Get explicit consent before running an installer, changing `PATH`, changing a
  shell profile, persisting an environment variable, installing a workload, or
  deleting anything during cleanup. Show the exact command and affected path
  first. A request to "join the bug bash" is not blanket consent.
- Work in a clean, user-approved sandbox with synthetic data. Never use customer
  projects, production resources, real connection strings, or unrelated files.
- Inspect only the three bug-bash environment variables named by the live wiki.
  Never dump the full environment, shell history, credential stores, or profile
  contents.
- Never collect or upload secrets, tokens, keys, connection strings, customer
  data, or unrelated logs. If output may contain any of these, stop and create a
  synthetic reproduction or a manually redacted excerpt.
- Keep evidence local until the user has reviewed the complete proposed title,
  labels, body, command output, and attachment list and explicitly confirms
  submission.
- Never silently capture the desktop. Screenshots must be initiated or selected
  by the user and limited to the relevant window or region.

## Workflow

1. **Read the source of truth.** Fetch the live wiki and summarize only the
   sections needed for the participant's requested stack or command area. Link
   to the wiki rather than duplicating its full test plan.
2. **Detect without changing anything.** Determine Windows versus macOS, CPU
   architecture, active shell, existing `func` resolution/version, and only the
   prerequisites for the scenarios the user wants. Inventory any pre-existing
   `~/.azure-functions` installation and whether each required bug-bash variable
   is set, but do not record variable values.
3. **Propose setup and get consent.** State which official wiki command will
   run, whether it downloads and executes a remote installer, which install
   directory it uses, and which profile or environment settings it may change.
   Wait for an explicit yes before executing it.
4. **Install from the wiki.** Use the exact official command currently shown for
   the participant's OS and shell. Do not rewrite the installer or substitute a
   local-source build. Surface any nonzero exit code and captured error output.
5. **Verify setup.** Run `func --version`, `func --verbose`, and `func help`.
   Verify that each required bug-bash variable is present and matches the live
   wiki, but report only `set`, `missing`, or `mismatch`; do not print or save
   values. Resolve any multiple-`func` or stale-`PATH` ambiguity before testing.
6. **Choose scenarios.** Offer the relevant groups from the live wiki, let the
   user choose a command surface or stack, and restate expected behavior before
   running each scenario. Use a fresh sandbox per stack. Keep interactive,
   cancellation, TUI, and long-running tests in a visible terminal.
7. **Capture a minimal reproduction.** For a finite non-interactive command,
   use [the evidence script](scripts/capture-evidence.ps1). For interactive or
   long-running commands, capture the smallest safe transcript manually and use
   a separate finite repro when possible. Follow the
   [reporting workflow](references/reporting.md).
8. **Review before publishing.** Prepare the report from
   [the issue template](assets/issue-report.md), list every proposed attachment,
   preview user-selected images when possible, and show the title, labels, body,
   and evidence to the user. Require a separate explicit confirmation before
   creating the issue or uploading any attachment.
9. **Submit or hand off.** Prefer authenticated `gh` after confirmation. Use
   `v5` plus one available classification label: `bug`, `enhancement`, or
   `documentation`. If `gh` is unavailable, unauthenticated, unsupported, or
   lacks permission, save the Markdown report locally and open the repository's
   new-issue page without embedding logs in the URL.
10. **Clean up deliberately.** Re-read the live cleanup section, compare it with
    the pre-install inventory, and propose only the exact workloads, preview
    installation, sandbox, profile lines, and evidence files added during this
    session. Get confirmation before each destructive group and preserve
    evidence until the user says it is no longer needed.

Use [setup and scenario guidance](references/workflow.md) for the detailed
runbook. Use [personal installation guidance](references/personal-install.md)
when a participant needs this skill outside a checkout of the temporary branch.

## Completion

Finish with:

- preview CLI version and resolved executable;
- platform, architecture, and shell;
- scenarios completed and their outcomes;
- local evidence/report paths;
- issue URL and verified labels, or the explicit reason submission was not
  completed;
- cleanup performed and anything intentionally retained.
