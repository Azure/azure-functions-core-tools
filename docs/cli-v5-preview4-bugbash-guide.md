# Azure Functions CLI v5 Preview 4 Bug Bash

## Preview status

**PROVISIONAL: this guide targets a source candidate, not a final release artifact.**
The Preview 4 release cut is planned for October 20, 2026, at approximately
9:00 AM Pacific (16:00 UTC). Synchronize this guide with the source selected for
that build and its release receipt before marking the documentation PR ready
for review.

| Boundary | Source identity |
| --- | --- |
| Preview 3, `v5.0.0-preview.3` | `f8f54ba328758b2165d835930e391af23e60f090` |
| Provisional Preview 4 | `33eb0b5389f56e5ea872a63420c98b2b04b8e260` |
| Final Preview 4 | **PENDING RELEASE CUT** |

The [provisional comparison][candidate-delta] contains 26 commits after Preview 3.
The candidate source still declares `5.0.0-preview.3`; that is not a Preview 4
artifact identity. Do not substitute it for `<PREVIEW4_VERSION>`.

No claim is made that runtime qualification has completed. Replace every
`<PLACEHOLDER>` below with an approved receipt value before running commands.
Missing installer, artifact, package, profile, or fixture inputs mean **BLOCKED**,
not permission to substitute an unrelated build or the latest available package.

## What's new since Preview 3

### Installation and upgrade

- **P0 - Correct archive selection:** installers request `func-<rid>.zip` on
  Windows and `func-<rid>.tar.gz` on Unix from the versioned CDN directory
  ([#5622](https://github.com/Azure/azure-functions-core-tools/pull/5622)).
- **P0 - Unix wrapper recreation:** both Bash and PowerShell-on-Unix explicitly
  normalize the wrapper to `0755`, including force publication that removes and
  recreates it. Restrictive umask does not determine the successful final mode
  ([#5701](https://github.com/Azure/azure-functions-core-tools/pull/5701)).
- **P0 - Publication and recovery:** installers inspect staged payloads, back up
  replaced entries, publish installation metadata, attempt restoration on
  publication failure, and retain recovery evidence if restoration is incomplete.
- **Exploratory - npm:** distribution work targets both `azure-functions-cli`
  and `azure-functions-core-tools`. Actual package/version publication is not
  verified by this guide. The package requires Node >=22 and does not establish a
  `func5` side-by-side contract
  ([#5669](https://github.com/Azure/azure-functions-core-tools/pull/5669)).

### Setup and workloads

- **P0 - Setup automation/lifecycle:** `--yes`, JSON output, and noninteractive
  operation suppress the stack picker. Setup NDJSON records avoid terminal
  wrapping; check/JSON invocations suppress trailing update/alias advisories.
  Exercise cancellation, completion markers, partial failures, and multiple
  profiles ([#5604](https://github.com/Azure/azure-functions-core-tools/pull/5604)).
- **P0 - Host acquisition:** RID-specific Host packages place payloads under
  `tools/<rid>`. Qualify the independently versioned Host pointer and selected
  implementation, not just the CLI binary
  ([#5614](https://github.com/Azure/azure-functions-core-tools/pull/5614)).
- **P1 - Profile rejection:** unsupported runtime/profile combinations reject
  before worker acquisition. Python is excluded from Windows target profiles;
  Go is excluded outside Flex
  ([#5507](https://github.com/Azure/azure-functions-core-tools/pull/5507)).
- Replay workload install/list/search/update/uninstall/prune with pinned packages,
  exact source/RID selection, ownership checks, and disposable state.

### Init / new / quickstart

- **P0:** init/quickstart force cleanup respects confirmation and cancellation.
  When input is available, confirmation defaults to No; when input is unavailable,
  explicit `--force` authorizes cleanup. Test only disposable directories.
- **P1:** incompatible bundle resolution blocks function creation without generated
  writes; read-only template listing remains available
  ([#5580](https://github.com/Azure/azure-functions-core-tools/pull/5580)).
- Retain existing-provider scaffolding, adoption/healing, template selection,
  quickstart, and local start/invocation regression scenarios.

### Terminal/noninteractive reliability

- Prompt and dashboard input decisions use the relevant console capabilities
  ([#5581](https://github.com/Azure/azure-functions-core-tools/pull/5581),
  [#5582](https://github.com/Azure/azure-functions-core-tools/pull/5582)).
- Exercise stdout/stderr redirection, unavailable stdin, explicit input, plain
  output, `NO_COLOR`, and cancellation without assuming all these environments
  have identical prompt capabilities.

### Internal/qualification work

Template context/factory/catalog/parameter/constraint and stack-metadata work is
foundation work, not template-first init or a complete template/package lifecycle.
Updater reliability/progress/metadata foundations do not make `func update`
operational.

[#5732](https://github.com/Azure/azure-functions-core-tools/pull/5732) is
**TEST-ONLY / DOCUMENTATION**, not a CLI feature. It adds isolated process/inventory
checks and optional Preview 3 empty-inventory replay on Windows x64. See
[CLI process smoke tests](../CONTRIBUTING.md#cli-process-smoke-tests). The optional
replay needs a pre-provisioned, digest-verified archive; absent that input it does
not qualify Preview 3. Neither configured coverage nor a project-output test
qualifies the final Preview 4 release artifact.

## Installation instructions

Use the approved installer path and record its source/hash separately from the
CLI artifact. A version pin does not pin mutable installer bytes. These commands
are for approved test environments after the receipt is populated; they have not
been executed as part of preparing this guide.

### Clean installation

Use an empty, test-owned installation directory. The default is shown below;
choose another explicit directory if that location already contains an install.
Do not delete existing state to make the clean-install scenario pass.

**PowerShell / Windows**

```powershell
$P4Version = '<PREVIEW4_VERSION>'
$Installer = '<APPROVED_INSTALLER>'
$InstallPath = Join-Path $HOME '.azure-functions'

& $Installer -Version $P4Version -InstallPath $InstallPath -Verbose
& (Join-Path $InstallPath 'func.exe') --version
```

**Bash / Linux or macOS**

```bash
P4_VERSION='<PREVIEW4_VERSION>'
INSTALLER='<APPROVED_INSTALLER>'
INSTALL_DIR="$HOME/.azure-functions"

bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --verbose
"$INSTALL_DIR/func" --version
```

Use the PowerShell or Bash installer appropriate to the example. The directly
addressed binary must report the receipt's version. Exit status alone is not
sufficient: the Bash existing-install guard can exit zero without replacing it.

### Upgrade from earlier v5 Preview

Provision the approved Preview 3 installation first and record its version,
installer, wrappers, paths, and workload state. Use the same installation directory
and the candidate's approved installer. This is a separate scenario from clean install.

```powershell
& $Installer -Version $P4Version -InstallPath $InstallPath -Force -Verbose
& (Join-Path $InstallPath 'func.exe') --version
```

```bash
bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --force --verbose
"$INSTALL_DIR/func" --version
```

**Force permits replacement. Force does not select a newer version.**
Explicit version selection bypasses manifest selection. If the CDN manifest still
selects Preview 3, an unpinned `--prerelease` / `-Prerelease` install can select
Preview 3 again, even with force. A pin cannot make an unpublished artifact exist.
Live manifest and served-installer identities remain release-receipt inputs.

### Side-by-side v4/v5 and version verification

Verify the directly addressed v5 binary first, then refresh PATH and inspect wrappers.
Subsequent `func5` examples assume that resolution has been verified; otherwise
invoke the verified v5 native binary by its full path.

```powershell
Get-Command func, func5 -All
func --version
func5 --version
func5 --verbose
```

```bash
type -a func func5
func --version
func5 --version
func5 --verbose
```

The scripts preserve an external `func` as the default when adding PATH and provide
a `func5` wrapper for v5. Record v4 before and after; detection of an external command
does not prove its version. Windows provides cmd/PowerShell and extensionless Bash
wrappers. Do not extend this script guarantee to npm, whose bin map has no `func5`.

**Unix refresh:** start a new shell, or source the exact profile reported by the
installer using the appropriate shell. Bash may also need `hash -r`. A subprocess
cannot update its parent shell. PowerShell-on-Unix writes a shell profile; do not
assume immediate PATH repair in the current PowerShell session.

**Windows refresh:** the installer updates process and user PATH when adding the
entry. Persisted PATH and an already-running terminal's inherited environment can
differ. Use full-path verification and refresh the session/terminal parent when
needed. `-SkipPath` / `--skip-path` deliberately suppress updates.

### Unix force-upgrade qualification

Run each case for Bash and PowerShell-on-Unix in disposable installations:

1. Normal clean creation.
2. Forced upgrade from the approved earlier preview.
3. Force upgrade under `umask 077`.
4. Remove only the wrapper from a disposable installation, then force recreation.
5. Set the disposable wrapper to restrictive mode `0700`, then force replacement.

For the restrictive-umask case, using the variables above:

```bash
(umask 077; bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --force)
```

For PowerShell-on-Unix, substitute the approved PowerShell script:

```bash
PS_INSTALLER='<APPROVED_INSTALLER>'
(umask 077; pwsh -NoLogo -NoProfile -File "$PS_INSTALLER" -Version "$P4_VERSION" -InstallPath "$INSTALL_DIR" -Force)
```

Expected successful wrapper mode is **0755**, independent of prior wrapper mode
and effective umask. Both paths explicitly normalize it after recreation. Inspect it:

```bash
stat -c '%a' "$INSTALL_DIR/func5" 2>/dev/null ||
    stat -f '%Lp' "$INSTALL_DIR/func5"
```

Expect `755`. Compare directly addressed native and wrapper version/help output,
exercise argument forwarding, and confirm v4 `func` remains the expected command.
Record native-binary and parent-directory permissions too: wrapper mode alone does
not prove shared-directory accessibility.

The [installer harness](../test/Installers/InstallationMetadata.Tests.ps1) explicitly
covers force recreation under `umask 0077` for both Unix paths. Clean-under-077 and
pre-existing-0700 cases are additional bug-bash checks, not claimed dedicated harness cases.

### Installation metadata

Successful script publication writes `func.installation.json` beside the native
binary with exactly these fields:

```json
{"schemaVersion":1,"updatePolicy":"in-place"}
```

Expect UTF-8 without BOM, one terminating LF, and Unix mode **0644**. The policy
marker is not proof that `func update` works or that final artifact qualification
has passed. Retain its contents and mode with installation evidence.

### Failure/recovery

The candidate inspects staged payloads and backs up replaced entries before
publication. Publication failures attempt restoration; incomplete restoration
reports failure and retains a recovery location. Invalid executable payloads,
reserved metadata collisions, and destination links/reparse points are rejected.

Preserve the reported error and recovery files before resetting anything.
Use the installer harness for controlled failure injection rather than corrupting
published assets or a working installation. Its synthetic payload checks do not
replace final-artifact installation qualification.

Metadata replacement is atomic, but the complete installer, PATH update, and shell
profile modification are **not one globally atomic transaction**. Do not promise
successful recovery from every interruption or post-publication PATH failure.
`-KeepArchive` / `--keep-archive` can retain download/staging evidence.

### Clean tester reset

Prefer a new test-owned install directory and new empty state homes over deletion:

```powershell
$env:FUNC_CLI_HOME = '<NEW_TEST_OWNED_CLI_HOME>'
$env:FUNC_CLI_WORKLOADS_HOME = '<NEW_TEST_OWNED_WORKLOAD_HOME>'
```

```bash
export FUNC_CLI_HOME='<NEW_TEST_OWNED_CLI_HOME>'
export FUNC_CLI_WORKLOADS_HOME='<NEW_TEST_OWNED_WORKLOAD_HOME>'
```

These relocate state, not installer binaries; choose `-InstallPath` / `--install-path`
separately. For a default-layout reset, stop tester-owned processes and move the
verified v5 directory to a unique backup location before reinstalling. Preserve
relocated state explicitly. Do not delete projects, v4 installs, broad home
directories, or shared NuGet caches. There is no installer reset switch.

## Workload-source instructions

Runtime catalog precedence is:

1. Command `--source`.
2. `FUNC_CLI_WORKLOADS_SOURCE`.
3. Default `https://api.nuget.org/v3/index.json`.

Use the receipt's HTTP(S) V3 service-index URL. Repository NuGet restore
configuration does not automatically configure the installed CLI. A local directory
is not a V3 catalog source; a local package file is passed positionally to
`func5 workload install`.

**PowerShell**

```powershell
$WorkloadSource = '<APPROVED_WORKLOAD_SOURCE>'
$HostVersion = '<HOST_WORKLOAD_VERSION>'
$env:FUNC_CLI_WORKLOADS_SOURCE = $WorkloadSource
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version $HostVersion --prerelease --source $WorkloadSource
func5 workload list --all-versions --json
```

**Bash**

```bash
WORKLOAD_SOURCE='<APPROVED_WORKLOAD_SOURCE>'
HOST_VERSION='<HOST_WORKLOAD_VERSION>'
export FUNC_CLI_WORKLOADS_SOURCE="$WORKLOAD_SOURCE"
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version "$HOST_VERSION" --prerelease --source "$WORKLOAD_SOURCE"
func5 workload list --all-versions --json
```

The Host package version is independent of the CLI version. Verify logical and
physical package identities, selected RID/version/source, and launchability.
Installing Host alone is not full language setup.

## Setup instructions

Example for .NET isolated, after wrapper resolution and source configuration:

```powershell
func5 setup --features dotnet --source $WorkloadSource --prerelease --non-interactive
```

```bash
func5 setup --features dotnet --source "$WORKLOAD_SOURCE" --prerelease --non-interactive
```

- `--yes` skips the picker; it does not install every stack.
- Without explicit features or configured runtime, suppressed/unavailable picker
  input defaults to host plus extension bundle. Use explicit `node`, `python`,
  `go`, or `dotnet` features for those defined setup paths.
- `.NET` here means isolated. Java/PowerShell use generic runtime handling rather
  than complete stack/template expansion.
- Success does not prove every SDK, worker, template, or payload is ready.
  Optional dependencies can be skipped. Follow setup with actual selected-stack
  creation, start, and local invocation.
- General prerelease selection does not automatically select every preview
  bundle/template channel.
- For automation, add `--check --output json` to check without installing.
  Expect setup NDJSON events, not one final JSON document. Metadata caches can
  change; check mode is not globally read-only. Parser/bootstrap errors do not
  inherit the setup renderer's JSON guarantee.

## Preview 4 testing matrix

These are **provisional testing dispositions**, not broader product-support or
artifact-qualification commitments. Final combinations require the release receipt.

### Expected Preview 4 coverage

| Surface | Coverage and qualification boundary |
| --- | --- |
| Windows x64 / arm64 | Native CLI installation and launch on each selected RID; workload support is separate. |
| Linux x64 / arm64, glibc | Native installer, Host acquisition and approved language lifecycle. |
| macOS x64 / arm64 | Native install/launch plus final signing/notarization evidence. |
| PowerShell installer; Bash installer on Unix | Clean, pinned, forced, wrapper, metadata and recovery checks. |
| Clean install; force upgrade; v4/v5 side-by-side | Verify binary and wrapper identities, modes, PATH and preserved v4 routing. |
| .NET isolated; Node; Python | Approved SDK/runtime/package/profile combinations only. |
| Setup | Explicit/default selection, automation, JSON/check, cancellation and partial failure. |
| Workloads | Install/list/search/update/uninstall/prune, exact identities and ownership; use disposable state. |
| Init / new / quickstart | Existing-provider creation, adoption/healing, selection and failure behavior. |
| Start / profiles | Local start/invoke/stop, valid combinations and early rejection. |
| Interactive; redirected/noninteractive | Actual capability combinations, required inputs, output contracts and cancellation. |

### Special attention

| Surface | Required attention |
| --- | --- |
| Restrictive umask | Corrected `0755` contract is included; qualify both Unix paths and all wrapper variants above. |
| Installer rollback/recovery | Exercise publication failure and incomplete restoration; retain recovery evidence. |
| Installer identity versus CLI artifact | Record both; pinning CLI version does not pin served script bytes. |
| Windows PATH/session | Distinguish persisted PATH from inherited process state. |
| Setup success versus readiness | Check optional skips, SDKs, real payloads and invocation. |
| Profile/runtime exclusions | Target-SKU restrictions are not the same as local developer OS support. |
| CDN installers | Confirm approved script publication, pinned assets and manifest behavior at release cut. |

### Exploratory

| Surface | Scope |
| --- | --- |
| PowerShell language; Java | Manually pinned approved workloads and runtimes; no full setup/templates promise. |
| Go | Approved Flex-oriented fixture, user-built executable; no templates setup expansion. |
| npm | Only if exact packages are published; isolated command ownership; Node >=22. |
| Template/constraint foundations | Component exploration, not a completed customer command lifecycle. |
| Updater-service foundations | Harness coverage only, not operational `func update`. |

### Unsupported / deferred

- Operational `func update`; use the approved installer for upgrade.
- .NET in-process setup and Python Windows arm64 worker.
- Python in Windows target profiles and Go outside Flex profiles.
- Native Windows Bash installer; unsupported architectures beyond x64/arm64;
  musl/Alpine as qualified native coverage.
- Template-first init and a complete new template/package lifecycle.
- npm `func5` side-by-side guarantee.
- F# quickstart through the current C#-only .NET quickstart provider; this does not
  exclude F# init.

## Core bug-bash scenarios

Use disposable, receipt-defined fixtures. Capture command, exit code, stdout/stderr,
identities, and before/after state for every row using [Result reporting](#result-reporting).
Use paths outside force-cleanup directories for logs. Do not deploy to Azure as part
of these local scenarios. Stop only tester-owned processes.

### P0

| ID / prerequisites | Command or action | Expected behavior | Additional evidence | Failure/regression signal |
| --- | --- | --- | --- | --- |
| P0-01 Clean install: empty test-owned install path, approved installer/artifact | Run the pinned clean-install instructions for the selected OS/RID. | Correct directly addressed version, wrapper and metadata; successful modes match this guide. | Installer/artifact identities, native/wrapper paths, metadata and modes. | Wrong version/RID, missing wrapper/marker, false success. |
| P0-02 Upgrade: approved Preview 3 install with recorded state | Run the forced-upgrade instructions against the same path. | Candidate replaces earlier CLI; wrapper/marker match candidate contract; unrelated workload state preserved. | Before/after versions, files, marker, modes and workload inventory. | Stale binary, lost unrelated state, misleading success. |
| P0-03 Unix modes: disposable Bash and PowerShell-on-Unix installs | Run all five Unix qualification cases above, including force under `umask 077`. | Wrapper `0755`; correct adjacent executable and argument forwarding. | Umask, wrapper/native/parent modes, native versus wrapper output. | Owner-only/missing wrapper or incorrect forwarding. |
| P0-04 Side-by-side: working v4 already resolved | Record `func --version`, install v5, refresh, inspect actual resolution and `func5 --version`. | Expected v4 remains `func`; approved v5 is `func5`. | Both identities and all command-resolution candidates. | v4 default displaced or v5 resolving elsewhere. |
| P0-05 Host: empty workload home, approved source and package/RID receipt | Install Host and inspect `func5 workload list --all-versions --json`; add receipt-defined stack/worker/bundle prerequisites without changing the selected Host, then run `func5 start "<PROJECT>" --output plain` for the fixture. | Exact logical/physical RID/version/source; Host launches with the approved fixture prerequisites. | Package/RID identities, payload layout, startup output. | Wrong/missing implementation or launch failure. |
| P0-06 Setup: explicit-feature, configured-runtime and empty-project fixtures | Run setup with `--non-interactive`; separately exercise `--yes` and `--check --output json`, including missing dependencies, cancellation and multiple profiles. | No picker in automation; correct defaults; one physical line per NDJSON event; no check-mode completion marker or false completion. | JSON records, marker/config snapshots, per-profile outcomes. | Prompt/hang, wrapped JSON, advisory contamination, false success/marker. |
| P0-07 Force safety: disposable populated directories and approved templates | Exercise `func5 init "<PROJECT>" --stack node --language JavaScript --force` and `func5 quickstart "<QUICKSTART_PROJECT>" --stack node --language JavaScript --template "<QUICKSTART_ID>" --force`; accept, decline, cancel, then test unavailable stdin separately. | Available-input confirmation defaults No; decline/cancel preserves sentinels; explicit force authorizes cleanup when input unavailable. | Before/after sentinel/config snapshots and prompt/cancellation output. | Unexpected deletion, cleanup after cancellation, or hang. |
| P0-08 Redirected console: runnable fixture and complete required input | Redirect stdout/stderr and vary stdin/capabilities for setup/init/new/quickstart/start; include `func5 start "<PROJECT>" --output plain` and dashboard cancellation. | Appropriate prompts/plain fallback, no inappropriate dashboard input loop; local run stops cleanly. | Redirection/capability settings, separate streams, exit codes. | Key-reader exception, unusable prompt, ANSI/output corruption or hang. |

### P1

| ID / prerequisites | Command or action | Expected behavior | Additional evidence | Failure/regression signal |
| --- | --- | --- | --- | --- |
| P1-01 Bundle gate: compatible, incompatible, malformed and missing-bundle fixtures | Run `func5 new "<PROJECT>" --list`; use listed ID with `--template "<TEMPLATE_ID>" --name BugBash --non-interactive` and receipt-defined required options. | Incompatible creation exits 1 with no generated writes; compatible creation succeeds; listing remains available. | Bundle constraints/versions, file snapshots and diagnostic. | Incompatible scaffolding, false success, or blocked listing. |
| P1-02 Profiles: Python and Go negative fixtures plus valid controls | Run `func5 start "<PROJECT>" --profile windows-consumption` for Python; test Go with a non-Flex profile and approved valid combinations. | Unsupported combinations reject before worker acquisition; valid controls continue. | Profile/runtime and before/after worker inventory. | Acquisition before rejection or invalid acceptance. |
| P1-03 Workloads: separate pinned package/ownership/multi-version fixtures | Exercise install and list above; `func5 workload search --source "<APPROVED_WORKLOAD_SOURCE>" --prerelease --json`; `func5 workload update "<WORKLOAD_ID>" --exact --source "<APPROVED_WORKLOAD_SOURCE>" --prerelease`; `func5 workload prune "<WORKLOAD_ID>" --exact`; `func5 workload uninstall "<WORKLOAD_ID>" --exact --all-versions`. | Exact source/identity/ownership; update respects default major boundary; prune retains highest installed version; uninstall scope is correct. | JSON inventory before/after each independently seeded operation. | Wrong package/version/source, ownership loss, unintended removal. |
| P1-04 Lifecycle: approved SDK, packages, templates and quickstart catalog | `func5 init "<PROJECT>" --stack node --language JavaScript`; list/select/create with new; `func5 quickstart list --stack node --language JavaScript`; scaffold selected ID into a separate path; start, invoke its documented local endpoint, then stop. Repeat approved .NET/Python paths and adoption/healing. | Expected minimum semantic project/function outputs, configuration preservation, successful local invocation and cleanup. | Template/catalog IDs, generated/config diffs, startup and local request results. | Existing-provider regression, overwritten config, unrunnable output or leaked process. |
| P1-05 Recovery: disposable candidate checkout and synthetic failure fixtures | Use the installer harness for download/invalid-payload, wrapper/metadata publication, destination-link and restoration failures; separately qualify real approved artifacts. | Nonzero failure, prior state preserved/restored where applicable, retained recovery location on incomplete restoration. | Error, prior/restored bytes/modes, metadata, recovery inventory. | False success, inconsistent marker, modified external link target, lost backup. |

Workload update's `--version` selects the installed version to replace, not a
desired target version. Prune retains the highest version; do not assume
profile-aware retention. Do not reuse a pruned/uninstalled fixture as an unchanged
baseline for another scenario.

### P2 / exploratory

| ID / prerequisites | Command or action | Expected behavior | Additional evidence | Failure/regression signal |
| --- | --- | --- | --- | --- |
| P2-01 npm: published exact package/version and isolated environment | Install/test each approved npm name, inspect command ownership, then remove that test installation. | Correct artifact and declared commands; no assumed `func5`. | npm/Node/package/CDN identities and command paths. | Missing package/asset or command conflict. |
| P2-02 Java / PowerShell / Go: approved manual workload/runtime fixtures | Install receipt-defined workloads; initialize or use approved project; start/invoke locally. Go uses approved Flex fixture and user-built executable. | Agreed exploratory path works or limitation is reported without claiming broader support. | SDK/worker/stack/profile identities, local lifecycle output. | Generic setup mistaken for full readiness or unsupported scope silently accepted. |
| P2-03 Foundations: candidate checkout and approved component checks | Explore template/constraint or updater-service harnesses; use #5732's bounded inventory checks where applicable. | Component contract evidence only, with exact tested target recorded. | Selected test/input identity and scope, result, target hash/fingerprint. | Internal coverage presented as new UX or final artifact qualification. |

## Result reporting

```text
Scenario:
Result: PASS / FAIL / BLOCKED
CLI version:
CLI source/release identity:
OS/architecture:
Shell/runtime:
Installer identity (if applicable):
Workload/package/profile/fixture identities:
Expected behavior:
Actual behavior:
Exit code:
Relevant stdout/stderr:
Filesystem/configuration/mode effects:
Issue link (if filed):
Preview 3 comparison classification:
```

For Preview 3 comparisons, use: **unchanged pass**, **expected change**,
**regression**, **new capability**, **baseline limitation**, or **inconclusive**.
Pin baseline artifacts and repeatable package/profile/fixture inputs. Compare
semantics, exit codes, configuration and filesystem effects, not arbitrary wording
or generated bytes unless the contract requires exactness. A baseline failure
is not automatically a candidate regression; inconclusive is not PASS.

Attach logs privately when necessary and redact secrets/customer data. Do not
change authentication to recover a blocked input in this bug-bash workflow.

## Known issues and limitations

- `func update` is not yet operational. Use the approved installer upgrade path.
- Final Preview 4 source/version/build, artifact and installer identities are
  pending the release cut.
- npm depends on actual package publication and does not provide the script
  `func5` side-by-side contract.
- Setup success is not complete SDK/runtime/template readiness.
- Template foundations are not completed new template UX.
- Platform/profile exclusions in the matrix remain applicable.
- Configured installer tests and #5732 coverage are not evidence that final
  release-artifact qualification passed.

## Preview 4 release receipt

| Field | Value |
| --- | --- |
| Preview 3 source | `f8f54ba328758b2165d835930e391af23e60f090` |
| Provisional Preview 4 source | `33eb0b5389f56e5ea872a63420c98b2b04b8e260` |
| Final Preview 4 source | **PENDING** |
| Version | **PENDING** |
| Tag | **PENDING** |
| Build | **PENDING** |
| Installer identity, URL and hash | **PENDING** |
| CLI artifact URLs, hashes and signing evidence | **PENDING** |
| Workload/package receipt and source | **PENDING** |
| Profile/template/catalog/fixture identities | **PENDING** |
| Approved platform/stack/channel combinations | **PENDING** |
| Known issues accepted for release | **PENDING** |
| Qualification status | **PENDING** |

### Release-cut synchronization gate

1. Obtain the source actually selected for the Preview 4 build at the 9 AM Pacific cut.
2. Compare `33eb0b5389f56e5ea872a63420c98b2b04b8e260..FINAL_PREVIEW4_SHA`.
   Verify ancestry; reconcile removals/reverts too if the boundary is not append-only.
3. Update only late-commit effects in What's new, scenarios and matrix.
4. Reconfirm #5701 remains included and resulting source still establishes both
   `0755` wrapper paths, metadata contract and recovery behavior.
5. Populate the release receipt and qualify the actual final artifacts; earlier
   source/project-output checks do not qualify rebuilt or re-signed bytes.
6. Validate commands, links, pending fields, coverage claims and published diff.
7. Only after reconciliation, mark the documentation PR ready for review and
   describe the synchronized guide as final Preview 4 guidance.

## Source evidence

These links pin the provisional source contracts, not mutable published artifacts:

- [Bash installer at the candidate][bash-source] and
  [PowerShell installer at the candidate][powershell-source].
- [Installer harness at the candidate][installer-tests] and
  [configured cross-platform workflow][installer-workflow].
- [Setup options][setup-source] and [catalog source validation][catalog-source].
- [Unimplemented CLI update command][update-source] and
  [existing new-command provider path][new-source].
- [Built-in profiles][profiles-source] and
  [Python worker RID exclusions][python-source].
- [#5732 process/inventory checks][inventory-source] and
  [npm package command map][npm-source].
- [Component release process](../proposed/vnext-release-process.md) is draft
  background, not proof of Preview 4 publication or coverage.

[candidate-delta]: https://github.com/Azure/azure-functions-core-tools/compare/f8f54ba328758b2165d835930e391af23e60f090...33eb0b5389f56e5ea872a63420c98b2b04b8e260
[bash-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/install.sh
[powershell-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/install.ps1
[installer-tests]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/test/Installers/InstallationMetadata.Tests.ps1
[installer-workflow]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/.github/workflows/installer-tests.yml
[setup-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Func/Commands/Setup/SetupCommand.cs
[catalog-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Func/Workloads/Catalog/PackageSourceProvider.cs
[update-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Func/Commands/Update/UpdateCommand.cs
[new-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Func/Templates/NewCommandTemplateCatalog.cs
[profiles-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Func/Profiles/BuiltInProfiles.json
[python-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/src/Workloads/Workers/Python/Workloads.Workers.Python.csproj
[inventory-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/test/Func.Tests/Commands/Workload/WorkloadProgramSmokeTests.cs
[npm-source]: https://github.com/Azure/azure-functions-core-tools/blob/33eb0b5389f56e5ea872a63420c98b2b04b8e260/eng/tools/publish-tools/npm/package.json
