# Doc-Sync Workflow

Automated documentation change detection for Azure Functions Core Tools.

## What It Does

When a new release is published, this workflow:

1. **Extracts** CLI command metadata (names, help text, arguments) from the current and previous release
2. **Diffs** the two manifests to find added, removed, or modified commands
3. **Opens a PR** in [`MicrosoftDocs/azure-docs-pr`](https://github.com/MicrosoftDocs/azure-docs-pr) with a detailed change summary
4. **Assigns Copilot** to the PR so it can auto-generate documentation updates

## How Commands Are Detected

The extraction script (`.github/scripts/extract_commands.py`) parses:

- `[Action(...)]` attributes on C# classes in `src/Cli/func/Actions/`
- `.Setup<T>()` argument definitions in `ParseArgs()` methods

It extracts: command name, context, help text, visibility, arguments (name, type, description, defaults).

## Required Secrets

| Secret | Description |
|--------|-------------|
| `DOC_SYNC_APP_ID` | The GitHub App's numeric ID |
| `DOC_SYNC_APP_PRIVATE_KEY` | The GitHub App's private key (`.pem` file contents) |

### GitHub App Setup

The workflow uses a **GitHub App** for authentication (no PAT expiry to manage). The App is installed on the fork `liliankasem/azure-docs-pr` and generates short-lived tokens automatically.

1. Create a GitHub App at https://github.com/settings/apps/new with:
   - **Contents:** Read and write
   - **Pull requests:** Read and write
   - Webhooks disabled
2. Install the App on `liliankasem/azure-docs-pr`
3. Add the App ID and private key as secrets in `Azure/azure-functions-core-tools`:
   - `DOC_SYNC_APP_ID`
   - `DOC_SYNC_APP_PRIVATE_KEY`

### Fork Setup

The workflow pushes branches to a **fork** (`liliankasem/azure-docs-pr`) and opens PRs into the upstream (`MicrosoftDocs/azure-docs-pr`). This is required because the core-tools team doesn't have direct write access to the upstream docs repo.

> **If the workflow fails** (App misconfigured, missing fork, etc.), it automatically opens a GitHub Issue in this repo with the full change summary and troubleshooting steps.

## Triggering

- **Automatic:** Runs on every `release: published` event
- **Manual:** Use `workflow_dispatch` from the Actions tab. Optionally provide a `previous_tag` to override auto-detection.

## Local Testing

You can run the extraction script locally to preview what the workflow will detect:

```bash
# Extract current command manifest
python3 .github/scripts/extract_commands.py . --output commands.json

# Diff against a saved manifest from a previous release
python3 .github/scripts/extract_commands.py . --diff old_commands.json --summary
```

## Files

| File | Purpose |
|------|---------|
| `.github/workflows/doc-sync.yml` | The GitHub Actions workflow |
| `.github/scripts/extract_commands.py` | Command metadata extraction and diffing script |
| `docs/doc-sync.md` | This file |
