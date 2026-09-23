# `func setup` Design (Draft)

This working draft records the goals and current implementation of `func setup` in the Azure Functions CLI (`func`). Readiness goals are distinct from the guarantees and limitations below.

## 1. Identity

`func setup` is a **dependency readiness orchestrator**. It reconciles selected CLI workloads in interactive and CI environments. Success does not guarantee that language SDKs, external tools, or every prerequisite needed to run a function are ready.

It is intentionally a small, thin command. Its interface is small ("ready this machine for func"), but behind that interface it sequences work across:

- Host workload installation.
- Setup feature expansion, where user-facing feature IDs such as `runtime`, `node`, or `dotnet-isolated` map to one or more lower-level workload installs.
- Profile-constrained installation for runtime constraint sets.
- Built-in stack and templates workload installation.
- Extension bundle workload installation when the selected feature/worker runtime uses extension bundles.

### What it is *not*

- **Not an alias for `func workload install`.** `workload install` is a sharp tool ("install this specific workload package"). `setup` is an orchestration verb ("ready this machine"). `setup --features node` may expand to multiple workload installs and should not be treated as a literal workload package ID.
- **Not a project bootstrapper.** That is `func init`. `setup` may read selected project configuration for setup inputs, but it never writes to the project.
- **Not a project detector.** Setup reads a declared runtime through the project configuration provider, including its local-settings projection. It does not infer a stack from source files or project manifests such as `package.json`, `requirements.txt`, `*.csproj`, or `pom.xml`.
- **Not the telemetry owner.** Telemetry consent is a global first-CLI-invocation concern, not a `setup` concern.
- **Not a profile authoring tool.** Profile authoring remains with profile configuration files and profile-specific commands.

## 2. Goals

These are readiness goals, not guarantees of the current workload reconciliation flow. Section 14 describes its limits.

- Give a new user a single command to go from "I installed `func`" to "I can run a function".
- Give CI a single command to bring a fresh runner to a known-ready state.
- Be idempotent: safe to re-run on every CI job, on a developer's machine after adding new features, and after partial failures.
- Keep responsibilities narrow: orchestrate, do not own. Each underlying subsystem remains the source of truth for its own state.
- Allow profile constraints to influence dependency selection, including installing the maximum available version inside a profile range when requested.

## 3. Command Surface

```text
func setup [<path>] [--features <list>]...
           [--profile <name>]... [--profiles <list>]
           [--install-policy <latest-compatible|if-needed>]
           [--source <nuget-feed>]
           [--prerelease]
           [--non-interactive] [--yes] [--check]
           [--output <plain|json>]
```

| Option | Description |
| --- | --- |
| `--features <list>` | Repeatable or comma-separated setup feature names. Accepts `host`, `runtime`, and stack/runtime names, not a literal workload package install request. |
| `--profile <name>` | Canonical repeatable option for profile names whose constraints should be applied. |
| `--profiles <list>` | Comma-separated convenience alias for passing multiple profiles. |
| `--install-policy <policy>` | Dependency reconciliation policy. Default: `latest-compatible`. |
| `--source <nuget-feed>` | Override the NuGet package source used for workload catalog resolution and installation. Same semantics as workload commands. |
| `--prerelease` | Override the workload prerelease policy. Otherwise use a valid `FUNC_CLI_WORKLOADS_PRERELEASE` setting, then the running CLI's stable/prerelease status. Bundle/templates channels have separate rules below. |
| `--non-interactive` | Suppress the stack picker. Use explicit features or supported project/runtime defaults. |
| `--yes`, `-y` | Suppress the stack picker and use project/runtime defaults when features are omitted. Does not select every stack. |
| `--check` | Check selected dependencies without installing or updating workloads. Metadata caches may change. Does not write the first-run marker. |
| `--output <plain|json>` | Output mode. `plain` is human-readable text. `json` emits newline-delimited JSON events and suppresses the stack picker. |

## 4. Setup Features

`--features` accepts setup feature IDs. A feature is a user-facing capability that expands to one or more lower-level dependencies.

The current built-in stack list is fixed to Node, Python, Go, and .NET. Feature expansion is:

| Feature | Meaning |
| --- | --- |
| `host` | Install or verify only the Azure Functions host workload. No workers. No extension bundle. |
| `runtime` | Host and the selected extension bundle, stable by default. No worker or stack workload. |
| `node`, `python` | Host, optional worker, built-in stack, optional templates, and the selected extension bundle. |
| `go` | Host, optional worker, built-in Go stack, and the selected extension bundle. No templates workload. |
| `dotnet`, also accepted as `dotnet-isolated` | Host, built-in .NET stack, and optional .NET templates. No separate worker or extension bundle for this feature. Profile checks use `dotnet-isolated`. |
| Other runtime names, including `java`, `powershell`, and `custom` | Host, optional worker, and the selected extension bundle. No stack or templates workload. |

Raw workload package installation belongs to `func workload install`. An unknown non-empty setup name is currently treated as a runtime, with an optional worker at `Azure.Functions.Cli.Workloads.Workers.<runtime>` and a bundle. It is not rejected merely for being unknown, and setup can succeed without that worker.

The `dotnet` setup name and its `dotnet-isolated` alias do not enable in-process .NET. `.net` and `dotnet-inprocess` are rejected.

Stack and templates package IDs use fixed `Azure.Functions.Cli.Workloads.<Node|Python|Go|DotNet>` and `Azure.Functions.Cli.Workloads.Templates.<Node|Python|DotNet>` mappings. Metadata-driven stack discovery, custom template package acquisition, and metapackage-based setup recipes are future work, not implemented by this setup flow.

## 5. Feature Defaults

If `--features` supplies a non-empty list, it is the complete explicit feature request.

Otherwise:

1. Setup reads `stack:runtime` from the project configuration provider for the selected directory. The provider first reads `local.settings.json`, projecting `Values.FUNCTIONS_WORKER_RUNTIME` to that key, then overlays `.func/config.json`. An explicit `stack.runtime` value in `.func/config.json` takes precedence.
2. With no non-empty configured runtime, an interactive plain run without `--non-interactive` or `--yes` offers the fixed stack picker. Stacks with a matching package ID in the workload registry are shown as installed and are not selectable. This is a UI hint, not a dependency health check.
3. With no configured runtime and the picker suppressed by `--non-interactive`, `--yes`, JSON output, or an unavailable interactive terminal, setup defaults to `runtime`.

If every built-in stack is already registered, the picker path is a successful no-op. Submitting an empty selection is an error. `--check` alone does not suppress the picker. Use explicit features or a non-interactive option for unattended checks.

## 6. Profile Selection

Profiles provide version constraints. `setup` consumes the existing profile catalog/resolution model rather than defining its own.

Profile selection order:

1. Explicit profiles from repeatable `--profile` and comma-separated `--profiles`.
2. If no explicit profiles are provided and the project `.func/config.json` declares profiles, setup runs once for each declared profile.
3. If no project profiles are declared, setup uses the user default profile when one is configured.
4. If no profile is selected, setup runs without profile constraints.

Explicit profile names are de-duplicated case-insensitively while preserving the first occurrence.

If a profile is explicitly requested inside a project that declares a profile allow-list, and the profile is not listed in `.func/config.json`, setup warns but continues if the profile can be resolved. This matches the current active-profile resolver behavior.

Deprecated profiles warn and continue.

## 7. Profile-Constrained Dependencies

Profile names are resolved before reconciliation. For each selected profile, setup then plans and runs a separate reconciliation loop. Profile constraints are not merged across profiles.

The profile affects dependency selection as follows:

| Dependency | Constraint behavior |
| --- | --- |
| Host workload | Constrained by the profile host version range. |
| Selected worker workloads | Constrained by the matching profile worker version range when one exists. |
| Extension bundle | Constrained by the profile extension bundle range, but only when the selected feature/worker runtime uses extension bundles. |

Host resolution uses the literal logical package ID `Azure.Functions.Cli.Workloads.Host`, not alias lookup or a hand-built RID suffix. Python workers use `Azure.Functions.Cli.Workloads.Workers.Python`. The workload installer follows a pointer's current-RID mapping at the exact pointer version and source. Installed dependency matching accepts physical package IDs and logical owner IDs.

Worker constraints apply only to selected workers, including those chosen through the project configuration defaults in section 5. Setup does not install every worker listed by a profile. Worker ranges do not constrain stack or templates versions.

If a selected runtime is not supported by a profile, the planner records a failure and omits that runtime's dependencies. Other planned dependencies can still install before that failure is reported. There is no per-profile preflight gate. If the profile supports the runtime but has no worker range, the optional worker uses the selected install policy without a profile range.

## 8. Extension Bundle Policy

Setup uses a two-value extension bundle policy:

```text
NotSupported
DefaultStable
```

Policy resolution:

| Worker runtime / feature | Policy |
| --- | --- |
| `dotnet` / `dotnet-isolated` | `NotSupported` |
| Other known runtime names | `DefaultStable` |
| Unknown worker runtimes | `DefaultStable` |

`NotSupported` means setup does not install or check extension bundles for that feature. A profile `extensionBundle` range does not force bundle installation for a stack that does not use bundles.

`DefaultStable` includes bundle setup on the stable channel unless the project explicitly selects another supported channel. The `host` feature alone omits bundle setup.

When a project `host.json` declares an extension bundle, setup intersects the `host.json` bundle version range with the selected profile range. When no project bundle declaration is available, setup uses the stable default bundle ID with the profile range, if any.

An explicit preview or experimental bundle ID also selects that channel for Node/Python templates. .NET templates remain channel-less and use the general prerelease policy. Bundle and channeled templates resolution falls back to stable with a warning when the requested non-stable channel has no matching version in the applicable range. A general prerelease opt-in does not change the selected channel.

The planner reads `host.json` for each profile, even for host-only or .NET selections.

## 9. Install Policy

`--install-policy` controls how aggressively setup reconciles installed state.

### `latest-compatible` (default)

For each dependency, setup resolves the maximum available version that satisfies all active constraints and ensures that version is installed.

- If a lower compatible version is installed and the catalog resolves a newer compatible version, setup installs the newer version.
- If a handled catalog error or no matching version prevents resolution, an installed compatible version can satisfy the dependency as `satisfied-fallback`. The handled errors include configuration/source errors as well as transport failures. This is not a fallback for arbitrary exceptions or later installation failures.
- Without an installed compatible version, required dependencies fail. Optional workers/templates are skipped when catalog resolution returns no matching version, but not when it throws a handled error.

### `if-needed`

For each dependency, setup first checks installed state.

- If any installed version satisfies the active constraints, setup skips installation.
- If no installed compatible version exists, setup resolves and installs the maximum available compatible version.
- If resolution fails, the same required/optional rules as `latest-compatible` apply.

### `--prerelease`

An explicit `--prerelease` value wins. Otherwise, a valid `FUNC_CLI_WORKLOADS_PRERELEASE` setting wins, then the CLI build determines the default. Stable builds exclude prereleases and prerelease builds include them. Bundle and Node/Python templates channels use the separate rules in section 8.

### `--source`

`--source` overrides `FUNC_CLI_WORKLOADS_SOURCE`, with nuget.org used when neither is set. Workload sources are HTTP(S) V3 service-index URLs, not local directories. The override is passed to workload resolution and installation without changing global configuration. Source validation occurs when resolution consumes the source, not as an unconditional setup preflight.

## 10. Check Mode

`--check` uses dependency resolution without installing or updating workloads. It never writes the first-run marker, including on the all-stacks-installed picker no-op path. Profile and NuGet metadata caches may change. It is not a filesystem-wide read-only or offline mode.

`--check` follows the selected install policy:

- With `latest-compatible`, check fails when the resolved target version is not installed, even if another compatible version is registered.
- With `if-needed`, check passes when any installed version satisfies the active constraints.
- For handled catalog errors or no matching version, check uses the same installed-version fallback as install mode.

`--check` exits with code `1` for failed dependency results or planning/configuration failures. A missing resolved target version is reported as `failed`, not `would-install`. Optional missing workers/templates can be `skipped`, so exit code `0` does not prove that every prerequisite exists or that installed payloads are intact.

Check mode continues through dependency results and returned planning failures for all selected profiles. A thrown configuration error ends the run rather than continuing to later profiles.

## 11. Interactive and Non-Interactive Behavior

The interactive flow selects stacks when no explicit or project feature is available. There is no separate setup confirmation before each dependency install.

`--non-interactive` suppresses the picker and uses explicit features or project/runtime defaults.

`--yes` and JSON output also suppress the picker. They do not select every stack. With no explicit features or configured project runtime, either uses `runtime`.

In plain output, `--yes` reports that this fallback targets only the host and extension bundle and points to `--features <stack>` for language-specific setup. The hint also applies in check mode. Explicit features, a configured runtime, and JSON output do not produce this hint.

Direct `setup` invocations skip the global first-run prompt. Parsed check and JSON setup invocations suppress the background CLI version check and trailing version/alias advisories, including after failures. JSON mode also suppresses setup's human prerelease hint. Telemetry ownership remains outside setup.

## 12. JSON Output

`--output json` emits newline-delimited JSON (NDJSON). Each renderer event has `type` and `timestamp` fields and uses the interaction service's raw stdout writer, bypassing console wrapping so it occupies one physical line. There is no separate final JSON document.

This contract covers setup renderer events, not every CLI failure. Parser, command-option/path validation, bootstrap, and unexpected exceptions can still use normal CLI error output. Cancellation exits with code `130` without a guaranteed final setup event.

The v1 event set is:

| Event | Description |
| --- | --- |
| `setup.started` | After features and profile scopes resolve. Includes features, worker runtimes, profiles, source override, install policy, check mode, and prerelease setting. |
| `profile.started` | At the start of each profile loop, with the profile name when constrained. |
| `dependency.detected` | Before a planned dependency is checked or installed. Includes dependency type, name, package ID when applicable, version range, and profile. |
| `dependency.result` | Status, package ID when applicable, version when selected, message, and optional warning. Also used for returned planning failures without a preceding `dependency.detected`. |
| `profile.completed` | Profile name, success, and failure count. |
| `setup.completed` | Successful reconciliation with `success: true`. Check drift produces `setup.failed` instead. |
| `setup.failed` | A failure count or configuration-error message. Can occur before `setup.started`. |
| `setup.warning` | Profile warnings, which can precede `setup.started`. |
| `setup.skipped` | Renderer event for the all-stacks-installed picker no-op. Ordinary JSON invocations bypass that picker path. |

Dependency types are `host`, `runtime`, `worker`, `stack`, `templates`, and `extension-bundle`. Dependency result states are:

- `satisfied`
- `installed`
- `satisfied-fallback`
- `skipped`
- `failed`

Human-readable output should present the same decisions in plain language.

## 13. State and File Layout

Normal setup can write dependency state, metadata caches, and the existing first-run marker through the owning subsystems. The default layout includes:

```text
~/.azure-functions/
  workloads/   # Owned by the workload subsystem
  workloads.json
  profiles/    # Profile metadata cache, when used
  .first-run-complete
```

Setup does not write to:

- `.func/config.json`
- `.func/profiles.json`
- `host.json`
- `local.settings.json`

Successful normal setup marks first-run completion on a best-effort basis, including the all-stacks-installed picker no-op. Check mode never marks it. JSON install mode still can. The global first-run coordinator owns the prompt and breadcrumb behavior.

`FUNC_CLI_HOME` can relocate CLI state. `FUNC_CLI_WORKLOADS_HOME` can separately relocate workload payloads and their registry, not the first-run marker. Metadata cache locations are owned by the profile and NuGet subsystems and are not restricted to the workload payload directory.

## 14. Failure Semantics

- Within each profile, setup processes planned dependencies first, then returned planning failures. Dependency order is host, each selected runtime's worker/stack/templates, then the bundle when included. A known planning failure does not prevent otherwise planned dependencies from installing.
- Install mode stops at the first failed dependency result, or the first returned planning failure reached afterward. That failed profile ends the run before later profiles. Earlier installs remain, including those in the same profile.
- Setup accepts partial completion. A re-run can reconcile remaining dependencies after the cause is addressed. There is no rollback or transaction across dependencies or profiles.
- Check mode continues through returned failures for all profiles. Thrown configuration errors abort the run, and unexpected exceptions reach the normal CLI error handler.
- Cancellation is cooperative. The runner checks it at phase, dependency, and reporting boundaries and passes the token to asynchronous work. It cannot undo completed installs or output, or make an in-progress write atomic.

### Current limitations

- Workers and templates are optional. No matching catalog version can mean that all published versions are incompatible with the range or policy, not just that a package is absent. Those optional dependencies can still be skipped.
- Installed-state checks compare registry identities and versions. They do not validate payload files or prove that a workload can run.

## 15. Boundaries with `init`, `start`, and `workload`

The commands each own one verb:

| Command | Verb |
| --- | --- |
| `func init` | Scaffold a project. |
| `func setup` | Reconcile selected CLI workloads under profile constraints. |
| `func start` | Run a project. |
| `func workload install` | Install a specific workload package ID or alias. |

`setup` does not call `init` or `start`.

`setup` may reuse the same lower-level resolvers as `start` for host, worker, profile, and extension bundle compatibility, but the setup reconciliation policy is different: setup can install or check the maximum compatible version according to `--install-policy`.

`setup --features` is not a literal package installation interface. Its generic runtime fallback remains as described in section 4. Use `func workload install` for a package ID or alias.
