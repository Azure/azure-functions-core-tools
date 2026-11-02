# Evidence, Screenshots, and Issue Submission

Target repository:
<https://github.com/Azure/azure-functions-core-tools>

## Evidence boundary

Collect only what is needed to reproduce and triage the observed behavior:

- literal isolated CLI executable path;
- exact command tokens, exit code, and separate stdout/stderr;
- exact CLI version and `--verbose` output from that same executable;
- OS, architecture, libc when relevant, and shell;
- selected stack runtime and relevant Host/workload versions;
- target-profile name and template ID when applicable;
- status of the five approved bug-bash settings.

Do not collect the full environment, command history, shell profile contents,
home-directory listings, unrelated logs, customer files, cloud resource data,
or expected/actual environment values.

Use synthetic projects and inputs. If a command or output contains a secret,
token, key, connection string, customer identifier, or private endpoint, do not
capture it. Reproduce with synthetic data or replace the sensitive material
with a clear `[REDACTED]` marker before it enters the report.

## Finite command capture

The helper's current API intentionally breaks compatibility with the original
`-FilePath`/optional `-FuncPath` contract. It now requires one literal
`-CliPath` and uses that same executable for identity, version, verbose
metadata, and the reproduction command. It never resolves `func` or `func5`
from `PATH`.

Prepare the expected configuration from the live wiki and approved disposable
paths. Values are compared in memory and are not written to evidence:

```powershell
$ExpectedConfiguration = [ordered]@{
    FUNC_CLI_WORKLOADS_SOURCE = '<current live-wiki value>'
    FUNC_CLI_QUICKSTART_MANIFEST_URL = '<current live-wiki value>'
    FUNC_CLI_WORKLOADS_PRERELEASE = '<current live-wiki value>'
    FUNC_CLI_HOME = $CliHome
    FUNC_CLI_WORKLOADS_HOME = $WorkloadsHome
} | ConvertTo-Json -Compress

$WorkloadVersions = [ordered]@{
    '<relevant workload or package>' = '<actual installed version>'
} | ConvertTo-Json -Compress

pwsh -NoProfile -File ./scripts/capture-evidence.ps1 `
  -CliPath $CliPath `
  -ExpectedCliVersion $ExpectedVersion `
  -ExpectedInstallPath $InstallPath `
  -ExpectedConfigurationJson $ExpectedConfiguration `
  -ArgumentsJson '["workload","search","node","--prerelease","--json"]' `
  -Stack node `
  -HostVersion '<actual Host version, if relevant>' `
  -WorkloadVersionsJson $WorkloadVersions `
  -TargetProfile '<target profile, if relevant>' `
  -TemplateId '<template ID, if relevant>' `
  -OutputDirectory $EvidenceDirectory
```

`-ArgumentsJson` and `-ExpectedConfigurationJson` work through `pwsh -File`
from PowerShell, bash, or zsh. When invoking the script directly in an existing
PowerShell process, `-ArgumentList @('...')` is also supported.

Before executing the reproduction, the helper:

- requires `CliPath` to be a literal file, not a command name or alias;
- resolves symbolic links and verifies the resulting file is under
  `ExpectedInstallPath` before launching it;
- verifies `--version` exactly matches `ExpectedCliVersion`;
- compares only the five allowlisted settings with the caller-supplied expected
  values, normalizing isolation paths without emitting them;
- refuses credential-bearing arguments and opaque shell executables.

An executable outside the approved root or a session with mismatched settings
is never launched. A version mismatch stops after the `--version` check. If any
gate fails, the reproduction is not run. A local evidence file records only
status (`set`, `missing`, or `mismatch`) and the failed gate. The helper captures
output in memory, applies conservative redaction, writes `evidence.md`, and
uploads nothing.

The helper is for finite, non-interactive commands. For host runs,
cancellation, prompts, or dashboard behavior, use a visible terminal and
collect a small reviewed transcript manually.

### Bash-only finite capture

PowerShell 7 is optional on Linux/macOS. If it is unavailable, first perform the
same literal-path, install-root, exact-version, and five-setting checks in the
current Bash process without printing setting values. Stop on any failure.

After consent to create the evidence directory, invoke the literal executable
directly and keep streams separate. Do not use `eval`, `bash -c`, a `func5`
function, or a command assembled in one string:

```bash
mkdir -- "$EVIDENCE_DIR" || exit 1
"$CLI_PATH" --verbose >"$EVIDENCE_DIR/func-verbose.stdout.txt" \
  2>"$EVIDENCE_DIR/func-verbose.stderr.txt" || exit 1

set +e
"$CLI_PATH" workload search node --prerelease --json \
  >"$EVIDENCE_DIR/command.stdout.txt" \
  2>"$EVIDENCE_DIR/command.stderr.txt"
COMMAND_EXIT=$?
set -e
printf '%s\n' "$COMMAND_EXIT" >"$EVIDENCE_DIR/command.exit-code.txt"
```

Replace the sample arguments with individually reviewed tokens. Record the
literal command separately, add OS/architecture/shell and only relevant runtime
versions, then review every file before copying excerpts into the report. This
fallback performs no automatic redaction. If any file contains sensitive or
unrelated data, do not share it; create a synthetic reproduction or a redacted
copy and retain the raw file locally only until the user approves deletion.

## Screenshots and recordings

1. Ask the user to capture or select a specific window or region. On Windows,
   `Win+Shift+S` opens the user-controlled snipping UI. On macOS,
   `Shift+Command+4` selects a region and `Shift+Command+5` opens capture
   controls. Use the participant's preferred interactive capture tool on Linux.
2. Save selected files in the evidence directory or another approved location.
3. List every exact path, type, size, and proposed alt text. Preview each image
   when the host supports it.
4. Check notifications, account names, paths, browser tabs, tokens, customer
   data, and unrelated windows. Crop or redact locally if needed.
5. Require explicit confirmation of the final attachment list.

Never trigger an unattended full-desktop capture. Never upload a file merely
because it is present in the evidence directory.

## Discover the live issue form

Use the **File a CLI v5 bug** link from the live wiki. Read the referenced issue
form from the repository's default branch, not from this temporary skill branch.
Discover its current:

- template path;
- title prefix;
- default labels;
- field labels and required fields.

Do not assume a generic `[v5]` prefix or only the `v5` label. Add a
classification label such as `bug`, `enhancement`, or `documentation` only when
it exists and fits the report.

Copy [the report template](../assets/issue-report.md) to a local approved path
and populate it. Show the user:

- exact title including the discovered prefix;
- discovered default labels and any proposed classification label;
- complete body;
- evidence contents and gate statuses;
- attachment paths and alt text;
- redactions, omitted data, and unavailable prerequisites.

Require explicit confirmation after this preview. Earlier consent to install,
test, or capture evidence is not consent to publish.

## Submit only after approval

Use `gh` only when installed and authenticated for `github.com`:

```text
gh --version
gh auth status --hostname github.com
gh issue create --help
```

Prefer opening the exact wiki-linked form after confirmation so its current
schema, prefix, and labels are preserved. `gh issue create --web --template
<discovered-template>` may open it in the browser. The user still performs the
final browser submission and manually attaches only confirmed files.

If the user explicitly approves direct terminal creation, use the discovered
prefix and labels with a reviewed body file:

```text
gh issue create --repo Azure/azure-functions-core-tools --title "<discovered-prefix><concise title>" --body-file "<report.md>" --label "<discovered-label>" [...]
```

Use repeated `--attach "<path>#<alt text>"` only when the local
`gh issue create --help` documents it and the user confirmed each file. Some
`gh` versions create the issue even if an attachment fails, so inspect the exit
code, output, and returned URL before retrying.

After creation, open the issue and verify the body, labels, and attachments.
Report any dropped label or failed attachment instead of claiming success.

If `gh` is missing, unauthenticated, unsupported, or denied, save the Markdown
report locally and open the exact issue-form URL from the wiki. Do not put the
body or logs in query parameters.
