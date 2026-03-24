# Doc-Sync Workflow

Automated documentation change detection for Azure Functions Core Tools.

When a new release is published, this workflow extracts CLI command metadata, diffs it against the previous release, and opens a PR in the docs repo with a detailed change summary.

## How It Works

```
Release published
  → extract-and-diff job: parses [Action(...)] attributes + .Setup<T>() args from C# source
  → open-docs-pr job: pushes branch to fork, opens PR into upstream docs repo
  → fallback-issue job: if PR fails, opens a GitHub Issue here with the changes + troubleshooting
```

### What It Detects

The extraction script (`.github/scripts/extract_commands.py`) parses:

- `[Action(...)]` attributes on C# classes in `src/Cli/func/Actions/`
- `.Setup<T>()` argument definitions in `ParseArgs()` methods

It extracts: command name, context, help text, visibility, arguments (name, type, description, defaults).

## Current Configuration

### Repository Variables (Settings → Variables → Actions)

| Variable | Current Value | Purpose |
|----------|---------------|---------|
| `DOCS_FORK_REPO` | `liliankasem/azure-docs-pr` | Fork where branches are pushed (head of the PR) |
| `DOCS_UPSTREAM_REPO` | `MicrosoftDocs/azure-docs-pr` | Upstream docs repo where the PR targets (base of the PR) |
| `DOC_SYNC_REVIEWERS` | `liliankasem` | Comma-separated GitHub usernames to @mention on notification issues |

> If these variables are not set, the workflow falls back to the defaults shown above.

### Repository Secrets (Settings → Secrets → Actions)

| Secret | Purpose |
|--------|---------|
| `DOC_SYNC_APP_ID` | The GitHub App's numeric ID |
| `DOC_SYNC_APP_PRIVATE_KEY` | The GitHub App's private key (`.pem` file contents) |

## Setup Guide

### 1. Create the GitHub App

1. Go to **https://github.com/settings/apps/new** (logged in as the fork owner)
2. Fill in:
   - **App name:** `core-tools-doc-sync` (must be globally unique)
   - **Homepage URL:** `https://github.com/Azure/azure-functions-core-tools`
   - **Webhook:** Uncheck **Active** (not needed)
3. Under **Permissions → Repository permissions**, set:
   - **Contents:** Read and write
   - **Pull requests:** Read and write
   - **Metadata:** Read-only (auto-selected)
4. Under **Where can this GitHub App be installed?** → select **Only on this account**
5. Click **Create GitHub App**
6. On the App page, note the **App ID** (number near the top)
7. Scroll to **Private keys** → click **Generate a private key** → save the downloaded `.pem` file

### 2. Install the App on the fork

1. Go to **https://github.com/settings/apps** → find your app → click **Edit**
2. In the left sidebar, click **Install App**
3. Click **Install** next to your account
4. Select **Only select repositories** → choose the fork repo (e.g., `azure-docs-pr`)
5. Click **Install**

### 3. Add secrets to `azure-functions-core-tools`

Via CLI:

```bash
# App ID (just the number, e.g., 123456)
gh secret set DOC_SYNC_APP_ID --repo Azure/azure-functions-core-tools

# Private key (the .pem file)
gh secret set DOC_SYNC_APP_PRIVATE_KEY --repo Azure/azure-functions-core-tools < ~/Downloads/core-tools-doc-sync.*.pem
```

Or via the UI: **https://github.com/Azure/azure-functions-core-tools/settings/secrets/actions** → **New repository secret**

### 4. Set repository variables

Via CLI:

```bash
gh variable set DOCS_FORK_REPO --repo Azure/azure-functions-core-tools --body "liliankasem/azure-docs-pr"
gh variable set DOCS_UPSTREAM_REPO --repo Azure/azure-functions-core-tools --body "MicrosoftDocs/azure-docs-pr"
gh variable set DOC_SYNC_REVIEWERS --repo Azure/azure-functions-core-tools --body "liliankasem"
```

Or via the UI: **https://github.com/Azure/azure-functions-core-tools/settings/variables/actions** → **New repository variable**

## Changing the Fork

If the fork owner leaves the team or you need to switch to a different fork (e.g., from `liliankasem/azure-docs-pr` to `azfuncgh/azure-docs-pr`), follow these steps. **No code changes are needed.**

### Step-by-step

1. **Create or identify the new fork** of `MicrosoftDocs/azure-docs-pr` under the new owner/org

2. **Create a new GitHub App** under the new fork owner's account (see [Setup Guide](#1-create-the-github-app) above), or transfer the existing App if same owner

3. **Install the App** on the new fork repo (see [Step 2](#2-install-the-app-on-the-fork) above)

4. **Update the secrets** in `Azure/azure-functions-core-tools`:
   ```bash
   gh secret set DOC_SYNC_APP_ID --repo Azure/azure-functions-core-tools
   gh secret set DOC_SYNC_APP_PRIVATE_KEY --repo Azure/azure-functions-core-tools < new-app.pem
   ```

5. **Update the repository variable:**
   ```bash
   gh variable set DOCS_FORK_REPO --repo Azure/azure-functions-core-tools --body "new-owner/azure-docs-pr"
   ```

6. **Test** by triggering the workflow manually from the Actions tab using `workflow_dispatch`

### Checklist

- [ ] New fork exists and is synced with upstream
- [ ] GitHub App created under new fork owner
- [ ] App installed on new fork repo with Contents + Pull requests permissions
- [ ] `DOC_SYNC_APP_ID` secret updated
- [ ] `DOC_SYNC_APP_PRIVATE_KEY` secret updated
- [ ] `DOCS_FORK_REPO` variable updated
- [ ] `DOC_SYNC_REVIEWERS` variable updated with new reviewer usernames
- [ ] Workflow tested via `workflow_dispatch`

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

## Troubleshooting

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| Workflow can't checkout fork | App not installed on fork, or wrong `DOCS_FORK_REPO` | Verify App installation and variable value |
| Can't push branch to fork | App missing `Contents: write` permission | Edit App permissions at https://github.com/settings/apps |
| Can't open PR in upstream | Fork owner can't open PRs in upstream repo | Request access from MicrosoftDocs team |
| Branch already exists | Re-run for same release tag | Delete the old branch from the fork first |
| No workflow in Actions tab | Workflow not on `main` yet | Merge the workflow PR first |

## Files

| File | Purpose |
|------|---------|
| `.github/workflows/doc-sync.yml` | The GitHub Actions workflow |
| `.github/scripts/extract_commands.py` | Command metadata extraction and diffing script |
| `docs/doc-sync.md` | This file |
