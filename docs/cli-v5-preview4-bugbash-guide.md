# Azure Functions CLI v5 Preview 4 Bug Bash

## Preview 4 status

This guide validates **Azure Functions CLI `5.0.0-preview.4`**, released on
October 20, 2026. It is a preview, not production-ready or at v4 feature parity.

| Receipt | Verified value or remaining gate |
| --- | --- |
| CLI version / tag | `5.0.0-preview.4` / [v5.0.0-preview.4][preview4-release] |
| Final tagged source | `9db15d8fadb49cb602a58c38ac0dd5a100585fcf` |
| Release preparation | [#5739][release-pr] merged as `d691ad70ce9718c2dd3ad40b7326964529f75aa4`; the tag includes its child [#5740][npm-build-pr]. |
| Installer / CLI artifact | Served installer scripts are identified by SHA-256 below. The approved native CLI location is an execution prerequisite; see the **Bug-bash CLI artifact** field in [Release receipt](#release-receipt). |
| Required workload source | https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json |
| Qualification | **SOURCE RELEASE COMPLETE; PUBLISHED-ARTIFACT QUALIFICATION NOT ESTABLISHED.** Source/build checks are not final-artifact qualification. |

## Preview 4 CLI build

**The documentation is ready for human review.** Preview 4 workload packages are
available. The approved native Preview 4 CLI artifact location has not yet been
established.

**Before tomorrow's bug bash:** complete the single **Bug-bash CLI artifact**
field in [Release receipt](#release-receipt) with the release owner's approved
location. Do not substitute Preview 3, another release, or an arbitrary
local/development build.

Until an approved Preview 4 CLI artifact is identified and its directly addressed
binary reports `5.0.0-preview.4`, scenarios requiring the native CLI are
**BLOCKED**. This execution prerequisite does not block documentation publication
or review. Public distribution scenarios retain their separate path-readiness
gates.

If a scenario's required artifact, package, runtime, or fixture is unavailable,
mark that scenario/subcase **BLOCKED**. Missing optional build, hash, signing, or
qualification evidence is **MISSING ARTIFACT EVIDENCE**, not proof of a defect.

## Quick start

1. **OBTAIN:** use the approved Preview 4 CLI artifact/build location in the
   **Bug-bash CLI artifact** field of [Release receipt](#release-receipt).
   If the placeholder remains, stop and record dependent scenarios **BLOCKED**;
   do not choose another build.
2. **INSTALL/USE:** use only that artifact in a new test-owned installation
   directory, following the release owner's instructions.
   [Installation](#installation) distinguishes product use from public installer
   tests, which remain gated by their own distribution paths.
3. **VERIFY:** directly address the installed native CLI and confirm it reports
   exactly `5.0.0-preview.4`; use its full path or verify `func5` resolution before using the wrapper.
4. **CONFIGURE:** set the [required workload source](#required-workload-source)
   before setup or any workload/project command.
5. **ISOLATE:** use the new CLI/workload state homes in the installation example,
   not your working v4/v5 state. Follow [Setup](#setup) for the assigned stack.
6. **TEST:** run the assigned [P0 and P1 scenarios](#core-bug-bash-scenarios);
   run P2 only within the stated exploratory scope.
7. **REPORT:** record **PASS / FAIL / BLOCKED** per scenario/subcase using
   [Result reporting](#result-reporting), including exact byte and input identities.

## Testing rules

- Test only the released Preview 4 candidate and receipt-approved inputs. Replace
  angle-bracket parameters before running examples; a missing required receipt
  means **BLOCKED**, not permission to use "latest."
- Use test-owned installation, state, project, and log directories. Keep logs
  outside directories that init/quickstart can force-clean.
- Preserve existing v4 installations and command routing. Do not delete shared
  NuGet caches, broad home directories, unrelated projects, or working installs.
- Record CLI, installer, workload, SDK/runtime, profile, template, and fixture
  identities. Record commands, exit codes, stdout/stderr, and before/after effects.
  Exit zero alone is not PASS.
- Do not turn missing optional engineering provenance into a global test gate.
  Record it as **MISSING ARTIFACT EVIDENCE**; a missing functional prerequisite
  blocks only the dependent scenario/subcase.
- Preserve failure, cancellation, and recovery evidence before moving/resetting
  state. Prefer a fresh test-owned directory to destructive cleanup.
- After refreshing a terminal, re-establish the test-owned state homes and
  workload-source configuration before continuing.
- Keep these scenarios local; do not deploy to Azure. Stop only tester-owned
  processes.
- Do not include credentials, secrets, or customer data in reports. Redact logs
  and use an approved private attachment location when needed.
- Do not log in, switch accounts, refresh credentials, authorize SSO, or otherwise
  change authentication to work around a blocked scenario.

## Installation

For native product scenarios, use the approved artifact recorded in
[Release receipt](#release-receipt). Set test-owned CLI/workload state homes as
shown below and verify the directly addressed binary before setup. An approved
artifact can enable product testing without making every public distribution
path ready.

The public installer examples below test distribution separately.
The [release page][preview4-release] advertises
`https://aka.ms/func-cli/install.ps1` and `https://aka.ms/func-cli/install.sh`.
The examples download the script into a test-owned file and verify the observed
served-script SHA-256 from [Release receipt](#release-receipt) before execution.
The aliases currently resolve to mutable `vnext` files; stop on a hash mismatch
and request a reconciled receipt. A CLI version pin does not pin installer bytes.
Source-pinned scripts alone do not prove which bytes an alias served.

**Public installer scenarios remain BLOCKED until their Preview 4 archive paths
are ready.** An approved bug-bash artifact alone does not clear this gate.
Only then execute these examples with the version pin shown; the preserved live
manifest observation selects Preview 3 for unpinned prerelease installation.
Manifest-selected Preview 4 installation remains separately **BLOCKED** until
that channel selects Preview 4.

The examples below have not been executed as part of this documentation revision.

### Clean installation

Use a new test-owned root. If any example directory already contains an install
or state, choose a new root rather than deleting it. Set the state homes before
invoking commands; they relocate CLI/workload state, not installer binaries.

**PowerShell / Windows**

```powershell
$P4Version = '5.0.0-preview.4'
$Installer = Join-Path $HOME 'func-bugbash-preview4\install.ps1'
$InstallPath = Join-Path $HOME 'func-bugbash-preview4\install'
$env:FUNC_CLI_HOME = Join-Path $HOME 'func-bugbash-preview4\cli-home'
$env:FUNC_CLI_WORKLOADS_HOME = Join-Path $HOME 'func-bugbash-preview4\workloads-home'

New-Item -ItemType Directory -Path (Split-Path $Installer -Parent) -Force -ErrorAction Stop | Out-Null
Invoke-WebRequest -Uri 'https://aka.ms/func-cli/install.ps1' -OutFile $Installer -ErrorAction Stop
if ((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash -ne 'D9AB2E55FDFE2ADC0478FFDD5BEED0FFA4C4390355F9166ACEEFA9709E3B4DCB') {
    throw 'BLOCKED: installer hash changed; request a reconciled receipt.'
}

& $Installer -Version $P4Version -InstallPath $InstallPath -Verbose
& (Join-Path $InstallPath 'func.exe') --version
```

**Bash / Linux or macOS**

```bash
P4_VERSION='5.0.0-preview.4'
INSTALLER="$HOME/func-bugbash-preview4/install.sh"
INSTALL_DIR="$HOME/func-bugbash-preview4/install"
export FUNC_CLI_HOME="$HOME/func-bugbash-preview4/cli-home"
export FUNC_CLI_WORKLOADS_HOME="$HOME/func-bugbash-preview4/workloads-home"

mkdir -p -- "$(dirname -- "$INSTALLER")" || exit 1
curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' \
    'https://aka.ms/func-cli/install.sh' --output "$INSTALLER" || exit 1
INSTALLER_SHA256='32532A7CD420971E5BAB18ACCC6693FF2BB5C32532D5364A8A37293D2B1EF196'
if command -v sha256sum >/dev/null 2>&1; then
    printf '%s  %s\n' "$INSTALLER_SHA256" "$INSTALLER" | sha256sum --check - || exit 1
else
    printf '%s  %s\n' "$INSTALLER_SHA256" "$INSTALLER" | shasum -a 256 --check - || exit 1
fi

bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --verbose
"$INSTALL_DIR/func" --version
```

Use the PowerShell or Bash installer appropriate to the example. The directly
addressed binary must report the receipt's version. Exit status alone is not
sufficient: the Bash existing-install guard can exit zero without replacing it.

### Upgrade from earlier v5 Preview

Provision the approved Preview 3 installation first and record its version,
installer, wrappers, paths, and workload state. Use the same installation directory
and the approved Preview 4 installer. This is a separate scenario from clean install.

```powershell
& $Installer -Version $P4Version -InstallPath $InstallPath -Force -Verbose
& (Join-Path $InstallPath 'func.exe') --version
```

```bash
bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --force --verbose
"$INSTALL_DIR/func" --version
```

**Force permits replacement. Force does not select a newer version.**
Explicit version selection bypasses manifest selection. An unpinned
`--prerelease` / `-Prerelease` install follows the live CDN manifest and need not
select the receipted candidate, even with force. Record the manifest snapshot when
testing selection; do not assume the GitHub tag proves CDN manifest contents.
A pin cannot make an unavailable artifact exist.

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

### Installation metadata

Successful script publication writes `func.installation.json` beside the native
binary:

```json
{"schemaVersion":1,"updatePolicy":"in-place"}
```

Expect exactly these fields, UTF-8 without BOM, one terminating LF, and Unix mode
**0644**. This policy permits the metadata-gated [Update](#update) command; the
marker alone does not qualify an actual update or the installed artifact.

### Unix wrappers and recovery

Successful Bash and PowerShell-on-Unix installs normalize `func5` to **0755**,
including forced recreation under restrictive umask. Run all five cases in
[Appendix C](#appendix-c---advanced-installer-and-recovery-scenarios) for P0-03.

Publication failures attempt restoration; incomplete restoration must report failure
and retain a recovery location. Use the controlled failure fixtures in Appendix C
for P1-05. Atomicity and synthetic-payload boundaries are in Appendix B.

### Clean tester reset

Choose new install and state paths using the clean-install example. For a
default-layout reset, stop tester-owned processes and move the verified v5
directory to a unique backup location before reinstalling. Preserve relocated
state separately. There is no installer reset switch.

## Required workload source

Configure this exact Preview 4 bug-bash feed before setup, workload, init, new, or
quickstart commands.

**PowerShell**

```powershell
$env:FUNC_CLI_WORKLOADS_SOURCE = "https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
$WorkloadSource = $env:FUNC_CLI_WORKLOADS_SOURCE
```

**Bash**

```bash
export FUNC_CLI_WORKLOADS_SOURCE="https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
WORKLOAD_SOURCE="$FUNC_CLI_WORKLOADS_SOURCE"
```

This setting applies to the current shell. Set it again after opening a new terminal.

Command `--source` overrides the environment setting; use the same feed for any
explicit override. Do not rely on the default nuget.org catalog for this bug bash.
Repository NuGet restore configuration does not configure the installed CLI.
A local directory is not a V3 catalog source; pass an approved local `.nupkg`
positionally to `func5 workload install` only for an assigned package-file fixture.

## Setup

Run the assigned stack's setup after version, wrapper, state, feed, and package
receipt verification. For .NET isolated:

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

### Host acquisition diagnostic

For P0-05, use the published Host version below, not the CLI version as a Host
version. The required feed lists this logical package and all six RID
implementations; package metadata is not payload or local-launch qualification.

```powershell
$HostVersion = '4.1048.200-preview.3'
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version $HostVersion --prerelease --source $WorkloadSource
func5 workload list --all-versions --json
```

```bash
HOST_VERSION='4.1048.200-preview.3'
func5 workload install Azure.Functions.Cli.Workloads.Host --exact --version "$HOST_VERSION" --prerelease --source "$WORKLOAD_SOURCE"
func5 workload list --all-versions --json
```

Verify the logical Host pointer and physical implementation's RID/version/source,
`tools/<rid>` payload, and launchability. Host alone is not full language setup.

## Preview 4 test scope

### What's new since Preview 3

- Installers select the target RID's ZIP on Windows or tar.gz on Unix, preserve
  executable Unix wrappers after forced replacement, and provide publication
  backup/recovery behavior.
- Setup behaves deterministically for automation, with explicit stack selection,
  noninteractive operation, and unwrapped NDJSON records.
- Unsupported profile/runtime combinations reject before worker acquisition;
  incompatible extension bundles block function creation without generated writes.
- Metadata-gated `func update` is available, including preview/version selection
  and confirmation controls. See [Update](#update).
- All six stack sources declare `1.0.0-preview.2`; their `IProjectStack` contract
  requires CLI `5.0.0-preview.4` or later. Existing-provider init/new/quickstart
  journeys remain the testing target, not a new template-first lifecycle.
- The tagged release disables npm build/pack tasks. Do not infer npm publication
  from the earlier distribution implementation.

Engineering PR references and the exact late-source reconciliation are in Appendix A.

## Expected to work in Preview 4

These are released source contracts, not a declaration that every final artifact
or platform/stack combination has been qualified.

| Area | Expected behavior and boundary |
| --- | --- |
| Native installers | Windows x64/arm64, Linux glibc x64/arm64, macOS x64/arm64; qualify each selected RID separately. Workload availability and macOS signing/notarization require their own receipts. |
| Script install/upgrade | Clean and pinned forced installs, v4/v5 coexistence, PATH/session handling, `0755` Unix wrappers, metadata, and bounded recovery behavior. |
| CLI update | In-place only when metadata permits; external-manager or reinstall guidance otherwise. Final-artifact update qualification is separate. |
| Setup | Explicit features/defaults, automation, JSON/check output, cancellation, partial failures, and multiple profiles. |
| Workloads | Install/list/search/update/uninstall/prune with exact identities, multi-version state, and ownership checks. |
| Representative language journeys | .NET isolated, Node.js, and Python with receipt-approved SDKs, workers, templates, and profiles; not every OS/stack permutation. |
| Project lifecycle | Existing-provider init/new/quickstart, adoption/healing, local start/invoke/stop, and early profile/bundle rejection. |
| Terminal behavior | Interactive, redirected/noninteractive, plain output, `NO_COLOR`, and cancellation using the actual input/output capabilities. |

## Exploratory in this bug bash

- **Java and PowerShell language:** manually pinned, approved workload/runtime
  fixtures only. Stack package existence is not complete setup/template support.
- **Go:** an approved Flex-oriented fixture and user-built executable; no promise
  of template setup expansion.
- **npm (P2-01):** blocked unless an existing exact published package/artifact
  receipt is supplied. The tagged build disables packaging; do not publish packages
  or substitute another version to make this scenario runnable.
- **Template/constraint and updater-service components (P2-03):** bounded harness
  evidence only. CLI update itself is released and covered by P0-09.
- **Additional platform exploration:** do not present musl/Alpine or unreceipted
  platform/stack combinations as qualified native Preview 4 coverage.

## Not supported in Preview 4

- .NET in-process setup; Python Windows arm64 worker.
- Python in Windows target profiles; Go outside Flex profiles. Target-SKU
  restrictions are not the same as local developer OS support.
- Native Windows execution of the Bash installer, or native installer
  architectures outside the x64/arm64 RID selections.
- Template-first init or a complete new template/package lifecycle.
- An npm `func5` side-by-side guarantee; the npm bin map does not provide it.
- F# quickstart through the C#-only .NET quickstart provider. This does not exclude
  F# init.

## Update

**FEATURE PRESENT IN PREVIEW 4:** [#5702][update-pr] is in the tagged source.
`func update` reads installation metadata beside the running native binary before
resolving releases. Update invocations skip first-run setup and version/alias
advisories; they must not prompt for stack setup.

Use the verified `func5` wrapper or full native path below. Use separate, test-owned
fixtures for each metadata state; never rewrite a real externally managed install's
metadata to authorize in-place changes.

| Metadata state | Action | Expected result |
| --- | --- | --- |
| Script-installed, schema 1, `updatePolicy: "in-place"` | Run against an approved update target. | Release resolution and, after confirmation, in-place replacement are allowed. Verify target bytes/version after replacement. |
| Schema 1, `updatePolicy: "external"` | Run update, including selection/confirmation flags. | Exit 0 with guidance to use the owning installer/package manager; no update-feed resolution or payload modification. It does not identify a specific manager. |
| Missing metadata | Run update from the supplied missing-marker fixture. | Nonzero exit with reinstall guidance; no release resolution or installation modification. |
| Malformed/invalid metadata | Exercise invalid JSON, missing required fields, duplicate fields, and the supplied oversized fixture. | Nonzero exit with reinstall guidance; no update. The reader limit is 4 KiB. |
| Unsupported schema/policy | Exercise an unsupported schema version and unknown update policy. | Fail closed with reinstall guidance; no unsupported installation is silently modified. |

Selection and confirmation are separate from authorization:

- With no selection flag, update resolves the latest **stable** release.
  `--prerelease` includes previews; neither flag proves which version the live
  manifest currently selects.
- `--version` selects an exact semantic version, including a preview without
  `--prerelease`. An explicitly pinned older version permits a downgrade; an
  unpinned older target does not. An equal version is a successful no-op.
- Without `--yes`, available-input confirmation defaults to **Yes**. Explicitly
  test decline and cancellation. With input unavailable, confirmation safely
  declines rather than proceeding or hanging.
- `--yes` / `-y` skips confirmation, not metadata checks. Use it only with an
  approved pinned target in an isolated fixture.

For example, on an already-current Preview 4 fixture:

```text
func5 update --version 5.0.0-preview.4
func5 update --version 5.0.0-preview.4 --yes
```

These can validate exact selection/no-op behavior; they do **not** prove replacement.
P0-09 also needs approved stable/preview manifest snapshots, a different pinned
target, metadata fixtures, and feed-observation evidence for the no-resolution
cases. Mark unavailable subcases **BLOCKED** instead of fabricating those inputs.

**PUBLISHED-ARTIFACT UPDATE E2E INDEPENDENTLY QUALIFIED: NOT ESTABLISHED.**
Command/component tests and release-preparation validation do not prove published
download, installation, recovery, or cross-platform E2E execution.

## Core bug-bash scenarios

**Product scenarios:** all native-CLI-dependent rows/subcases are **BLOCKED**
until the approved artifact in [Release receipt](#release-receipt) is supplied
and directly verified as `5.0.0-preview.4`. After that, workload, setup, project,
local lifecycle, and update metadata/selection scenarios can run with their
assigned inputs. Published workload availability is not negated by incomplete
native CLI distribution; unavailable fixtures block only dependent subcases.

**Public distribution scenarios:** CDN clean install, public installer archive
retrieval, manifest-selected installation, forced upgrade/update subcases needing
unavailable Preview 4 CDN bytes, and npm distribution remain **BLOCKED** until
their respective public paths/receipts are ready. Supplying the approved native
bug-bash artifact does not by itself qualify those paths.

Run actionable assigned rows after installation, feed configuration, and setup.
Use the common evidence fields in [Result reporting](#result-reporting); Capture
lists only row-specific additions. Record a separate result for each
platform/subcase.

### P0

| ID | Area | Prerequisites | Steps | Expected result | Capture |
| --- | --- | --- | --- | --- | --- |
| P0-01 | Clean install | Empty test-owned install path; matching served-script hash and accessible Preview 4 public RID archive. Public archive access remains BLOCKED. | Install Preview 4 using the pinned clean-install instructions; directly verify the binary, then wrapper and metadata. | CLI reports `5.0.0-preview.4`; correct RID, wrapper/marker, and successful permission modes; no false success. | Installer/archive hashes, native/wrapper paths, metadata bytes/modes. |
| P0-02 | Preview 3 upgrade | Approved Preview 3 install and recorded baseline state; accessible pinned Preview 4 public archive (currently BLOCKED). | Upgrade that installation to pinned Preview 4 with force; compare native/wrapper versions and workload inventory. | Preview 4 replaces Preview 3; wrapper/marker match the released contract; unrelated workload state is preserved. | Before/after files, versions, metadata, modes, inventory. |
| P0-03 | Unix wrapper modes | Disposable Bash and PowerShell-on-Unix installs; their Preview 4 public archive paths must be ready. | Run all five Appendix C cases, including forced replacement under `umask 077`, missing wrapper, and prior `0700` mode. | Wrapper is `0755`, resolves the adjacent executable, and forwards arguments correctly. | Effective umask, wrapper/native/parent modes, native versus wrapper output. |
| P0-04 | v4/v5 coexistence | Working v4 command resolution recorded before install; the Preview 4 public archive path must be ready. | Install v5, refresh PATH/session, inspect all resolution candidates, and run both versions. | Expected v4 remains `func`; receipted v5 is `func5`; no displaced default or stale wrapper. | Both binary identities, command paths, persisted versus inherited PATH. |
| P0-05 | Host acquisition | Empty workload home; approved Host pointer/implementation and runnable fixture prerequisites. | Install the pinned Host; inspect JSON inventory; add approved stack/worker/bundle prerequisites without changing Host; run `func5 start "<PROJECT>" --output plain`. | Correct logical/physical RID/version/source and `tools/<rid>` layout; Host launches the fixture. | Package identities, payload layout, startup output. |
| P0-06 | Setup automation | Explicit-feature, configured-runtime, empty-project, missing-dependency, and multi-profile fixtures. | Run setup noninteractively; separately exercise `--yes`, `--check --output json`, cancellation, and partial failure. | No picker/hang in automation; correct defaults; one physical line per NDJSON event; no check-mode completion marker, trailing advisories, or false completion. | JSON records, marker/config snapshots, optional skips, per-profile outcomes. |
| P0-07 | Force-cleanup safety | Populated disposable projects with sentinels; approved template/catalog IDs. | Run `func5 init "<PROJECT>" --stack node --language JavaScript --force` and `func5 quickstart "<QUICKSTART_PROJECT>" --stack node --language JavaScript --template "<QUICKSTART_ID>" --force`; accept, decline, cancel, and separately remove stdin. | Available-input confirmation defaults No; decline/cancel preserves sentinels; explicit force authorizes cleanup when input is unavailable. | Sentinel/config snapshots and confirmation/cancellation evidence. |
| P0-08 | Redirected console | Runnable fixture; complete required command inputs. | Redirect stdout/stderr; vary stdin and prompt/dashboard capabilities for setup/init/new/quickstart/start; use plain start output, `NO_COLOR`, and dashboard cancellation. | Appropriate prompts/plain fallback, no key-reader failure, inappropriate input loop, output corruption, or hang; local run stops cleanly. | Capability/redirection settings, separate output streams, process termination. |
| P0-09 | Metadata-gated CLI update | Released CLI fixtures for every metadata state; approved manifest/target/byte receipts and no-feed observation fixture. | Run every state and selection/confirmation case in [Update](#update): default stable, `--prerelease`, exact `--version`, same-version no-op, approved pinned downgrade, `--yes`, accept/decline/cancel, and unavailable stdin. Use a fresh fixture per mutating case. | Only `in-place` metadata permits replacement; external policy gives manager guidance; missing/invalid/unsupported metadata gives reinstall guidance. Rejected/declined/no-op cases leave installation bytes unchanged; external/invalid cases do not resolve the feed. Missing inputs block the affected subcase. | Metadata state, selected manifest/target, before/after install hashes and inventory, no-feed observation, prompt/exit behavior, replaced binary's version/hash. |

### P1

| ID | Area | Prerequisites | Steps | Expected result | Capture |
| --- | --- | --- | --- | --- | --- |
| P1-01 | Extension-bundle gate | Compatible, incompatible, malformed, and missing-bundle fixtures. | Run `func5 new "<PROJECT>" --list`; create with a listed `--template "<TEMPLATE_ID>" --name BugBash --non-interactive` and all required options. | Incompatible creation exits 1 without generated writes; compatible creation succeeds; listing stays available. | Bundle constraints/versions, file snapshots, diagnostic. |
| P1-02 | Profiles | Python/Go negative fixtures plus valid controls. | Start Python with `--profile windows-consumption`; test Go with a non-Flex profile; repeat approved valid combinations. | Unsupported combinations reject before worker acquisition; valid controls continue. | Profile/runtime and before/after worker inventory. |
| P1-03 | Workload lifecycle | Separate pinned package, ownership, and multi-version fixtures; required feed configured. | Install/list; run `func5 workload search --prerelease --json`, `func5 workload update "<WORKLOAD_ID>" --exact --prerelease`, `func5 workload prune "<WORKLOAD_ID>" --exact`, and `func5 workload uninstall "<WORKLOAD_ID>" --exact --all-versions` on independently seeded states. | Correct source/identity/ownership; update respects the default major boundary; prune retains the highest installed version; uninstall removes only its requested scope. | JSON inventory before/after each operation, selected package/source and ownership. |
| P1-04 | Create, invoke, stop | Approved SDK/runtime, packages, profiles, templates, and quickstart catalog. | Create a Node.js project with `func5 init "<PROJECT>" --stack node --language JavaScript`; list/select/create a function with new; run `func5 quickstart list --stack node --language JavaScript`, then scaffold a selected ID into another path; start, invoke its documented local endpoint, and stop. Repeat representative .NET isolated/Python paths and adoption/healing. | Expected semantic project/function outputs and preserved configuration; successful local invocation; no leaked tester-owned process. | Template/catalog IDs, generated/config diffs, startup and local request/response results. |
| P1-05 | Installer recovery | Pre-provisioned released-source harness environment and synthetic failure fixtures; separate real-artifact receipts. | Run Appendix C download/invalid-payload, wrapper/metadata publication, destination-link, and restoration cases; qualify real artifacts separately. | Nonzero failure; prior state preserved/restored where applicable; incomplete restoration reports and retains recovery files; external link targets remain unchanged. | Error, prior/restored bytes/modes, metadata consistency, recovery inventory. |

Workload update's `--version` selects the **installed version to replace**, not
the desired target version; CLI update's `--version` selects the **target**.
Prune is not profile-aware retention. Do not reuse a pruned/uninstalled fixture
as an unchanged baseline for another operation.

### P2 / exploratory

| ID | Area | Prerequisites | Steps | Expected result | Capture |
| --- | --- | --- | --- | --- | --- |
| P2-01 | npm | Existing exact published package/artifact receipt, Node >=22, isolated command ownership. Currently receipt-blocked. | If the prerequisite is supplied, install/test each approved package name, inspect command ownership, and remove only that test install. Do not publish or enable build tasks. | Correct declared package/commands without assuming `func5`; report absent receipt as BLOCKED, not a release regression. | npm/Node/package/CDN identities and command paths. |
| P2-02 | Java / PowerShell / Go | Approved manual workload/runtime fixtures; Go Flex fixture and user-built executable. | Install pinned workloads; use the approved project; start/invoke/stop locally. | Bounded exploratory outcome, not a claim of complete setup/template or customer-flow support. | SDK/worker/stack/profile identities and local lifecycle results. |
| P2-03 | Component evidence | Approved template/constraint/updater-service checks or #5732 inventory harness inputs. | Run the assigned bounded checks against the explicitly identified target. | Component evidence only; no fallback to another target and no final-artifact qualification claim. | Test/input identity, tested target role/hash/fingerprint, result and scope. |

## Known issues / expected limitations

- All six installer-selected Preview 4 archive URLs returned HTTP 404 in the
  preserved receipt. Public script installation remains **BLOCKED** until its
  required path is ready; other native product scenarios need the approved
  bug-bash artifact, not every public path.
- The preserved live CDN manifest selected `5.0.0-preview.3` for preview and had
  no stable entry. This is a channel observation, not a Preview 4 identity;
  manifest-selected Preview 4 installation remains **BLOCKED** until that
  selection changes. Use the explicit version pin for pinned installer scenarios.
- Metadata-gated update is released. Published-artifact update E2E is not
  independently established; an already-current no-op does not qualify replacement.
- npm pack tasks are disabled in the tagged source; neither npm publication nor
  npm `func5` coexistence is established.
- Setup may skip optional dependencies. Success alone does not prove SDK, worker,
  template, or application readiness.
- General prerelease selection does not select every preview bundle/template
  channel. Use the assigned receipt rather than assuming channel equivalence.
- The scope exclusions above remain applicable. Component/template foundations
  and configured CI are not completed customer UX or final-artifact qualification.
- No release-owner accepted-issue list was established from the published
  evidence. These documented limitations are not an invented acceptance list.

## Result reporting

Report **PASS** only for observed expected behavior, **FAIL** for an observed
contract violation with approved inputs, or **BLOCKED** for a missing prerequisite.
Name the missing input and affected subcase. Do not turn missing evidence or an
inconclusive comparison into PASS.

Required tester identity is the installed native version/path, OS/RID, verified
installer (when used), workload source and selected package versions, plus the inputs needed
by that subcase. Record deeper provenance when available; enter
**MISSING ARTIFACT EVIDENCE** for an unavailable release build, archive/package
hash, or signing receipt without converting an otherwise exercisable subcase
to BLOCKED. No binary was executed to qualify this guide.

```text
Scenario / subcase:
Result: PASS / FAIL / BLOCKED
Missing prerequisite (if BLOCKED):
CLI version / executable path / SHA-256:
CLI source SHA / release tag / release build:
OS / architecture / shell:
SDK / runtime identities:
Installer URL / source / SHA-256 (if applicable):
CLI archive URL / SHA-256 / signing receipt:
Workload source / logical and physical package IDs / versions / hashes / RID:
Profile / template / catalog / fixture identities:
Update manifest / target identity (if applicable):
Command / expected behavior / actual behavior:
Exit code / relevant stdout and stderr:
Filesystem / configuration / mode / inventory effects:
Failure or recovery evidence location:
Issue link (if filed):
Preview 3 comparison classification:
```

For a reproducible failure, file an issue in the
[Core Tools issue tracker](https://github.com/Azure/azure-functions-core-tools/issues)
and tag it `v5`, as directed by the release notes; include the scenario ID and
redacted evidence. Record BLOCKED inputs with the bug-bash coordinator rather than
asserting a product defect.

For Preview 3 comparisons, use **unchanged pass**, **expected change**,
**regression**, **new capability**, **baseline limitation**, or **inconclusive**.
Pin baseline artifacts and repeatable package/profile/fixture inputs. Compare
semantics, exit codes, configuration, and filesystem effects, not arbitrary wording
or generated bytes unless the contract requires exactness. A baseline failure
is not automatically a Preview 4 regression.

## Release receipt

This preserved receipt was reconciled against the published tag/release, source receipt,
served installer bytes, live CDN endpoints/manifest, and the required workload
feed on **October 21, 2026 UTC**. No artifact probes were repeated for this
documentation publication. A source-declared version, registration entry, archive
name, or HTTP header is not cryptographic final-artifact provenance.

| Field | Verified value or explicit gap |
| --- | --- |
| Preview 3 source / tag | `f8f54ba328758b2165d835930e391af23e60f090` / `v5.0.0-preview.3` |
| Original guide baseline | `33eb0b5389f56e5ea872a63420c98b2b04b8e260`; historical draft boundary, not released Preview 4. |
| Final Preview 4 source | `9db15d8fadb49cb602a58c38ac0dd5a100585fcf` |
| Parent / release-preparation relationship | Tagged commit's parent is `d691ad70ce9718c2dd3ad40b7326964529f75aa4` (#5739); that commit's parent is `f30fca693b4b97d7ab853f85bd126068144aeac9` (#5702). #5739's PR head was `71ef1f4efe046f594183049fbf44817069bf71a5`. |
| CLI version | `5.0.0-preview.4`; final source prefix/suffix and #5739 PublicRelease evaluation agree. No released binary was executed for this documentation revision. |
| Tag / GitHub release | [v5.0.0-preview.4][preview4-release], pointing directly to final source; prerelease published `2026-10-20T22:46:37Z`. |
| Final release build/run | **MISSING ARTIFACT EVIDENCE; non-blocking engineering evidence.** No exact artifact-producing build/run or source-to-CDN-byte association was established from the available release evidence. |
| Release-preparation validation | #5739 reports Windows restore/clean/Release CI build with 0 warnings/errors and 2,668 CLI tests passed, 0 failed/skipped; PublicRelease version evaluates to `5.0.0-preview.4`. PR check `coretools.public` build `310928` succeeded; this is not a final release artifact receipt. See Appendix B. |
| Installer source identity | Final-source [PowerShell][powershell-source] Git blob `a7fbeba6f0f336fb678d427314622287aceb3a14`; [Bash][bash-source] Git blob `818d71fa3d536a30968b3731d8d37fcd615567ba`. Downloaded served scripts matched these blob identities; their separate SHA-256 hashes are below. |
| Published installer entry points | Both release-advertised aliases returned HTTP 200 and resolved to the mutable `vnext` URLs below. The received scripts were hashed, not executed. Testers must verify their saved script against the receipt before running it. |
| Bug-bash CLI artifact | **TODO before bug bash: add approved Preview 4 artifact/build location** |
| CLI archive hashes / signing / build association | **MISSING ARTIFACT EVIDENCE; non-blocking once the approved artifact is identified and runtime-verified.** No downloaded Preview 4 archive hash, signing/notarization result, or final build association was established. An empty GitHub release asset list does not prove CDN absence. |
| Live channel manifest | [CDN version manifest][cdn-version] returned HTTP 200 with `{"stable":null,"preview":"5.0.0-preview.3"}`. Unpinned prerelease installation is not a Preview 4 path at this observation. |
| Required installation source for workloads | https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json |
| Published stack versions | The feed lists `Azure.Functions.Cli.Workloads.DotNet`, `.Node`, `.Python`, `.Go`, `.Java`, and `.PowerShell`, each at `1.0.0-preview.2`, agreeing with all six source versions. Exact .NET registration is listed and published `2026-10-20T21:48:46Z`. Their tagged source requires CLI `5.0.0-preview.4` or later for `IProjectStack`; metadata alone does not qualify published-binary compatibility or complete Java/PowerShell flows. |
| Published Host identity | Logical `Azure.Functions.Cli.Workloads.Host` and each `.win-x64`, `.win-arm64`, `.linux-x64`, `.linux-arm64`, `.osx-x64`, `.osx-arm64` implementation list `4.1048.200-preview.3`. The logical registration is listed and published `2026-09-29T22:12:02Z`; Host is versioned independently of the CLI. |
| Workload/package byte receipt | IDs/versions and publication metadata are verified below; package bytes/hashes, source-to-package linkage, and complete stack/Host/worker/bundle/template pairings remain **MISSING ARTIFACT EVIDENCE**. Registration metadata does not prove downloadable payload identity or compatibility. |
| Profiles / templates / catalogs / fixtures | Exact assigned runtime/profile/catalog and update target/manifest/no-feed observation fixtures remain scenario-specific prerequisites. A missing input makes only its dependent subcase **BLOCKED**, not the whole guide. |
| Platform/RID/channel data | Installer source selects the six RIDs below; all six Preview 4 CLI endpoints were unavailable. Host metadata lists all six; Python worker metadata lists five, excluding Windows arm64. The required feed is the pre-release feed, not a guarantee of every component's preview selection or a qualified platform/stack pairing. |
| Known issues accepted for release | No release-owner acceptance receipt was established; this is a recommended-evidence gap, not a reason to prevent all testing. The limitations above remain guidance boundaries, not an acceptance list. |
| Qualification status | **PUBLISHED-ARTIFACT QUALIFICATION NOT ESTABLISHED.** Final-artifact install/update/local-lifecycle and cross-platform results were not supplied by the inspected evidence. The bug bash is intended to collect results, not inherit a fabricated qualification claim. |

### Tomorrow-morning release check

Before starting native CLI scenarios:

1. Obtain the approved Preview 4 CLI artifact/build location from the release owner.
2. Record its build/run/artifact identity.
3. Confirm that the artifact is intended for Preview 4.
4. Verify the directly addressed native binary reports exactly `5.0.0-preview.4`.
5. Replace the **Bug-bash CLI artifact** receipt field with the approved location
   and identity.
6. Proceed with native CLI scenarios only after these checks pass; public
   distribution subcases retain their own readiness gates.

### Observed served-script identities

These are received-file hashes, not claims that the scripts or CLI were executed.
The clean-install examples verify these exact bytes; a later alias change requires
reconciliation rather than silently accepting another script.

| Installer | Observed resolved URL | SHA-256 |
| --- | --- | --- |
| PowerShell | [raw `vnext/install.ps1`][served-powershell] | `D9AB2E55FDFE2ADC0478FFDD5BEED0FFA4C4390355F9166ACEEFA9709E3B4DCB` |
| Bash | [raw `vnext/install.sh`][served-bash] | `32532A7CD420971E5BAB18ACCC6693FF2BB5C32532D5364A8A37293D2B1EF196` |

### Installer-selected Preview 4 archive access

These are the exact version-pinned destinations selected by the observed scripts.
An unavailable URL is not a hash/signing receipt or proof of global nonpublication.

| Developer platform | RID | Exact archive URL | Observed access |
| --- | --- | --- | --- |
| Windows x64 | `win-x64` | [func-win-x64.zip][archive-win-x64] | HEAD 404; ranged GET 404 |
| Windows arm64 | `win-arm64` | [func-win-arm64.zip][archive-win-arm64] | HEAD 404; ranged GET 404 |
| Linux glibc x64 | `linux-x64` | [func-linux-x64.tar.gz][archive-linux-x64] | HEAD 404; ranged GET 404 |
| Linux glibc arm64 | `linux-arm64` | [func-linux-arm64.tar.gz][archive-linux-arm64] | HEAD 404; ranged GET 404 |
| macOS x64 | `osx-x64` | [func-osx-x64.tar.gz][archive-osx-x64] | HEAD 404; ranged GET 404 |
| macOS arm64 | `osx-arm64` | [func-osx-arm64.tar.gz][archive-osx-arm64] | HEAD 404; ranged GET 404 |

### Published workload/package metadata

The required feed's search/registration metadata lists these exact package
versions. These observations establish package names and published versions, not
downloaded-byte hashes or qualified dependency pairings. Record actual selected
logical/physical IDs, versions, source and RID after acquisition; do not silently
substitute a CI version, another feed, or a different stack version.

| Package identity | Observed published version | Boundary |
| --- | --- | --- |
| `Azure.Functions.Cli.Workloads.DotNet`, `.Node`, `.Python`, `.Go`, `.Java`, `.PowerShell` | `1.0.0-preview.2` | Six stack packages; Java/PowerShell/Go retain their exploratory scope. |
| `Azure.Functions.Cli.Workloads.Host` and six RID implementations | `4.1048.200-preview.3` | Pin the exact version, including on Windows arm64 where search also exposes a differently ordered CI version. |
| `Azure.Functions.Cli.Workloads.ExtensionBundles` | `4.42.0-preview.2` | Publication is not evidence of every profile/bundle pairing. |
| `Azure.Functions.Cli.Workloads.Templates.DotNet`, `.Node`, `.Python` | `1.0.1` | Stable entries exist; preview/experimental entries are separate channels, not implied by general prerelease selection. |
| `Azure.Functions.Cli.Workloads.Workers.Node` / `.Go` | `3.13.0-preview.1` / `1.0.0-preview.1` | Exact non-CI entries are listed; record actual selection rather than assuming the newest search result is the assigned fixture. |
| `Azure.Functions.Cli.Workloads.Workers.Python` and five RID implementations | `4.43.0-preview.3` | `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`; no Windows arm64 worker. |
| `Azure.Functions.Cli.Workloads.Workers.Java` / `.PowerShell` | `2.19.4-preview.1` / `4.0.4778-preview.1` | Manual, assigned exploratory fixtures only. |

### Receipt-field classification

Classification describes the field's role; a missing field is not thereby
verified. Scenario-specific required inputs can be BLOCKED without blocking
publication of otherwise usable instructions.

| Field | Classification | Justification |
| --- | --- | --- |
| Release version, tag/source, and native `--version` check | **BUG-BASH REQUIRED** | The tester must identify `5.0.0-preview.4` by its directly addressed executable rather than infer it from a tag or command exit. |
| Resolved installer URL and served/executed-script identity | **BUG-BASH REQUIRED for public script scenarios** | The receipt supplies observed URLs/hashes and the tester must verify the saved script before executing it. |
| Approved Preview 4 CLI artifact location and identity | **BUG-BASH REQUIRED for native CLI execution** | The release owner must supply the approved artifact and the tester must verify its native version; the missing location does not block documentation publication or review. |
| Public installation path / manifest readiness | **BUG-BASH REQUIRED for dependent distribution scenarios** | An approved bug-bash artifact does not establish that the public archive, manifest selection, forced upgrade, or npm path is ready. |
| Artifact-producing build/run and source association | **NON-BLOCKING ENGINEERING EVIDENCE** | This strengthens provenance but is not necessary to exercise an accessible, explicitly pinned and runtime-verified preview. |
| CLI archive hashes and signing/notarization results | **RECOMMENDED EVIDENCE** | These strengthen byte/platform assurance but their absence alone does not block this isolated preview bug bash or establish a signing PASS. |
| Workload source and selected logical/physical package IDs/versions | **BUG-BASH REQUIRED** | Reproducible workload testing requires the exact feed and available package versions rather than CLI-version inference. |
| Published workload/package byte hashes and source linkage | **NON-BLOCKING ENGINEERING EVIDENCE** | Package metadata and recorded selection support bounded testing while unverified byte provenance remains explicitly missing. |
| Platform/RID/channel under test | **BUG-BASH REQUIRED** | Each result must identify its actual RID and selection channel without claiming all combinations are qualified. |
| Runtime/profile/template/catalog/update fixtures | **BUG-BASH REQUIRED** | Each assigned subcase needs its own functional inputs, and unavailable inputs make that subcase BLOCKED rather than halt unrelated testing. |
| Known accepted issues | **RECOMMENDED EVIDENCE** | An owner-supplied acceptance list helps triage, but its absence must not be converted into either invented acceptance or a global start gate. |
| Final-artifact qualification, including update E2E | **NON-BLOCKING ENGINEERING EVIDENCE** | The bug bash collects this evidence and must not claim it already exists. |
| Exact npm publication receipt | **NOT PRODUCED / MISSING EVIDENCE** | No existing exact npm receipt was established, so P2-01 remains BLOCKED without blocking native scenarios. |

### Minimum safe tester receipt

1. **Release:** `5.0.0-preview.4`, associated with the final source/tag above.
2. **Artifact:** the approved location and build/artifact identity in the
   **Bug-bash CLI artifact** field. Public script scenarios additionally require
   a hash-verified release-advertised script, explicit version pin, and an
   accessible public archive for the assigned RID.
3. **Runtime identity:** directly addressed native `func --version` must report
   exactly `5.0.0-preview.4` before wrapper/workload scenarios; this is tester
   verification, not cryptographic provenance or an already-observed result.
4. **Workloads:** configure the exact required feed, use available assigned
   package versions, and record selected logical/physical identities and RID.
5. **Isolation and scope:** record OS/architecture/shell, use new test-owned
   state, and mark every unavailable scenario prerequisite BLOCKED.

The documentation is ready for review: version, installer instructions/identity,
required feed, package publication metadata, RID scope, and the blocked-result
model are established. The single approved-artifact receipt field remains an
execution prerequisite, not a guide-publication blocker. Native CLI scenarios
stay **BLOCKED** until that field is completed and the version check passes;
public distribution subcases also need their own paths ready. No installer, CLI,
package payload, or product scenario was executed here.

## Remaining release-receipt gaps

- **NATIVE CLI EXECUTION ONLY:** complete the approved artifact field in
  [Release receipt](#release-receipt) and verify `5.0.0-preview.4` before dependent
  scenarios run. This does not block documentation publication or review.
- **PUBLIC DISTRIBUTION:** the six observed installer-selected URLs returned
  404 and the observed preview manifest selected Preview 3. Dependent public
  install/upgrade/channel subcases remain **BLOCKED** until their paths are ready.
- **NON-BLOCKING:** the final artifact-producing build/run and exact
  source-to-artifact association remain missing engineering evidence.
- **NON-BLOCKING:** CLI archive SHA-256 and signing/notarization receipts remain
  missing recommended evidence; no signing result is claimed.
- **NON-BLOCKING:** package byte hashes/source linkage and complete qualified
  dependency/platform pairings remain missing engineering evidence.
- **NON-BLOCKING for guide publication:** assigned runtime/profile/template/catalog
  and update manifest/target/no-feed fixtures are still needed for their dependent
  subcases; record those subcases BLOCKED until the inputs are supplied.
- **NON-BLOCKING:** a release-owner accepted-issue receipt remains recommended.
- **NON-BLOCKING:** final-artifact qualification, including replacement update E2E
  and cross-platform local lifecycle results, remains unestablished.
- **NON-BLOCKING:** the exact npm receipt remains missing; P2-01 stays BLOCKED.

Publish and review #5737 with the artifact prerequisite explicit. Do not present
publication as permission to run blocked native CLI or public distribution
scenarios, or as final-artifact qualification. Missing optional engineering
evidence alone must not hold the guide. Do not trigger a build/release, publish
packages, alter artifacts, or change authentication to fill any gap.

## Appendix A - Changes since Preview 3

The [final comparison][release-delta] is append-only: 30 commits after Preview 3,
including the original guide's 26-commit baseline. The
[draft-to-final comparison][draft-delta] adds exactly four commits; no baseline
commits were removed, and the delta contains no reverts.

| Added commit | Classification | Documentation consequence |
| --- | --- | --- |
| `2e1926982853934e1810caac804a3867dc9e543b` ([#5738][stack-release-pr]) | Workload release preparation | Aligns Go/Python/Java/PowerShell stack source versions with .NET/Node at `1.0.0-preview.2`; refreshes six stack release notes. No upstream Host/worker pin changes. |
| `f30fca693b4b97d7ab853f85bd126068144aeac9` ([#5702][update-pr]) | User-visible update and startup behavior, with regression tests | Wires metadata-gated update, delayed update HTTP client creation, version/preview/confirmation controls, and first-run/advisory suppression. Corrects the draft's outdated update disposition. |
| `d691ad70ce9718c2dd3ad40b7326964529f75aa4` ([#5739][release-pr]) | CLI release preparation | Sets `5.0.0-preview.4` and release notes. No installer, workload, or functional implementation changes in this commit. |
| `9db15d8fadb49cb602a58c38ac0dd5a100585fcf` ([#5740][npm-build-pr]) | Packaging/distribution | Disables npm prepare/pack tasks in the build template. This tagged boundary, not #5739 alone, is the final source identity. |

Installer source/tests/workflow, setup behavior, profiles, and existing-provider
template paths did not change between the draft baseline and final tag. There are
no additional standalone test-only or internal-only commits in that four-commit
delta; #5702 includes its own test and architecture-documentation changes.

Earlier included engineering references:

- Install/archive selection [#5622](https://github.com/Azure/azure-functions-core-tools/pull/5622);
  installation metadata/publication [#5700](https://github.com/Azure/azure-functions-core-tools/pull/5700),
  [#5701](https://github.com/Azure/azure-functions-core-tools/pull/5701).
- Setup extraction/automation [#5594](https://github.com/Azure/azure-functions-core-tools/pull/5594),
  [#5604](https://github.com/Azure/azure-functions-core-tools/pull/5604);
  RID-specific Host layout [#5614](https://github.com/Azure/azure-functions-core-tools/pull/5614).
- Bundle/profile gates [#5580](https://github.com/Azure/azure-functions-core-tools/pull/5580),
  [#5507](https://github.com/Azure/azure-functions-core-tools/pull/5507);
  terminal capabilities [#5581](https://github.com/Azure/azure-functions-core-tools/pull/5581),
  [#5582](https://github.com/Azure/azure-functions-core-tools/pull/5582).
- Template command context, factory, catalog, parameters, and constraints
  [#5660](https://github.com/Azure/azure-functions-core-tools/pull/5660),
  [#5661](https://github.com/Azure/azure-functions-core-tools/pull/5661),
  [#5665](https://github.com/Azure/azure-functions-core-tools/pull/5665),
  [#5667](https://github.com/Azure/azure-functions-core-tools/pull/5667),
  [#5692](https://github.com/Azure/azure-functions-core-tools/pull/5692),
  [#5698](https://github.com/Azure/azure-functions-core-tools/pull/5698);
  metadata-only stack catalog [#5707](https://github.com/Azure/azure-functions-core-tools/pull/5707).
- Updater reliability/progress [#5609](https://github.com/Azure/azure-functions-core-tools/pull/5609),
  [#5610](https://github.com/Azure/azure-functions-core-tools/pull/5610) are now
  connected to the CLI by #5702; they still do not prove published-artifact E2E.
- npm implementation [#5669](https://github.com/Azure/azure-functions-core-tools/pull/5669)
  remains in source, but #5740 disables release build packaging. Its package
  requires Node >=22 and declares `func`, `azfun`, and `azurefunctions`, not `func5`.

## Appendix B - Engineering evidence and qualification boundaries

### Source/build evidence versus final-artifact qualification

[#5732](https://github.com/Azure/azure-functions-core-tools/pull/5732) is
**TEST-ONLY / DOCUMENTATION**, not a released CLI feature. It adds isolated
process/inventory checks and optional Preview 3 empty-inventory replay on Windows
x64. See [CLI process smoke tests](../CONTRIBUTING.md#cli-process-smoke-tests).
Record whether a test used project output, an explicit published executable, or
the Preview 3 replay, together with its target hash/fingerprint.

The optional replay requires a pre-provisioned archive matching the source's
expected SHA-256
`06fdd97d0e17fff6b7da1e2d2701a01e9fac21f63c6c007cfb79004ec3b5a73b`.
Absent that input, it does not qualify Preview 3. Its Windows x64 scope, empty
inventory, and explicit target checks do not qualify all workload mutations,
other platforms, or rebuilt/re-signed Preview 4 artifacts.

The installer workflow is configured for Windows, Ubuntu, and macOS runners.
Configuration is not evidence that the workflow or final-artifact qualification
ran. The harness uses synthetic payloads for metadata, wrapper, publication,
invalid-payload, destination-link, and recovery checks.

#5739 reports Windows restore/clean/Release build and 2,668 passing CLI tests after
refreshing onto `f30fca693b4b97d7ab853f85bd126068144aeac9`. It reports PublicRelease
version `5.0.0-preview.4`; development version
`5.0.0-preview.4.dev.26520.0` is not the release identity. The
[PR validation build](https://azfunc.visualstudio.com/ae7e3bf3-d41a-4480-9ac0-b6cf9df9ac24/_build/results?buildId=310928)
is not the final artifact-producing build. Final user edits after the reported
build/test run affected release notes only.

The release PR explicitly says published-artifact E2E, actual update
downloads/installation, and cross-platform execution were not performed in that
preparation transaction. The later tagged #5740 packaging change is not covered
by that claim. The inspected tag has no returned GitHub check runs, statuses, or
Actions runs, and the release has no GitHub assets; none of those observations
proves that an external release pipeline or CDN artifact does not exist.

[Update command tests][update-tests] cover metadata authorization, external and
invalid states, flags, confirmation/unavailable input, no-op/downgrade behavior,
progress, and cancellation. They and updater-service harnesses are component
evidence, not independently observed final-artifact E2E.

### Installer contracts and atomicity

The installers request `func-<rid>.zip` on Windows and `func-<rid>.tar.gz` on Unix
from the versioned CDN directory. Inspect staged executable payloads and record
archive digests separately from installer source/served-script identity.

Publication backs up replaced entries and writes installation metadata; failures
attempt restoration. Invalid executable payloads, reserved metadata collisions,
and destination links/reparse points are rejected. Incomplete restoration must
retain recovery evidence, not report success.

Metadata replacement is atomic, but payload publication, PATH updates, and shell
profile changes are **not one globally atomic transaction**. Do not promise
recovery from every interruption or post-publication PATH failure. Successful
metadata is exactly schema 1 with `in-place`, UTF-8 without BOM, one terminating
LF, and Unix `0644`.

The harness explicitly covers forced wrapper recreation under `umask 0077` for
both Unix installer paths. Clean-under-077 and pre-existing-0700 are additional
bug-bash checks, not claimed dedicated harness cases. Wrapper `0755` does not
alone prove native-binary or parent-directory accessibility.

Template and stack metadata/catalog/constraint foundations do not establish
template-first init or a complete new package lifecycle. Six stack package
versions sharing a contract does not establish complete Java/PowerShell flows.

### Final-source references

Implementation, test, and version links below pin
`9db15d8fadb49cb602a58c38ac0dd5a100585fcf`; they establish source contracts,
not mutable published bytes:

- [CLI version][version-source] and [Preview 4 release notes][notes-source].
- [Bash installer][bash-source], [PowerShell installer][powershell-source],
  [installer harness][installer-tests], and [configured workflow][installer-workflow].
- [Setup options][setup-source] and [catalog source validation][catalog-source].
- [Metadata-gated update command][update-source], [metadata reader][metadata-source],
  [update registration][update-registration], [updater implementation][updater-source],
  and [command tests][update-tests].
- [Existing new-command provider path][new-source] and
  [C#-only .NET quickstart provider][dotnet-quickstart].
- [Built-in profiles][profiles-source] and [Python worker RID exclusions][python-source].
- [Host source version][host-version] and stack source versions:
  [.NET][dotnet-version], [Node.js][node-version], [Python][python-version],
  [Go][go-version], [Java][java-version], [PowerShell][powershell-version].
- [#5732 process/inventory checks][inventory-source], [npm command map][npm-source],
  and [tagged npm build/pack disposition][npm-build-source].
- [Component release process](../proposed/vnext-release-process.md) is draft
  background, not proof of publication or coverage.

## Appendix C - Advanced installer and recovery scenarios

### Unix force-upgrade cases (P0-03)

Run each case for Bash and PowerShell-on-Unix in separate test-owned installations:

1. Normal clean creation.
2. Forced upgrade from the approved Preview 3 installation.
3. Forced upgrade under `umask 077`.
4. Remove only the wrapper from a disposable installation, then force recreation.
5. Set that disposable wrapper to `0700`, then force replacement.

Using the installation variables, run the restrictive-umask Bash case:

```bash
(umask 077; bash "$INSTALLER" --version "$P4_VERSION" --install-path "$INSTALL_DIR" --force)
```

For PowerShell-on-Unix, use the approved PowerShell script:

```bash
PS_INSTALLER='<APPROVED_INSTALLER>'
(umask 077; pwsh -NoLogo -NoProfile -File "$PS_INSTALLER" -Version "$P4_VERSION" -InstallPath "$INSTALL_DIR" -Force)
```

Inspect the resulting wrapper:

```bash
stat -c '%a' "$INSTALL_DIR/func5" 2>/dev/null ||
    stat -f '%Lp' "$INSTALL_DIR/func5"
```

Expect `755` for every successful case. Compare directly addressed native and
wrapper version/help output, exercise argument forwarding, and confirm v4 routing.
Record native-binary and parent-directory permissions as well as wrapper mode.

### Controlled publication/recovery cases (P1-05)

Use the [installer harness][installer-tests] only in a pre-provisioned, disposable
test environment. Do not corrupt published assets or a working installation.

Exercise download failure, missing/invalid executable payloads, reserved metadata
collisions, destination symlinks/reparse points, wrapper/metadata publication
failure, and incomplete restoration. Compare prior/restored payload, wrapper, and
metadata bytes/modes; verify external link targets remain untouched.

On failure, retain the reported error and recovery location/inventory before
resetting. `-KeepArchive` / `--keep-archive` retains download/staging evidence.
Synthetic harness success and recovery evidence must be reported separately from
real released-artifact installation/upgrade qualification.

[preview4-release]: https://github.com/Azure/azure-functions-core-tools/releases/tag/v5.0.0-preview.4
[release-pr]: https://github.com/Azure/azure-functions-core-tools/pull/5739
[stack-release-pr]: https://github.com/Azure/azure-functions-core-tools/pull/5738
[update-pr]: https://github.com/Azure/azure-functions-core-tools/pull/5702
[npm-build-pr]: https://github.com/Azure/azure-functions-core-tools/pull/5740
[release-delta]: https://github.com/Azure/azure-functions-core-tools/compare/f8f54ba328758b2165d835930e391af23e60f090...9db15d8fadb49cb602a58c38ac0dd5a100585fcf
[draft-delta]: https://github.com/Azure/azure-functions-core-tools/compare/33eb0b5389f56e5ea872a63420c98b2b04b8e260...9db15d8fadb49cb602a58c38ac0dd5a100585fcf
[served-powershell]: https://raw.githubusercontent.com/Azure/azure-functions-core-tools/vnext/install.ps1
[served-bash]: https://raw.githubusercontent.com/Azure/azure-functions-core-tools/vnext/install.sh
[cdn-version]: https://cdn.functions.azure.com/public/cli/v5/version.json
[archive-win-x64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-win-x64.zip
[archive-win-arm64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-win-arm64.zip
[archive-linux-x64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-linux-x64.tar.gz
[archive-linux-arm64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-linux-arm64.tar.gz
[archive-osx-x64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-osx-x64.tar.gz
[archive-osx-arm64]: https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.4/func-osx-arm64.tar.gz
[version-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Directory.Version.props
[notes-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/release_notes.md
[bash-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/install.sh
[powershell-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/install.ps1
[installer-tests]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/test/Installers/InstallationMetadata.Tests.ps1
[installer-workflow]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/.github/workflows/installer-tests.yml
[setup-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Commands/Setup/SetupCommand.cs
[catalog-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Workloads/Catalog/PackageSourceProvider.cs
[update-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Commands/Update/UpdateCommand.cs
[metadata-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Update/InstallationMetadataReader.cs
[update-registration]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Update/UpdateRegistration.cs
[updater-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Update/CliUpdater.cs
[update-tests]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/test/Func.Tests/Commands/Update/UpdateCommandTests.cs
[new-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Templates/NewCommandTemplateCatalog.cs
[dotnet-quickstart]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/DotNet/DotNetQuickstartProvider.cs
[profiles-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Func/Profiles/BuiltInProfiles.json
[python-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Workers/Python/Workloads.Workers.Python.csproj
[host-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Host/Directory.Version.props
[dotnet-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/DotNet/Directory.Version.props
[node-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/Node/Directory.Version.props
[python-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/Python/Directory.Version.props
[go-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/Go/Directory.Version.props
[java-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/Java/Directory.Version.props
[powershell-version]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/src/Workloads/Stacks/PowerShell/Directory.Version.props
[inventory-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/test/Func.Tests/Commands/Workload/WorkloadProgramSmokeTests.cs
[npm-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/eng/tools/publish-tools/npm/package.json
[npm-build-source]: https://github.com/Azure/azure-functions-core-tools/blob/9db15d8fadb49cb602a58c38ac0dd5a100585fcf/eng/ci/templates/jobs/build.yml
