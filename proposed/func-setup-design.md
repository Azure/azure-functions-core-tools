# `func setup` Design (Draft)

This document describes the proposed `func setup` command for the Azure Functions CLI (`func`). It is a working draft.

## 1. Identity

`func setup` is a **machine readiness orchestrator**. It coordinates several subsystems so that a freshly installed `func` is ready to develop or run Azure Functions, in one command, idempotently, in both interactive and CI environments.

It is intentionally a small, thin command. Its interface is small ("ready this machine for func"), but behind that interface it sequences work across:

- Host workload installation.
- Setup feature expansion, where user-facing feature IDs such as `runtime`, `node`, or `dotnet-isolated` map to one or more lower-level workload installs.
- Profile-constrained installation for runtime constraint sets.
- Extension bundle cache priming when the selected feature/worker runtime uses extension bundles.

### What it is *not*

- **Not an alias for `func workload install`.** `workload install` is a sharp tool ("install this specific workload package"). `setup` is an orchestration verb ("ready this machine"). `setup --features node` may expand to multiple workload installs and should not be treated as a literal workload package ID.
- **Not a project bootstrapper.** That is `func init`. `setup` may read selected project configuration for setup inputs, but it never writes to the project.
- **Not a project detector.** Stack/project detection remains a stack-specific implementation detail. `setup` does not infer stacks from files such as `package.json`, `requirements.txt`, `*.csproj`, `pom.xml`, or `local.settings.json`.
- **Not the telemetry owner.** Telemetry consent is a global first-CLI-invocation concern, not a `setup` concern.
- **Not a profile authoring tool.** Profile authoring remains with profile configuration files and profile-specific commands.

## 2. Goals

- Give a new user a single command to go from "I installed `func`" to "I can run a function".
- Give CI a single command to bring a fresh runner to a known-ready state.
- Be idempotent: safe to re-run on every CI job, on a developer's machine after adding new features, and after partial failures.
- Keep responsibilities narrow: orchestrate, do not own. Each underlying subsystem remains the source of truth for its own state.
- Allow profile constraints to influence dependency selection, including installing the maximum available version inside a profile range when requested.

## 3. Command Surface

```text
func setup [--features <list>]
           [--profile <name>]... [--profiles <list>]
           [--install-policy <latest-compatible|if-needed>]
           [--prerelease]
           [--non-interactive] [--yes] [--check]
           [--output <plain|json>]
```

| Option | Description |
| --- | --- |
| `--features <list>` | Comma-separated setup feature IDs to ensure are present. Features are CLI-curated capabilities, not raw workload package IDs. |
| `--profile <name>` | Canonical repeatable option for profile names whose constraints should be applied. |
| `--profiles <list>` | Comma-separated convenience alias for passing multiple profiles. |
| `--install-policy <policy>` | Dependency reconciliation policy. Default: `latest-compatible`. |
| `--prerelease` | Allow prerelease package versions during catalog resolution. Prerelease versions are not considered unless this option is present. |
| `--non-interactive` | Never prompt. Fail if a required answer is missing or a step would otherwise block. |
| `--yes`, `-y` | Accept defaults for any prompt that would otherwise block. Implies non-interactive. |
| `--check` | Report what would change. Make no mutations. Exits non-zero if anything is missing or drifted according to the selected install policy. |
| `--output <plain|json>` | Output mode. `plain` is human-readable text. `json` emits newline-delimited JSON events. |

## 4. Setup Features

`--features` accepts setup feature IDs. A feature is a user-facing capability that expands to one or more lower-level dependencies.

The initial built-in feature catalog is:

| Feature | Meaning |
| --- | --- |
| `host` | Install or verify only the Azure Functions host workload. No workers. No extension bundle. |
| `runtime` | Install or verify the host and the default stable extension bundle. No stack worker. |
| `node`, `python`, `java`, `powershell`, `custom`, `go` | Install or verify host, the selected worker workload, and the default stable extension bundle. |
| `dotnet-isolated` | Install or verify host and the `dotnet-isolated` worker. Extension bundles are skipped because this stack does not use them. |
| Future feature IDs, such as `durable` | Expand to one or more additional workloads or caches. |

Raw workload package IDs remain the responsibility of `func workload install`. Unknown setup feature IDs fail with a message that distinguishes setup features from workload package IDs and points users to `func workload search` or `func workload install`.

The supported .NET setup stack is `dotnet-isolated`. There is no separate in-process `dotnet` stack feature.

## 5. Feature Defaults

If `--features` is provided, it is the complete explicit feature request.

If `--features` is not provided:

1. If `.func/config.json` declares a worker runtime, setup treats that worker runtime as the stack feature to prepare.
2. Otherwise, setup defaults to the `runtime` feature.

Setup does not infer worker runtime from any other project file.

## 6. Profile Selection

Profiles provide version constraints. `setup` has no separate profile concept of its own; it consumes the existing profile catalog/resolution model.

Profile selection order:

1. Explicit profiles from repeatable `--profile` and comma-separated `--profiles`.
2. If no explicit profiles are provided and the project `.func/config.json` declares profiles, setup runs once for each declared profile.
3. If no project profiles are declared, setup uses the user default profile when one is configured.
4. If no profile is selected, setup runs without profile constraints.

Explicit profile names are de-duplicated case-insensitively while preserving the first occurrence.

If a profile is explicitly requested inside a project that declares a profile allow-list, and the profile is not listed in `.func/config.json`, setup warns but continues if the profile can be resolved. This matches the current active-profile resolver behavior.

Deprecated profiles warn and continue.

## 7. Profile-Constrained Dependencies

For each selected profile, setup runs a separate reconciliation loop. Profile constraints are not merged across profiles.

The profile affects dependency selection as follows:

| Dependency | Constraint behavior |
| --- | --- |
| Host workload | Constrained by the profile host version range. |
| Selected worker workloads | Constrained by the matching profile worker version range when one exists. |
| Extension bundle | Constrained by the profile extension bundle range, but only when the selected feature/worker runtime uses extension bundles. |

Worker constraints apply only to workers selected by `--features` or by `.func/config.json` worker runtime. Setup does not install every worker listed by a profile.

If a selected worker runtime is not supported by a profile, setup fails for that profile. If the profile supports the runtime but does not specify an explicit worker version range for it, setup installs the worker according to the selected install policy without a profile range.

## 8. Extension Bundle Policy

Setup uses a two-value extension bundle policy:

```text
NotSupported
DefaultStable
```

Policy resolution:

| Worker runtime / feature | Policy |
| --- | --- |
| `dotnet-isolated` | `NotSupported` |
| All other known stack features | `DefaultStable` |
| Unknown worker runtimes | `DefaultStable` |

`NotSupported` means setup does not install or check extension bundles for that feature. A profile `extensionBundle` range does not force bundle installation for a stack that does not use bundles.

`DefaultStable` means setup ensures the stable default extension bundle workload is present when the selected feature includes bundle setup.

When a project `host.json` declares an extension bundle, setup intersects the `host.json` bundle version range with the selected profile range. When no project bundle declaration is available, setup uses the stable default bundle ID with the profile range, if any.

## 9. Install Policy

`--install-policy` controls how aggressively setup reconciles installed state.

### `latest-compatible` (default)

For each dependency, setup resolves the maximum available version that satisfies all active constraints and ensures that version is installed.

- If a lower compatible version is installed and the catalog resolves a newer compatible version, setup installs the newer version.
- If catalog resolution fails for any reason but an installed version satisfies the active constraints, setup warns and accepts the installed version as a fallback. JSON output reports this as a fallback result.
- If no installed compatible version exists and catalog resolution fails, setup fails.

### `if-needed`

For each dependency, setup first checks installed state.

- If any installed version satisfies the active constraints, setup skips installation.
- If no installed compatible version exists, setup resolves and installs the maximum available compatible version.
- If no installed compatible version exists and catalog resolution fails, setup fails.

### `--prerelease`

Prerelease versions are considered only when `--prerelease` is present. Otherwise, setup resolves stable packages only.

## 10. Check Mode

`--check` uses the same resolution logic as a real run but makes no mutations.

`--check` follows the selected install policy:

- With `latest-compatible`, check reports drift when an installed compatible version is older than the maximum available compatible version.
- With `if-needed`, check passes when any installed version satisfies the active constraints.
- If catalog resolution fails and an installed compatible version exists, check reports the same fallback result as install mode.

`--check` exits with code `1` on any drift, missing dependency, incompatible installed state, or failed dependency resolution. It exits with code `0` only when all selected profiles and dependencies are satisfied according to the selected install policy.

When multiple profiles are selected, check mode evaluates all profile loops and summarizes all failures.

## 11. Interactive and Non-Interactive Behavior

The default interactive flow may prompt before installing selected dependencies.

`--non-interactive` never prompts. It fails if setup cannot decide what to install from explicit arguments and supported defaults.

`--yes` implies non-interactive and accepts setup defaults. For example, with no `--features` and no `.func/config.json` worker runtime, `--yes` accepts the default `runtime` feature.

Telemetry consent remains out of scope for `func setup`; it is handled by the global CLI bootstrap path.

## 12. JSON Output

`--output json` emits newline-delimited JSON (NDJSON). Each line is one event object. There is no separate final JSON document; the final state is represented by the `setup.completed` or `setup.failed` event.

The v1 event set is:

| Event | Description |
| --- | --- |
| `setup.started` | Emitted once when setup begins. Includes selected features, profiles, install policy, check mode, and prerelease setting. |
| `profile.started` | Emitted at the start of each profile loop. Includes profile name/source when one is active. |
| `dependency.detected` | Emitted for every resolved dependency before it is checked or installed. Includes dependency type, ID, constraints, feature, and profile. |
| `dependency.result` | Emitted after each dependency is checked or reconciled. Includes action/result, selected version, installed version, fallback state, and message. |
| `profile.completed` | Emitted after a profile loop completes. Includes profile outcome and summary counts. |
| `setup.completed` | Emitted when setup completes successfully or with check drift. Includes exit code and summary counts. |
| `setup.failed` | Emitted for fatal failures that prevent normal completion. |

Recommended dependency result states:

- `installed`
- `already-satisfied`
- `satisfied-fallback`
- `would-install`
- `would-skip`
- `skipped`
- `failed`

Human-readable output should present the same decisions in plain language.

## 13. State and File Layout

`func setup` mutates only dependency/cache state owned by the underlying subsystems:

```text
~/.azure-functions/
  workloads/   # Owned by the workload subsystem
  profiles/    # Owned by the profiles subsystem, if applicable
```

Setup does not write to:

- `.func/config.json`
- `.func/profiles.json`
- `host.json`
- `local.settings.json`

The first-run marker/hints originally proposed for setup are deferred from v1. First-run UX should be handled separately.

## 14. Failure Semantics

- Each underlying subsystem call is atomic per its own contract.
- Setup accepts partial completion; a re-run finishes the job. There is no rollback.
- Install mode stops at the first failed profile loop and exits non-zero.
- Check mode continues through all selected profile loops and exits non-zero if any loop fails or drifts.
- Errors clearly state what completed and what did not, with the exact command or option to retry when possible.

## 15. Boundaries with `init`, `start`, and `workload`

The commands each own one verb:

| Command | Verb |
| --- | --- |
| `func init` | Scaffold a project. |
| `func setup` | Ready a machine by ensuring selected features and profile-constrained dependencies are present. |
| `func start` | Run a project. |
| `func workload install` | Install a specific workload package ID or alias. |

`setup` does not call `init` or `start`.

`setup` may reuse the same lower-level resolvers as `start` for host, worker, profile, and extension bundle compatibility, but the setup reconciliation policy is different: setup can install or check the maximum compatible version according to `--install-policy`.

`setup --features` should not accept arbitrary workload package IDs as if it were `func workload install`. That separation keeps the user-facing readiness command distinct from the lower-level workload package command.

