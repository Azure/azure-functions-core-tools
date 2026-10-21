# Evidence, Screenshots, and Issue Submission

Target repository:
<https://github.com/Azure/azure-functions-core-tools>

## Evidence boundary

Collect only what is needed to reproduce and triage the observed behavior:

- exact command tokens;
- exit code;
- separate stdout and stderr;
- `func --version` and `func --verbose`;
- OS and architecture;
- shell;
- relevant stack runtime versions;
- whether each required bug-bash variable is set, missing, or mismatched.

Do not collect the full environment, command history, profile contents, home
directory listing, unrelated logs, customer files, or cloud resource data.

Use synthetic projects and inputs. If a command or output contains a secret,
token, key, connection string, customer identifier, or private endpoint, do not
capture it. Reproduce with synthetic data or replace the sensitive material
with a clear `[REDACTED]` marker before it enters the report.

## Finite command capture

With PowerShell 7, run the helper from the installed skill directory:

```powershell
pwsh -NoProfile -File ./scripts/capture-evidence.ps1 `
  -FilePath func `
  -ArgumentsJson '["workload","install","bogus-package"]' `
  -Stack node `
  -OutputDirectory ./func-v5-evidence
```

`-ArgumentsJson` works from PowerShell, bash, and zsh. When invoking the script
directly inside an existing PowerShell session, `-ArgumentList
@('workload', 'install', 'bogus-package')` is also supported.

The helper:

- invokes the executable directly instead of evaluating a shell string;
- refuses command arguments that look credential-bearing;
- captures stdout and stderr in memory, then applies conservative redaction
  before writing;
- records only allowlisted environment-variable presence, never values;
- creates `evidence.md` locally and uploads nothing.

It is intended for finite, non-interactive commands. For `func run`,
cancellation, prompts, or TUI behavior, use a visible terminal and collect a
small, user-reviewed transcript manually. The helper's redaction is defense in
depth, not a substitute for reviewing every line.

## Screenshots and recordings

1. Ask the user to capture or select a specific window or region. On Windows,
   `Win+Shift+S` opens the user-controlled snipping UI. On macOS,
   `Shift+Command+4` selects a region and `Shift+Command+5` opens capture
   controls.
2. Save selected files in the evidence directory or another user-approved
   location.
3. List each exact path, type, and size. Preview each image when the host
   supports it.
4. Check for notifications, account names, paths, browser tabs, tokens,
   customer data, and unrelated windows. Crop or redact locally if needed.
5. Require explicit confirmation of the final attachment list and alt text.

Never trigger an unattended full-desktop capture. Never upload a file merely
because it is present in the evidence directory.

## Prepare the report

Copy [the report template](../assets/issue-report.md) to a local, user-approved
path and replace every placeholder. Keep the title concise, normally:

```text
[v5] <area>: <specific symptom>
```

Choose one classification:

| Report | Preferred labels |
| --- | --- |
| Defect, crash, wrong output, or regression | `v5`, `bug` |
| Product feedback or requested behavior | `v5`, `enhancement` |
| Missing, incorrect, or confusing documentation | `v5`, `documentation` |

Before submission, query the repository's current labels and use only names that
exist. Do not silently substitute a different label. Show the user:

- title;
- labels;
- complete body;
- evidence contents;
- attachment paths and alt text;
- any redactions or omitted data.

Require an explicit confirmation after this preview. Earlier consent to install,
test, or capture evidence is not consent to publish.

## Submit with GitHub CLI

Use `gh` only when it is installed and authenticated for `github.com`.
Capability-check the local version rather than assuming attachment support:

```text
gh --version
gh auth status --hostname github.com
gh issue create --help
```

After confirmation, create the issue with a body file and separate labels:

```text
gh issue create --repo Azure/azure-functions-core-tools --title "<title>" --body-file "<report.md>" --label v5 --label "<classification>"
```

If `gh issue create --help` documents `--attach`, selected images or videos may
be added with repeated `--attach "<path>#<alt text>"` flags in that same,
confirmed create command. Do not use an undocumented upload endpoint. Be aware
that supported `gh` versions can create the issue even when one attachment
fails; inspect the exit code, stdout, stderr, and printed issue URL before
retrying.

After creation:

1. open the returned issue;
2. verify the body and attachments;
3. query the issue labels and verify `v5` plus the classification;
4. report any label dropped because the account lacks permission instead of
   claiming success.

## Browser/manual fallback

If `gh` is missing, unauthenticated, too old, or denied:

1. save the complete report as local Markdown;
2. retain selected attachments locally;
3. open
   `https://github.com/Azure/azure-functions-core-tools/issues/new?labels=v5%2C<classification>`;
4. paste the title and report in the browser;
5. attach only the confirmed files through GitHub's visible upload control;
6. verify labels after creation.

Do not put the body or logs in query parameters. If labels are unavailable to
the participant, add a short `Requested labels: v5, <classification>` note to
the report and state that repository triage must apply them.
