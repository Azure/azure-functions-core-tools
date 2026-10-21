# Install This Temporary Skill for GitHub Copilot

GitHub's current Agent Skills documentation defines personal Copilot skills
under `~/.copilot/skills/<skill-name>` on Windows and macOS:

- <https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills>
- <https://agentskills.io/specification>
- <https://cli.github.com/manual/gh_skill_install>

This skill is intentionally hosted on the temporary branch
`fabiocav/func-v5-bug-bash-skill`, not the repository's default branch.

## Preferred: GitHub CLI 2.90 or later

`gh skill` is a preview feature. Resolve the temporary branch to an immutable
commit, preview the exact skill tree, and install that reviewed commit at user
scope.

PowerShell:

```powershell
$repo = 'Azure/azure-functions-core-tools'
$branch = 'fabiocav/func-v5-bug-bash-skill'
$skillName = 'func-v5-bug-bash'
$skillPath = '.github/skills/func-v5-bug-bash/SKILL.md'
$sha = gh api "repos/$repo/commits/$branch" --jq '.sha'

gh skill preview $repo "$skillPath@$sha" --allow-hidden-dirs
gh skill install $repo $skillName --pin $sha --agent github-copilot --scope user --allow-hidden-dirs
gh skill list --agent github-copilot --scope user
```

bash/zsh:

```bash
repo='Azure/azure-functions-core-tools'
branch='fabiocav/func-v5-bug-bash-skill'
skill_name='func-v5-bug-bash'
skill_path='.github/skills/func-v5-bug-bash/SKILL.md'
sha="$(gh api "repos/$repo/commits/$branch" --jq '.sha')"

gh skill preview "$repo" "$skill_path@$sha" --allow-hidden-dirs
gh skill install "$repo" "$skill_name" --pin "$sha" --agent github-copilot --scope user --allow-hidden-dirs
gh skill list --agent github-copilot --scope user
```

Inspect the preview before installing. Start a new Copilot session after
installation so the skill inventory refreshes.

## Manual download and copy

This fallback downloads an archive; it does not execute a remote script. Review
the extracted `.github/skills/func-v5-bug-bash` directory before copying it.

PowerShell:

```powershell
$uri = 'https://codeload.github.com/Azure/azure-functions-core-tools/zip/refs/heads/fabiocav/func-v5-bug-bash-skill'
$archive = Join-Path ([IO.Path]::GetTempPath()) 'func-v5-bug-bash.zip'
$extract = Join-Path ([IO.Path]::GetTempPath()) "func-v5-bug-bash-$([guid]::NewGuid())"
$destinationRoot = Join-Path $HOME '.copilot\skills'
$destination = Join-Path $destinationRoot 'func-v5-bug-bash'

if (Test-Path -LiteralPath $destination) {
    throw "Destination already exists: $destination"
}

Invoke-WebRequest -Uri $uri -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $extract
$root = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
Get-ChildItem -LiteralPath (Join-Path $root.FullName '.github\skills\func-v5-bug-bash') -Recurse

New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root.FullName '.github\skills\func-v5-bug-bash') -Destination $destination -Recurse
Remove-Item -LiteralPath $archive -Force
Remove-Item -LiteralPath $extract -Recurse -Force
```

bash/zsh on macOS:

```bash
set -eu
tmp="$(mktemp -d)"
archive="$tmp/func-v5-bug-bash.zip"
extract="$tmp/extracted"
destination="$HOME/.copilot/skills/func-v5-bug-bash"
uri='https://codeload.github.com/Azure/azure-functions-core-tools/zip/refs/heads/fabiocav/func-v5-bug-bash-skill'

test ! -e "$destination" || { printf 'Destination already exists: %s\n' "$destination" >&2; exit 1; }
curl --fail --location "$uri" --output "$archive"
mkdir -p "$extract"
ditto -x -k "$archive" "$extract"
root="$(find "$extract" -mindepth 1 -maxdepth 1 -type d -print -quit)"
find "$root/.github/skills/func-v5-bug-bash" -maxdepth 2 -type f -print

mkdir -p "$HOME/.copilot/skills"
cp -R "$root/.github/skills/func-v5-bug-bash" "$destination"
rm -rf -- "$tmp"
```

## Update

With `gh`, resolve the branch again, preview the new commit, then reinstall:

```text
gh skill install Azure/azure-functions-core-tools func-v5-bug-bash --pin <new-commit-sha> --agent github-copilot --scope user --allow-hidden-dirs --force
```

Pinned skills are intentionally not changed by a general `gh skill update`.
For a manual installation, download to a temporary directory, review the
changes, then replace only
`~/.copilot/skills/func-v5-bug-bash`.

## Uninstall

First inspect the exact target. Then remove only that skill directory.

PowerShell:

```powershell
$skillPath = Join-Path $HOME '.copilot\skills\func-v5-bug-bash'
Get-ChildItem -LiteralPath $skillPath -Recurse
Remove-Item -LiteralPath $skillPath -Recurse -Force
```

bash/zsh:

```bash
skill_path="$HOME/.copilot/skills/func-v5-bug-bash"
find "$skill_path" -maxdepth 2 -type f -print
rm -rf -- "$skill_path"
```

Start a new Copilot session after updating or uninstalling.
