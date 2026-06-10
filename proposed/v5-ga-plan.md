# Azure Functions CLI v5: Path to GA

Everything we need to GA the v5 CLI, organized by milestone. Each bullet
is one issue / one PR scope (unless explicitly informational).

---

## Milestone 1 (Now: August)

GitHub milestone: [v5 Milestone 1](https://github.com/Azure/azure-functions-core-tools/milestone/104)

Themes: release CI + profiles integration, tech debt cleanup, parity
(Java + PowerShell), quickstart-as-workload, `func update`.

- Implement Java worker workload (#5319)
- Implement Java stack workload (#5320)
- Implement PowerShell worker workload (#5321)
- Implement PowerShell stack workload (#5322)
- Implement PowerShell templates workload (#5323)
- Extract `func quickstart` content from the core CLI into a workload (#5324)
- Create quickstart workload release pipeline (#5325)
- Drop `.{rid}` suffix from workload packages (host + python) (#5326)
- Implement Go templates workload (#5327)
- Design: v4 + v5 release story (parallel release, workload alignment,
  distribution channels, rollback) (#5328)
- Design: profile updates as part of release pipelines (#5329)
- Refactor templates engine out of the core CLI (#5330)
- Set up dedicated ADO pipelines for vnext (#5331)
- Stand up profiles CDN (#5332)
- Implement `func update` command (#5333)

---

## Milestone 2

Themes: parity (durable + pack + custom handlers), CLI settings / config, tooling story.

### Parity: Durable

- Implement durable workload (#5340)
- Setup durable workload CI/CD (#5341)

### Parity: `func pack`

- Design: new `func pack` experience & AZ CLI integration (#5342)

### CLI settings / config

- Design: figure out the settings story (local settings json / .env / app.settings etc.) (#5343)

### Tooling: notify partner teams (#5344)

Reach out to teams that ship tools embedding `func` so they have a head
start migrating from v4 and can raise concerns before GA. We're not
asking them to release anything; this is awareness + intake. Reference
doc: https://aka.ms/func-cli.

- Notify VS Code Azure Functions extension team + capture feedback
- Notify Visual Studio Azure Functions tooling team + capture feedback
- Notify Azure CLI (`az functionapp`) team + capture feedback
- Notify Rider Azure Toolkit team + capture feedback
- Notify IntelliJ Azure Toolkit team + capture feedback
- Triage any concerns raised into follow-up issues before GA

---

## Milestone 3

Themes: Kubernetes/KEDA decision, workload ownership migration, `func
doctor`, schema store publishing.

### Investigate Kubernetes / KEDA usage in v4 (#5345)

- Pull telemetry on `func kubernetes` + `func keda` usage in v4
- Decision: ship a v5 Kubernetes/KEDA workload, or drop?
- If shipping: open implementation issues (kubernetes deploy/delete,
  keda install/remove, scaler helpers, workload packaging + CI)

### Workload ownership + migration to owner repos (#5346)

- Document workload publisher onboarding (per-repo CI + feed publishing)
- Move .NET templates workload to templates repo
- Move Node templates workload to templates repo
- Move Python templates workload to templates repo
- Move Node worker workload to node-worker repo
- Move Python worker workload to python-worker repo
- Move Java worker workload to java-worker repo
- Move PowerShell worker workload to pwsh-worker repo
- Move Host workload to host repo

### `func doctor` (#5347)

Implement func doctor command.

- Some troubleshoot area ideas:
  - dotnet SDK check
  - workload manifest health check
  - Azurite availability check
  - port conflict check
  - workload package cache check
- Integrate with [`azure-functions-skills`](https://github.com/Azure/azure-functions-skills):
  the skills repo already ships a doctor command that runs LLM-driven
  skills to validate config + code (~53% of incoming ICMs could be
  prevented by keeping these skills current). Decide whether
  `func doctor` shells out to the skills runtime, or whether the skills'
  doctor calls into `func doctor` for the deterministic checks.

### Schema store (#5348)

Publish all defined schemas in the new CLI.

- Publish `workload.json` schema
- Publish `workloads.json` schema
- Publish profile schema
- Publish .func/config.json schema
- Audit vnext for other authored configs and publish them
- Add `$schema` references in samples and docs

### Telemetry & diagnostics (#5349)

- Telemetry vendor / pipeline decision
- Telemetry opt-in/opt-out wiring
- Honor `DO_NOT_TRACK`
- Per-command timing + outcome instrumentation
- PII scrubbing helpers
- Crash reporting: unhandled exception capture
- Crash reporting: stack scrubbing
- `--verbose` audit across all commands
- Consistent log scope IDs per command

---

## Future milestones / backlog

Not yet assigned to a milestone. Grouped by area for easy sorting.

### Docs (epic: #5350)

#### External

- learn.microsoft.com Functions Core Tools refresh for v5
- Rebrand user-facing strings to "Azure Functions CLI"
- Update Azure Functions docs sample snippets to v5

#### In-Repo

- README rewrite for v5
- CONTRIBUTING refresh
- repo-structure.md refresh
- building-a-workload.md refresh
- v4 -> v5 command map (in-repo)

#### UX

- `--help` audit: every command has a one-line description (#5351)
- `--help` audit: every option has a one-line description (#5351)
- Error messages include next-step hints (#5352)
- `func completion bash`
- `func completion zsh`
- `func completion pwsh`
- `func completion fish`

### Quality

E2E testing and performance.

- E2E testing (#5353)
- Performance budget: define cold-start target (#5354)
- Performance budget: enforce in CI (#5354)

### Parity Audit (epic: #5355)

#### Languages (audit matrix)

- Living parity matrix doc (per-language v4 vs v5)
- .NET in-proc parity audit
- .NET isolated parity audit
- Node parity audit
- Python parity audit
- PowerShell parity audit
- Java parity audit
- Go parity audit

#### Tools / extensions

- Extension bundles parity audit
- KEDA / Kubernetes tooling parity audit
- Azure Container Apps tooling parity audit
- Custom handlers parity audit
- Worker indexing model parity (v4 -> v5)

### Migration & v4 deprecation

- v4 -> v5 migration guide content (#5356)
- v4 -> v5 command map (companion to in-repo docs) (#5356)
- v4 -> v5 behavior diffs catalog (#5356)
- v4 -> v5 known gaps page (#5356)
- `func --version` v4-detected migration pointer (#5357)
- `local.settings.json` schema compatibility audit (#5358)
- `host.json` schema compatibility audit (#5358)
- Document any breaking schema changes (#5358)
- v4 deprecation timeline (public announcement) (#5359)
- v4 security-only support window decision (#5359)
- v4 archive / sunset date decision (#5359)

### Security (#5360)

- Token handling review
- Workload package signature verification
- NuGet feed pinning
- Supply-chain audit
- Threat modeling

### Localization (#5361)

- Decision (in-scope for GA or defer)
- Implementation (if in-scope)

### Accessibility (#5362)

- NO_COLOR audit
- Non-TTY audit
- Screen-reader friendliness via `IInteractionService`

### Release process (GA cutover) (#5363)

- Switch default branch (vnext -> main, or rename)
- Cut `v5.0.0` tag
- Comprehensive v5.0.0 release notes (full delta from v4 latest)
- Issue templates refresh
- Triage process documented
- Post-GA 30-day SLA decision + comms

---

## Release Process Notes

Everything related to shipping v5 to users. Most of these are
informational (decisions, channel coverage, comms) rather than discrete
issues. Pulled out into issues only when they need active work.

### Distribution: installer (info)

Installer scripts already exist (`install.sh`, `install.ps1`). Coverage
to validate before GA: Windows x64, Windows arm64, macOS x64, macOS arm64,
Linux x64, Linux arm64.

### Distribution: package channels (info)

Channels to make a per-channel decision on before GA:

- Homebrew
- npm (keep / deprecation-pointer / drop)
- winget
- Chocolatey
- Scoop
- apt
- rpm

### Distribution: signing (info)

- Authenticode signing for Windows binaries in release pipeline
- macOS notarization for the published archive
- Documented signature verification for users

#### Release story

- v4 + v5 parallel cadence + branding split
- Side-by-side install story across channels
- Bug-fix backport policy v5 -> v4 during parallel window
- Coordinated release notes across v4 and v5
- Define rollback per distribution channel
- Per-channel rollback playbook (npm, brew, winget, choco, scoop, apt,
  rpm, installer scripts, CDN profiles)
- Workload rollback story (unlist/yank without breaking older CLIs)
- Profiles rollback (pinned previous-version URLs + CDN cache purge)
- Incident comms template
- Base CLI / workload version compatibility matrix
- Coordinated release tagging across owner repos
- Aggregate workload release notes into CLI release notes
- Cross-repo release readiness checklist
- Channel-by-channel release-day checklist
- Cross-channel version-bump automation
- Release-day per-channel status tracker
