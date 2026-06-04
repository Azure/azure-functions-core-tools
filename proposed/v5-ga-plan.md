# Azure Functions CLI v5: Path to GA

Everything we need to GA the v5 CLI, grouped by area. Each bullet is an
independently shippable work item (one issue / one PR scope).

---

## Workloads: Language workers & templates

### PowerShell

- Powershell stack workload
- PowerShell worker workload
- PowerShell templates workload

### Java

- Java stack workload
- Java worker workload
- Java templates workload

### Go

- Go templates workload

### Quickstart

- Extract `func quickstart` content from thin CLI into a workload
- Quickstart workload publish pipeline (CI + feed)

### Worker package layout

- Host package per-RID layout cleanup (drop `.{rid}` suffix)
- Python worker per-RID layout cleanup (drop `.{rid}` suffix)
- Document the per-RID worker package convention

## Workloads: Optional / advanced

### Durable

- `func durable get-history`
- `func durable get-instances`
- `func durable get-runtime-status`
- `func durable purge-history`
- `func durable raise-event`
- `func durable rewind`
- `func durable start-new`
- `func durable terminate`
- `func durable delete-task-hub`
- Durable workload packaging + CI

### Kubernetes / KEDA

- `func kubernetes deploy`
- `func kubernetes delete`
- `func keda install`
- `func keda remove`
- KEDA scaler config generation helpers
- Kubernetes workload packaging + CI

### Azure Container Apps

- Drop support in v5

## Workloads: Ownership migration

Move each workload to its source-of-truth repo so it ships with the thing it
wraps.

- Move .NET templates workload to templates repo
- Move Node templates workload to templates repo
- Move Python templates workload to templates repo
- Move Node worker workload to node-worker repo
- Move Python worker workload to python-worker repo
- Move Java worker workload to java-worker repo
- Move PowerShell worker workload to pwsh-worker repo
- Move Host workload to host repo?
- Document workload publisher onboarding (per-repo CI + feed publishing)

---

## Commands: Local development parity (from `main`)

### `func settings`

- `func settings add`
- `func settings list`
- `func settings delete`
- `func settings encrypt`
- `func settings decrypt`

### `func extensions`

- `func extensions install`
- `func extensions sync`

### Misc

- `func templates list`: surface installed template metadata
- `func logs`: host log streaming
- `func azurite` lifecycle polish (start / stop / status UX)

## Commands: `func pack`

- `func pack` (custom / generic)
- `func pack dotnet`
- `func pack node`
- `func pack python`
- `func pack powershell`
- `func pack go`
- Pack options + validation helpers (shared)

## Commands: Diagnostics (`func doctor`)

- `func doctor` core scaffold
- `func doctor`: dotnet SDK check
- `func doctor`: workload manifest health check
- `func doctor`: Azurite availability check
- `func doctor`: port conflict check
- `func doctor`: workload package cache check

## Commands: CLI configuration (`func cli settings`)

- Storage location + schema decision
- `func cli settings get`
- `func cli settings set`
- `func cli settings unset`
- `func cli settings list`

## Commands: `func azure` group

Need to discuss if we want a func azure workload or to invest in a better story for using az cli and azd instead

- ADR: `func azure` group home (thin CLI vs workload)
- `func azure login` / auth strategy (DefaultAzureCredential vs az shell-out)
- `func azure publish` (function app publish)
- `func azure fetch-app-settings`
- `func azure add-storage-account-setting`
- `func azure list-functions`
- `func azure log-stream`

---

## Infrastructure: Templating

- ADR: templating engine location (separate package vs per-workload vs
  shared library). Blocks Go/durable/k8s workloads.
- Prototype the chosen templating engine approach

## Infrastructure: Profiles CDN

- Pick CDN + path scheme
- Publishing pipeline
- Versioning + cache-bust strategy
- Switch `func setup` / profile commands to CDN

## Infrastructure: Schema store

- Publish `workload.json` schema
- Publish `workloads.json` schema
- Publish profile config schema
- Audit vnext for other authored configs and publish them
- Add `$schema` references in samples and docs

## Infrastructure: CI / ADO pipelines

- Dedicated ADO **public-build** pipeline for vnext (stop depending on the
  `main` branch pipeline)
- Dedicated ADO **official/signed-build** pipeline for vnext
- vnext PR validation triggers (branch + path filters)
- Document vnext pipeline ownership + GA rollover plan (what happens when
  vnext becomes main)

---

## Distribution: Installer

- Installer E2E: Windows x64
- Installer E2E: Windows arm64
- Installer E2E: macOS x64
- Installer E2E: macOS arm64
- Installer E2E: Linux x64
- Installer E2E: Linux arm64

## Distribution: Package channels

- Homebrew formula update for v5
- npm package policy decision (deprecation pointer vs full install)
- npm package implementation per chosen policy
- winget manifest
- Chocolatey package
- Scoop manifest
- apt repo (or document install.sh as the apt path)
- rpm repo (or document install.sh as the rpm path)

## Distribution: Signing

- Authenticode signing for Windows binaries in release pipeline
- macOS notarization for published archive
- Document signature verification for users

## Distribution: Updates

- `func --version` opt-in new-version-available check
- `func update` command (or documented installer re-run pointer)
  - Currently in progress

---

## Telemetry & diagnostics

- Telemetry vendor / pipeline decision
- Telemetry opt-in/opt-out wiring via `func cli settings`
- Honor `DO_NOT_TRACK`
- Per-command timing + outcome instrumentation
- PII scrubbing helpers
- Crash reporting: unhandled exception capture
- Crash reporting: stack scrubbing
- `--verbose` audit across all commands
- Consistent log scope IDs per command

---

## Docs: External

- learn.microsoft.com Functions Core Tools refresh for v5
- Rebrand user-facing strings to "Azure Functions CLI"
- Update Azure Functions docs sample snippets to v5

## Docs: In-repo

- README rewrite for v5
- CONTRIBUTING refresh
- repo-structure.md refresh
- building-a-workload.md refresh
- v4 → v5 command map (in-repo)

## Docs: UX

- `--help` audit: every command has a one-line description
- `--help` audit: every option has a one-line description
- Error messages include next-step hints
- `func completion bash`
- `func completion zsh`
- `func completion pwsh`
- `func completion fish`

---

## Quality

- E2E CI: install → init → start → publish for .NET
- E2E CI: Node
- E2E CI: Python
- E2E CI: PowerShell
- E2E CI: Java
- E2E CI: Go
- Performance budget: define cold-start target
- Performance budget: enforce in CI

---

## Migration & v4 deprecation

- v4 → v5 migration guide content
- v4 → v5 command map (companion to in-repo docs item)
- v4 → v5 behavior diffs catalog
- v4 → v5 known gaps page
- `func --version` v4-detected migration pointer
- `local.settings.json` schema compatibility audit
- `host.json` schema compatibility audit
- Document any breaking schema changes
- v4 deprecation timeline (public announcement)
- v4 security-only support window decision
- v4 archive / sunset date decision

---

## Hardening

### Security

- Token handling review
- Workload package signature verification
- NuGet feed pinning
- Supply-chain audit

### Localization

- Decision (in-scope for GA or defer)
- Implementation (if in-scope)

### Accessibility

- NO_COLOR audit
- Non-TTY audit
- Screen-reader friendliness via `IInteractionService`

---

## Release process

- Switch default branch (vnext → main, or rename)
- Cut `v5.0.0` tag
- Comprehensive v5.0.0 release notes (full delta from v4 latest)
- Issue templates refresh
- Triage process documented
- Post-GA 30-day SLA decision + comms

---

## Cross-cutting / not yet bucketed

- Third-party command extensibility (plugin model beyond workloads)
- Extension bundles management UX: dedicated `func bundles` group?
- Workload feed strategy: public NuGet only vs dedicated feed (affects
  offline / airgapped story)

---

## Open questions

1. Templating engine location (blocks Go/durable/k8s workloads).
2. Workload ownership migration sequencing (templates first, then workers,
   or all at once?).
3. `func azure` group: thin CLI or workload?
4. Telemetry vendor / pipeline.
5. Distribution package matrix: GA-required vs post-GA.
