# `func setup` & Onboarding Design (Draft)

This document sketches the proposed `func setup` command and the broader onboarding story for the Azure Functions CLI (`func`). It is a working draft — open questions are called out inline.

## Goals

- Give new users a single command to go from "I installed `func`" to "I can run a function".
- Let users pick the workloads they care about up front (language stacks like Node / Python / .NET, plus extensions like Durable Functions, Azure-integration tooling, etc.), instead of discovering workloads ad-hoc.
- Support a non-interactive mode for CI / scripted environments.
- Centralize first-run concerns: telemetry opt-in, CLI config, baseline workload install.

## Command Surface (proposed)

```
func setup [--profile <name>] [--non-interactive] [--yes]
```

| Option              | Description                                                                 |
| ------------------- | --------------------------------------------------------------------------- |
| `--profile <name>`  | Apply a named profile (e.g. `node`, `python`, `dotnet`) without prompting.  |
| `--non-interactive` | Never prompt; fail if a required answer is missing. Intended for CI.        |
| `--yes`, `-y`       | Accept defaults for any prompt that would otherwise block.                  |

### Interactive flow (default)

1. Welcome + telemetry opt-in.
2. Pick workloads to install — language stacks (Node, Python, .NET, Java, …) and/or feature workloads (e.g. Durable Functions, Azure-integration tooling).
3. Confirm and install the corresponding workloads (or workload meta-packages — see below).
4. Write `.func/config.json` (or update the user-level config) with the chosen profile.
5. Print "what's next" hints (`func init`, `func new`, `func start`).

### Non-interactive flow (CI)

```
func setup --profile node --non-interactive
```

- No prompts. Telemetry defaults to off (or whatever the env/config says).
- Installs the workloads implied by the profile.
- Exits non-zero if anything is ambiguous or missing.

## Workload Meta-Packages

Concept: a meta-package is a workload that pulls in a curated set of other workloads.

- Example: `pythondev` brings in the Python stack workload + related tooling.
- Example: `durable` brings in Durable Functions tooling on top of whatever stack the user already has.
- Example: a `base` meta-package brings in everything needed to get started with `func` regardless of stack.
- Implementation TBD — likely a NuGet package with dependencies on the underlying workload packages, resolved by `func workload install`.

### Open questions

- Naming: `pythondev` vs `python-dev` vs `python.dev`?
- Do meta-packages live in the same feed as regular workloads?
- Can a profile reference a meta-package directly, or only individual workloads?
- Versioning: does the meta-package pin workload versions, or float?

## Profiles

Profiles tie together:

- A set of workloads to install.
- Defaults for `func start` (host version range, stack — see [func-start-design.md](./func-start-design.md)).
- Possibly telemetry / CLI defaults.

### Open questions

- Where do built-in profiles live? Shipped with the CLI? Downloaded?
- Can users define their own profiles, and where?
- Profile precedence: project-level `.func/config.json` vs user-level config?

## First-Run / Config Touchpoints

- Telemetry opt-in (one-time prompt; respect `DOTNET_CLI_TELEMETRY_OPTOUT`-style env vars).
- `func` CLI config (user-level, e.g. `~/.azure-functions/config.json`).
- Project-level `.func/config.json` — checked / created on `func setup` inside a project directory.
- Workload install via `func workloads install <name>` under the hood.

## Relationship to Other Commands

- `func setup` is the front door; `func workload install/uninstall` remains the lower-level primitive.
- `func init` should detect a missing setup and either auto-run it or point the user at it (TBD).
- `func start` consumes the profile written by `func setup` to resolve host + worker versions.

## Open Questions (cross-cutting)

- Is `func setup` re-runnable / idempotent? What does re-running do — diff and reconcile?
- Does `func setup` ever uninstall workloads it didn't install, to match a profile? Or only additive?
- How do we surface progress for long-running workload installs (especially in CI)?
- Should there be a `func setup --check` mode that reports current state without changing anything?
- What's the story for offline / air-gapped environments?
