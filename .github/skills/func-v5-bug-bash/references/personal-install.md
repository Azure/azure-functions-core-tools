# Install This Temporary Skill for GitHub Copilot

GitHub's current Agent Skills documentation defines personal Copilot skills
under `~/.copilot/skills/<skill-name>` on Windows, Linux, and macOS:

- <https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills>
- <https://agentskills.io/specification>
- <https://cli.github.com/manual/gh_skill_install>

This skill is intentionally hosted on the temporary branch
`fabiocav/func-v5-bug-bash-skill`, not the repository's default branch.

## Preferred: GitHub CLI 2.90 or later

Resolve the branch to an immutable commit, preview the exact hidden skill, and
install that reviewed commit at Copilot user scope.

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

Bash/zsh:

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

Inspect the preview before installing. Start a new Copilot session afterward so
the skill inventory refreshes.

## Manual download and copy

This fallback downloads an archive and does not execute a remote script. Review
the extracted skill directory before copying it. Set `sha` to the immutable
commit that was reviewed or supplied by the bug-bash coordinator; do not use a
moving branch archive.

PowerShell:

```powershell
$sha = '<reviewed-commit-sha>'
$uri = "https://codeload.github.com/Azure/azure-functions-core-tools/zip/$sha"
$id = [guid]::NewGuid()
$archive = Join-Path ([IO.Path]::GetTempPath()) "func-v5-bug-bash-$id.zip"
$extract = Join-Path ([IO.Path]::GetTempPath()) "func-v5-bug-bash-$id"
$destinationRoot = Join-Path $HOME '.copilot\skills'
$destination = Join-Path $destinationRoot 'func-v5-bug-bash'

if (Test-Path -LiteralPath $destination) {
    throw "Destination already exists: $destination"
}

Invoke-WebRequest -Uri $uri -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $extract
$root = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
$source = Join-Path $root.FullName '.github\skills\func-v5-bug-bash'
Get-ChildItem -LiteralPath $source -Recurse

New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Recurse
Remove-Item -LiteralPath $archive -Force
Remove-Item -LiteralPath $extract -Recurse -Force
```

Bash/zsh on Linux or macOS:

```bash
set -eu
tmp="$(mktemp -d)"
archive="$tmp/func-v5-bug-bash.tar.gz"
extract="$tmp/extracted"
destination="$HOME/.copilot/skills/func-v5-bug-bash"
sha='<reviewed-commit-sha>'
uri="https://codeload.github.com/Azure/azure-functions-core-tools/tar.gz/$sha"

test ! -e "$destination" || { printf 'Destination already exists: %s\n' "$destination" >&2; exit 1; }
curl --fail --location "$uri" --output "$archive"
mkdir -p "$extract"
tar -xzf "$archive" -C "$extract"
root="$(find "$extract" -mindepth 1 -maxdepth 1 -type d -print -quit)"
source="$root/.github/skills/func-v5-bug-bash"
find "$source" -maxdepth 2 -type f -print

mkdir -p "$HOME/.copilot/skills"
cp -R "$source" "$destination"
rm -rf -- "$tmp"
```

## Update

Resolve the branch again, preview the new commit, then reinstall:

```text
gh skill install Azure/azure-functions-core-tools func-v5-bug-bash --pin <new-commit-sha> --agent github-copilot --scope user --allow-hidden-dirs --force
```

Pinned skills are intentionally skipped by a general `gh skill update`. For a
manual installation, download to a temporary directory, review changes, then
replace only `~/.copilot/skills/func-v5-bug-bash`.

## Uninstall

Inspect the exact target, then remove only that skill directory.

PowerShell:

```powershell
$skillPath = Join-Path $HOME '.copilot\skills\func-v5-bug-bash'
Get-ChildItem -LiteralPath $skillPath -Recurse
Remove-Item -LiteralPath $skillPath -Recurse -Force
```

Bash/zsh:

```bash
skill_path="$HOME/.copilot/skills/func-v5-bug-bash"
find "$skill_path" -maxdepth 2 -type f -print
rm -rf -- "$skill_path"
```

Start a new Copilot session after updating or uninstalling.
