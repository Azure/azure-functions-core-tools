# `func setup` Design (Draft)

This working draft records the intended setup goals and the current Azure Functions CLI (`func`) implementation. The inherited limitations listed below remain gaps against the readiness goal, not newly accepted completion criteria.

## 1. Identity

`func setup` is a **dependency readiness orchestrator**. It coordinates CLI workloads in interactive and CI environments. Success reports the selected workload reconciliation outcome, not a guarantee that language SDKs, external tools, or every runtime prerequisite are ready.

It is intentionally a small, thin command. Its interface is small ("ready this machine for func"), but behind that interface it sequences work across:

- Host workload installation.
- Setup feature expansion, where user-facing feature IDs such as `runtime`, `node`, or `dotnet-isolated` map to one or more lower-level workload installs.
- Profile-constrained installation for runtime constraint sets.
- Stack and templates workload installation when discovery provides them.
- Extension bundle workload installation when the selected feature/worker runtime uses extension bundles.

### Scope of the discovery change

Setup discovers package identity from workload metadata, then reconciles concrete dependencies. The extraction keeps feature resolution, profile selection, planning, installation, and rendering independently testable. Discovery replaces the fixed stack list without introducing another package manager.

Three invariants govern that integration. Partial discovery must not replace known ownership with fallback guesses. A prospective templates package must not displace a usable installed owner without explicit migration. Setup and `func new` must use the same templates eligibility and ownership rules.

Discovery distinguishes source configuration errors from transport failures. It does not add SDK provisioning, feed-wide uniqueness guarantees, transactions, automatic owner migration, or RID stack/templates support. The existing channel-less .NET templates version ordering is unchanged.

### What it is *not*

- **Not an alias for `func workload install`.** `workload install` is a sharp tool ("install this specific workload package"). `setup` is an orchestration verb ("ready this machine"). `setup --features node` may expand to multiple workload installs and should not be treated as a literal workload package ID.
- **Not a project bootstrapper.** That is `func init`. `setup` may read selected project configuration for setup inputs, but it never writes to the project.
- **Not a project detector.** Stack/project detection remains a stack-specific implementation detail. `setup` does not infer stacks from files such as `package.json`, `requirements.txt`, `*.csproj`, `pom.xml`, or `local.settings.json`.
- **Not the telemetry owner.** Telemetry consent is a global first-CLI-invocation concern, not a `setup` concern.
- **Not a profile authoring tool.** Profile authoring remains with profile configuration files and profile-specific commands.

## 2. Goals

These are readiness goals, not guarantees that every prerequisite is ready. Section 14 describes the remaining readiness limitations.

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
| `--features <list>` | Repeatable or comma-separated setup feature IDs. Accepts `host`, `runtime`, and stack/runtime names, not literal workload package IDs. |
| `--profile <name>` | Canonical repeatable option for profile names whose constraints should be applied. |
| `--profiles <list>` | Comma-separated convenience alias for passing multiple profiles. |
| `--install-policy <policy>` | Dependency reconciliation policy. Default: `latest-compatible`. |
| `--source <nuget-feed>` | Override the HTTP(S) V3 NuGet service index used for discovery, resolution, and installation. Local feed directories are not supported. |
| `--prerelease` | Override the workload prerelease policy. Without an explicit value, use `FUNC_CLI_WORKLOADS_PRERELEASE` when valid, otherwise the running CLI's stable/prerelease status. Explicit bundle channels have separate rules below. |
| `--non-interactive` | Never prompt. Fail if a required answer is missing or a step would otherwise block. |
| `--yes`, `-y` | Suppress the stack picker and use project/runtime defaults when features are omitted. |
| `--check` | Check selected dependencies without installing or updating workloads. Metadata caches may change. Does not write the first-run marker. |
| `--output <plain|json>` | Output mode. `plain` is human-readable text. `json` emits newline-delimited JSON events and suppresses the stack picker and human advisories. |

## 4. Setup Features

`--features` accepts setup feature IDs. A feature is a user-facing capability that expands to one or more lower-level dependencies.

The feature expansion is:

| Feature | Meaning |
| --- | --- |
| `host` | Install or verify only the Azure Functions host workload. No workers. No extension bundle. |
| `runtime` | Install or verify the host and selected extension bundle (stable by default). No stack worker. |
| Catalog stack, such as `node`, `python`, or `go` | Host, optional worker, discovered stack and optional templates workloads, plus the selected extension bundle. |
| `dotnet`, also accepted as `dotnet-isolated` | Host, discovered .NET stack and optional .NET templates. No separate worker or extension bundle. Profile checks use `dotnet-isolated`. |
| Other runtime name, such as `java`, `powershell`, or `custom` | The same generic worker/bundle path. Stack and templates workloads are added only when discovery maps the name. |

Raw workload package IDs remain the responsibility of `func workload install`. An unknown setup name is currently treated as a runtime, with an optional worker at `Azure.Functions.Cli.Workloads.Workers.<runtime>` and a bundle. It is not rejected merely for being unknown. This inherited behavior can succeed without installing a stack or worker. Use `func workload search --stack` to browse the selected feed rather than treating arbitrary package IDs as features.

The canonical .NET setup name is `dotnet`, mapped to profile runtime `dotnet-isolated`. This does not enable in-process .NET. `.net` and `dotnet-inprocess` are rejected.

### Catalog discovery

Setup discovers stack packages from `kind:workload` and `alias:` tags, and templates content packages from `kind:content` and `alias:<canonical-stack>-templates`. Package IDs need not follow Microsoft's naming convention. Multiple distinct stack aliases require exactly one non-empty `stack:<canonical-alias>` tag matching an alias. A single distinct alias may omit the tag. Empty or duplicate declarations, even identical duplicates, are invalid. Alias order does not choose the runtime.

Discovery supports portable stack and templates packages. Observed `kind:rid-pointer` aliases and concrete-RID stack/templates metadata used for either role fail closed, including on offline fallback. Alias ownership spans all observed package kinds. Alternate spellings cannot bypass a contested canonical name or its restricted templates role, and aliases that fold into reserved setup feature words are rejected. Host and worker RID pointers use the installer separately, not stack discovery.

Each discovery scan requests up to 100 rows per page with independent limits of 1,000 raw rows and 32 search requests. Offsets advance by raw row count, before package-type filtering. A stable `totalHits` can establish completion. Without a total, only an empty raw page ends the scan, not a short or filtered-empty page. Reaching either limit without observing completion fails without caching the partial result. For example, 1,000 rows without a total cannot prove completion within the budget.

Malformed responses, inconsistent pagination, conflicting kinds, alias sets, or canonical stack names for repeated package IDs, invalid canonical tags, and unsupported sources fail rather than triggering offline fallback. Identical parsed rows are allowed. Transport failures use built-in maps while retaining observed conflicts and unsupported roles. A completed scan also uses built-in maps when no unambiguous canonical stack entry was collected. Collected entries later rejected for a concrete RID or reserved setup name do not trigger that fallback. The fallback contains Node, Python, Go, and .NET stacks, with templates for Node, Python, and .NET. Successful discovery does not fill missing built-in stacks from that fallback. Requesting such a missing built-in stack fails planning.

Snapshots, including transport fallbacks, are cached by source override and prerelease policy for the catalog instance. Discovery remains best effort. Offset paging is not a stable snapshot of a mutable feed, and retaining observed conflicts is not a guarantee of feed-wide alias uniqueness.

## 5. Feature Defaults

If `--features` is provided, it is the complete explicit feature request.

If `--features` is not provided:

1. If `.func/config.json` declares `stack.runtime`, setup uses that stack feature.
2. Otherwise, an interactive plain run without `--non-interactive` or `--yes` offers a stack picker. Entries with a matching package ID in the workload registry are shown as already installed and are not selectable. This is a UI hint, not a dependency health check.
3. With `--non-interactive`, `--yes`, JSON output, or an unavailable interactive terminal, setup defaults to `runtime` instead of showing the picker.

If every eligible stack is already registered, the picker path is a successful no-op. No eligible stacks is an error, as is submitting an empty selection. `--check` alone does not suppress the stack picker. Use explicit features or a non-interactive option for unattended checks.

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

Host resolution uses the literal logical package ID `Azure.Functions.Cli.Workloads.Host`, not alias/tag lookup or a hand-built RID suffix. Python workers use `Azure.Functions.Cli.Workloads.Workers.Python`. The workload installer follows each pointer's current-RID mapping at the exact pointer version and source. Other worker IDs use `Azure.Functions.Cli.Workloads.Workers.<canonical-runtime>`. Installed dependency matching accepts physical IDs and logical owner IDs.

Worker constraints apply only to workers selected by `--features` or by `.func/config.json` worker runtime. Setup does not install every worker listed by a profile.

If a selected runtime is not supported by a profile, setup fails planning for that profile. In install mode, any returned planning failure blocks all dependency installs for that profile, including otherwise valid host, bundle, and other runtime dependencies. This does not undo earlier successful profiles. Check mode still checks the planned dependencies and reports returned planning failures, as described in section 14. If the profile supports the runtime but has no worker range, worker resolution has no profile range. Stack and templates versions are not constrained by worker ranges. Workers remain optional, as described under current limitations below.

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
| All other known stack features | `DefaultStable` |
| Unknown worker runtimes | `DefaultStable` |

`NotSupported` means setup does not install or check extension bundles for that feature. A profile `extensionBundle` range does not force bundle installation for a stack that does not use bundles.

`DefaultStable` means bundle setup is included, using the stable channel unless the project explicitly selects another supported channel.

When a project `host.json` declares an extension bundle, setup intersects the `host.json` bundle version range with the selected profile range. When no project bundle declaration is available, setup uses the stable default bundle ID with the profile range, if any.

An explicit preview or experimental bundle ID also selects that channel for non-.NET templates. If the general prerelease policy is off, setup performs a separate prerelease-inclusive templates discovery without opting stacks or workers into prerelease versions. Canonical stack names must remain consistent across completed scans, and conflicts or unsupported roles observed in either scan fail planning. If the supplemental scan falls back, the normal scan's package ownership, canonical names, and known absence of templates remain authoritative. Built-in fallback IDs do not replace or fill that normal result. .NET templates remain channel-less and follow the general prerelease policy.

Bundle and channeled templates resolution falls back to stable, with a warning, when no matching version exists on the requested non-stable channel within the applicable range. A general prerelease opt-in does not change the selected bundle/templates channel.

Search metadata may describe a newer version than the selected channel. Setup validates the actual templates registry entry before reporting installation success or using an installed-version shortcut. It requires portable content with the expected effective logical alias or the legacy conventional ID. A mismatch discovered after installation is a failure with an explicit uninstall command, not an automatic rollback.

Setup and the templates consumer share one eligibility policy. Channel and portable-content filtering precede owner selection. Within that channel, the conventional effective package ID retains precedence regardless of its aliases. Otherwise a single custom alias owner is selected, and competing custom owners fail. Unrelated channels or an old non-portable row do not block usable portable templates. Before considering a prospective package, setup preserves any usable owner selected from the installed registry. Replacing that incumbent requires explicit migration in either direction between conventional and custom owners. This does not change selection precedence when both owners are already installed. The registry is checked again after installation.

## 9. Install Policy

`--install-policy` controls how aggressively setup reconciles installed state.

### `latest-compatible` (default)

For each dependency, setup resolves the maximum available version that satisfies all active constraints and ensures that version is installed.

- If a lower compatible version is installed and the catalog resolves a newer compatible version, setup installs the newer version.
- If a handled transport failure or no matching catalog version prevents resolution, an installed compatible version can satisfy the dependency as a fallback. JSON output reports `satisfied-fallback`.
- Unsupported sources and discovery configuration errors are not fallback cases.
- Without a compatible installed version, required dependencies fail. Optional workers/templates can be skipped when catalog resolution finds no matching version. Templates registry-readiness or matching-entry metadata errors still fail. Transport failures do not produce an optional skip.

### `if-needed`

For each concrete dependency, setup first checks installed state. Stack aliases still require discovery to establish package identity before reconciliation. This policy is not an offline alias-resolution mode.

- If any installed version satisfies the active constraints, setup skips installation.
- If no installed compatible version exists, setup resolves and installs the maximum available compatible version.
- If no installed compatible version exists, the same required/optional failure rules as `latest-compatible` apply.

### `--prerelease`

An explicit `--prerelease` value wins. Otherwise, a valid `FUNC_CLI_WORKLOADS_PRERELEASE` setting wins, then the CLI build determines the default (stable builds exclude prereleases, prerelease builds include them). Explicit bundle and non-.NET templates channels use the separate rules in section 8.

### `--source`

`--source` overrides `FUNC_CLI_WORKLOADS_SOURCE`, with nuget.org used when neither is set. Sources must be absolute HTTP(S) V3 service-index URLs. The client sets protocol version 3 even when the URL has no `.json` suffix, and search requires a `SearchQueryService` entry. Local directories, file URLs, and V2 feeds are not supported. A local `.nupkg` belongs in the positional argument to `func workload install`, not `setup --source`.

Source syntax is validated when discovery or dependency resolution consumes the source. A satisfied host/runtime `if-needed` run retains its installed-first shortcut even when an unused source setting is invalid. Host/runtime-only runs skip stack discovery, not all catalog or metadata access.

## 10. Check Mode

`--check` uses dependency resolution without installing or updating workloads and does not write the first-run marker, including the all-stacks-installed no-op path. Metadata caches may change, including profile and NuGet HTTP caches. It is not a filesystem-wide read-only or offline mode.

`--check` follows the selected install policy:

- With `latest-compatible`, check reports drift when an installed compatible version is older than the maximum available compatible version.
- With `if-needed`, check passes when any installed version satisfies the active constraints.
- For handled transport failures or no matching catalog version, check reports the same installed-version fallback as install mode. Configuration errors still fail.

`--check` exits with code `1` for failed dependency results or planning/configuration errors. A missing resolved target version is reported as `failed`, not `would-install`. Optional missing workers/templates can be `skipped`, so exit code `0` is not proof that every prerequisite exists or that installed payloads are intact.

Check mode continues through dependency results and returned planning failures for all selected profiles. A thrown configuration error ends the run rather than continuing to later profiles.

## 11. Interactive and Non-Interactive Behavior

The interactive flow selects stacks when no explicit or project feature is available. There is no separate setup confirmation before each dependency install.

`--non-interactive` never prompts. It fails if setup cannot decide what to install from explicit arguments and supported defaults.

`--yes` and JSON output suppress the picker and use project/runtime defaults. They do not select every offered stack. For example, with no explicit features or project stack runtime, either uses `runtime`.

Direct `setup` invocations skip the global first-run prompt. Parsed check and JSON setup invocations suppress the background CLI version check and trailing version/alias advisories, including after failures. JSON mode also suppresses setup's human prerelease hint. Telemetry ownership remains outside setup.

## 12. JSON Output

`--output json` emits newline-delimited JSON (NDJSON). Each event object with `type` and `timestamp` is serialized through the interaction service's raw line writer, bypassing console wrapping so each setup renderer event occupies one physical line. There is no separate final JSON document. Parser and bootstrap failures are outside this event contract.

The v1 event set is:

| Event | Description |
| --- | --- |
| `setup.started` | After features and profile scopes resolve. Includes features, worker runtimes, profiles, source override, install policy, check mode, and prerelease setting. |
| `profile.started` | At the start of each profile loop, with the profile name when constrained. |
| `dependency.detected` | Before a planned dependency is checked or installed. Includes type, name, package ID when applicable, version range, and profile. |
| `dependency.result` | Status, package ID when applicable, version when selected, message, and optional warning. Also used for planning failures that have no preceding `dependency.detected`. |
| `profile.completed` | Profile name, success, and failure count. |
| `setup.completed` | Success with `success: true`. Check drift produces `setup.failed` instead. |
| `setup.failed` | A failure count or configuration-error message. It can occur before `setup.started`. |
| `setup.warning` | Profile warnings, which can precede `setup.started`. |
| `setup.skipped` | Renderer event for the all-stacks-installed picker no-op. Ordinary JSON invocations bypass that picker path. |

Dependency result states are:

- `satisfied`
- `installed`
- `satisfied-fallback`
- `skipped`
- `failed`

Human-readable output should present the same decisions in plain language.

## 13. State and File Layout

Normal setup can write dependency state, metadata caches, and the first-run marker through the owning subsystems. The workload home defaults to:

```text
~/.azure-functions/
  workloads/   # Owned by the workload subsystem
  workloads.json
```

Setup does not write to:

- `.func/config.json`
- `.func/profiles.json`
- `host.json`
- `local.settings.json`

Successful normal setup marks first-run completion on a best-effort basis, including the all-stacks-installed picker no-op. Check mode never marks it. JSON install mode still can. Metadata cache locations are owned by the profile and NuGet subsystems, not restricted to the workload payload directory.

## 14. Failure Semantics

- In normal install mode, all returned planning failures are reported before installing any dependency **for that profile**. None of that profile's dependencies are installed when its plan has a failure. This is not a preflight transaction across all profiles.
- Setup permits partial completion. A re-run can reconcile remaining dependencies after the failure's cause is addressed. There is no rollback or transaction across profiles.
- With a valid plan, install mode stops at the first failed dependency. A failed profile ends the run with a non-zero exit code before later profiles are processed. Earlier dependency installs and earlier successful profiles remain installed.
- Check mode continues through returned failures, but a thrown configuration error aborts the run.
- Errors clearly state what completed and what did not, with the exact command or option to retry when possible.

### Current limitations

- Workers and templates are optional. A catalog result with no matching version is treated as missing even when versions exist but all are incompatible with the active range or policy. That inherited behavior can skip an incompatible optional worker rather than fail setup.
- Installed-state checks compare registry identities and versions. They do not validate payload files or prove that host, worker, or templates content can run.
- Setup discovery supports portable stack/templates roles, not RID pointers for those roles. The installed templates consumer also rejects matching non-portable rows. Supporting host/worker RID pointers does not imply RID templates support.
- Discovery is bounded and best effort. Neither a successful scan nor transport fallback guarantees that unobserved or concurrently changed feed aliases are unique.

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
