# `func setup` Design (Draft)

This document describes the proposed `func setup` command for the Azure Functions CLI (`func`). It is a working draft. Open questions are called out inline.

## 1. Identity

`func setup` is a **machine readiness orchestrator**. It coordinates several subsystems so that a freshly installed `func` is ready to develop or run Azure Functions, in one command, idempotently, in both interactive and CI environments.

It is intentionally a small, thin command. Its interface is small ("ready this machine for func"), but behind that interface it sequences work across:

- Workload installation (via `func workload install`).
- Profile pre-install for runtime constraint sets (via `func profile install`, see [cli-profiles.md](./cli-profiles.md)).
- Cache priming (extension bundle cache, profile registry cache).
- First-run hints (telemetry consent is handled separately, see §6).

### What it is *not*

- **Not an alias for `func workload install`.** That command is a sharp tool ("install this specific workload"). `setup` is an orchestration verb ("ready this machine"). The distinction is by intent, not capability.
- **Not a project bootstrapper.** That is `func init`. `setup` may *read* project files for inference, but it never writes to the project.
- **Not the telemetry owner.** Telemetry consent is a global first-CLI-invocation concern, not a `setup` concern. See §6.
- **Not a profile authoring tool.** Profile authoring lives with [cli-profiles.md](./cli-profiles.md) (`.func/profiles.json`, `~/.azure-functions/profiles.json`).
- **Has no "profile" concept of its own.** The word *profile* is reserved for [cli-profiles.md](./cli-profiles.md).

## 2. Goals

- Give a new user a single command to go from "I installed `func`" to "I can run a function".
- Give CI a single command to bring a fresh runner to a known-ready state.
- Be idempotent: safe to re-run on every CI job, on a developer's machine after adding new languages, after partial failures.
- Keep responsibilities narrow: orchestrate, do not own. Each underlying subsystem remains the source of truth for its own state.

## 3. Command Surface

```
func setup [--workloads <list>] [--profiles <list>]
           [--non-interactive] [--yes] [--check]
```

| Option              | Description                                                                                                         |
| ------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `--workloads <list>`| Comma-separated workload IDs to ensure are installed (e.g. `node,python,durable`).                                  |
| `--profiles <list>` | Comma-separated profile names whose dependencies should be pre-installed (see [cli-profiles.md](./cli-profiles.md)).|
| `--non-interactive` | Never prompt. Fail if a required answer is missing or a step would otherwise block.                                 |
| `--yes`, `-y`       | Accept defaults for any prompt that would otherwise block. Implies non-interactive.                                 |
| `--check`           | Report what would change. Make no mutations. Exits non-zero if anything is missing.                                 |

### 3.1 Interactive flow (default)

1. If running inside a project directory, infer suggested workloads from project files (read-only).
2. Present a grouped picker (language stacks, feature workloads).
3. Confirm and install the selected workloads via the workload subsystem.
4. If `.func/config.json` declares profiles (see [cli-profiles.md](./cli-profiles.md)), pre-install their dependencies.
5. Print "what's next" hints (`func init`, `func new`, `func start`).

### 3.2 Non-interactive flow (CI)

```
func setup --workloads node,python --profiles flex --non-interactive --yes
```

- No prompts. Telemetry default is whatever the env/config dictates (see §6).
- Installs the listed workloads. Pre-installs dependencies for the listed profiles.
- Exits non-zero on any failure or ambiguity.

### 3.3 `--check` mode

Same resolution logic as a real run, no mutations. Reports what is missing (workloads, profile dependencies, caches). Useful for verifying a pre-baked CI image.

## 4. Re-run Model: Idempotent Additive Reconciliation

Each invocation ensures the requested set is *present*. It never removes.

- Workloads already installed are skipped.
- Workloads not yet installed are installed.
- Profile dependencies already cached are skipped.
- Profile dependencies not yet cached are fetched.

`setup` does not maintain a manifest of "what setup installed." The workload subsystem and profile registry are the source of truth for installed state. Removing a workload is `func workload uninstall`'s job, not `setup`'s.

The only state `setup` writes is a **first-run completed** marker (user-level), used to suppress first-run hints on subsequent invocations.

## 5. Boundaries with `init` and `start`

The three commands each own one verb (scaffold, ready, run). They do not call each other. They communicate through state on disk and installed workloads, not through invocation.

| Scenario                                                            | Behavior                                                                                                       |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `func init` on a fresh machine (no workloads installed)             | `init` succeeds. It does not check or auto-run `setup`. Project scaffolding does not gate on machine state.    |
| `func start` on a fresh machine (no host workload installed)        | Uses the auto-install pattern from [cli-profiles.md](./cli-profiles.md) §13.2: prompt interactive, error in CI.|
| `func setup` inside an existing project                             | Reads project files (read-only) for inference. Reads `.func/config.json` for declared profiles. No project writes.|

## 6. Telemetry

Telemetry consent is **out of scope for `func setup`**. It is a global concern, prompted on first invocation of any `func` command, owned by the CLI bootstrap path. `setup` happens to be one such command but is not special in this regard. Env vars (`DOTNET_CLI_TELEMETRY_OPTOUT`-style) override the prompt as expected.

This avoids the failure mode where a user runs `func init` first, never sees `setup`, and is silently never asked about telemetry.

## 7. State and File Layout

`func setup` mutates only user-level state:

```
~/.azure-functions/
  .first-run                     # Marker: first-run hints already shown
  workloads/                     # Owned by the workload subsystem
  profiles/                      # Owned by the profiles subsystem
```

`setup` does not write to:

- `.func/config.json` (owned by the profiles proposal).
- `.func/profiles.json` (owned by the profiles proposal).
- `host.json`, `local.settings.json` (owned by `func init`).

## 8. Failure Semantics

- Each underlying subsystem call is atomic per its own contract (e.g. workload install per the Workloads spec).
- `setup` itself accepts partial completion: a re-run finishes the job. There is no rollback.
- Errors clearly state what completed and what did not, with the exact command to retry the failed step.
- Non-zero exit on any failure in `--non-interactive` or `--check` mode.

## 9. Open Questions

These are concrete, lower-level questions. The foundational architecture (identity, scope, re-run model, boundaries) is settled above.

- **Progress reporting.** How is long-running workload install progress surfaced in CI logs (where TTY tricks fail)? Plain-text periodic lines, structured JSON via a flag, or both?
- **Offline / air-gapped.** Should `setup` accept a `--offline` flag that skips network attempts and only validates that already-cached artifacts satisfy the request? (Likely yes, mirroring `func start --offline` from the profiles proposal.)
- **Picker UX.** Categorized prompt vs flat list. Do we ship a recommended-defaults set per detected language?
- **Discoverability after install.** Where does the "next step is `func setup`" hint surface? Post-install message from the package, output of `func` with no args, both?
- **`--check` exit codes.** Single non-zero on any drift, or distinct codes for "missing workload" vs "missing profile cache" vs "stale registry"?
- **Inferring workloads from a project.** What signals do we read (e.g. `host.json`, presence of `requirements.txt` / `package.json` / `*.csproj`)? How is the inference surfaced (pre-selected in picker vs informational only)?
