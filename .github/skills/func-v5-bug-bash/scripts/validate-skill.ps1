#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$skillRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = (Resolve-Path (Join-Path $skillRoot '../../..')).Path
$skillName = Split-Path -Leaf $skillRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([string] $Message)

    $errors.Add($Message)
}

$requiredFiles = @(
    'SKILL.md',
    'assets/issue-report.md',
    'references/personal-install.md',
    'references/reporting.md',
    'references/workflow.md',
    'scripts/capture-evidence.ps1',
    'scripts/validate-skill.ps1'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $skillRoot $relativePath) -PathType Leaf)) {
        Add-ValidationError "Missing required file: $relativePath"
    }
}

$skillPath = Join-Path $skillRoot 'SKILL.md'
if (Test-Path -LiteralPath $skillPath -PathType Leaf) {
    $skillContent = Get-Content -LiteralPath $skillPath -Raw
    $frontmatter = [regex]::Match($skillContent, '\A---\r?\n(?<yaml>.*?)\r?\n---\r?\n', 'Singleline')
    if (-not $frontmatter.Success) {
        Add-ValidationError 'SKILL.md must start with YAML frontmatter.'
    }
    else {
        $yaml = $frontmatter.Groups['yaml'].Value
        $nameMatch = [regex]::Match($yaml, '(?m)^name:\s*[''"]?(?<value>[^''"\r\n]+)[''"]?\s*$')
        $descriptionMatch = [regex]::Match($yaml, '(?m)^description:\s*[''"](?<value>.*)[''"]\s*$')

        if (-not $nameMatch.Success) {
            Add-ValidationError 'Frontmatter must include name.'
        }
        else {
            $declaredName = $nameMatch.Groups['value'].Value
            if ($declaredName -ne $skillName) {
                Add-ValidationError "Frontmatter name '$declaredName' must match directory '$skillName'."
            }
            if ($declaredName.Length -gt 64 -or $declaredName -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
                Add-ValidationError "Frontmatter name '$declaredName' violates the Agent Skills naming rules."
            }
        }

        if (-not $descriptionMatch.Success) {
            Add-ValidationError 'Frontmatter must include a quoted, single-line description.'
        }
        elseif ($descriptionMatch.Groups['value'].Value.Length -gt 1024) {
            Add-ValidationError 'Frontmatter description exceeds 1024 characters.'
        }
    }

    $lineCount = (Get-Content -LiteralPath $skillPath).Count
    if ($lineCount -ge 500) {
        Add-ValidationError "SKILL.md has $lineCount lines; keep it under 500."
    }

}

$markdownFiles = Get-ChildItem -LiteralPath $skillRoot -Filter '*.md' -File -Recurse
foreach ($markdownFile in $markdownFiles) {
    $markdownContent = Get-Content -LiteralPath $markdownFile.FullName -Raw
    $localLinks = [regex]::Matches(
        $markdownContent,
        '\]\((?<path>(?!https?://|mailto:|#)[^)]+)\)')
    foreach ($link in $localLinks) {
        $target = $link.Groups['path'].Value.Split('#')[0]
        $targetPath = Join-Path $markdownFile.DirectoryName $target
        $isBroken = -not [string]::IsNullOrWhiteSpace($target) -and -not (Test-Path -LiteralPath $targetPath)
        if ($isBroken) {
            $relativeMarkdownPath = [System.IO.Path]::GetRelativePath($skillRoot, $markdownFile.FullName)
            Add-ValidationError "Broken local link in ${relativeMarkdownPath}: $target"
        }
    }
}

$parseTargets = Get-ChildItem -LiteralPath (Join-Path $skillRoot 'scripts') -Filter '*.ps1' -File
foreach ($script in $parseTargets) {
    $tokens = $null
    $parseErrors = $null
    [void] [System.Management.Automation.Language.Parser]::ParseFile(
        $script.FullName,
        [ref] $tokens,
        [ref] $parseErrors)
    foreach ($parseError in $parseErrors) {
        Add-ValidationError "$($script.Name): $($parseError.Message) at line $($parseError.Extent.StartLineNumber)"
    }
}

$agentsPath = Join-Path $repoRoot 'AGENTS.md'
if (-not (Select-String -LiteralPath $agentsPath -SimpleMatch 'func-v5-bug-bash' -Quiet)) {
    Add-ValidationError 'Root AGENTS.md does not list func-v5-bug-bash.'
}

$claudeRelativePath = '.claude/skills/func-v5-bug-bash'
$indexEntry = & git -C $repoRoot ls-files --stage -- $claudeRelativePath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($indexEntry)) {
    Add-ValidationError "Missing indexed Claude skill link: $claudeRelativePath"
}
elseif ($indexEntry -notmatch '^120000\s') {
    Add-ValidationError "$claudeRelativePath must be a Git symlink (mode 120000)."
}
else {
    $target = (& git -C $repoRoot show ":$claudeRelativePath").Trim()
    if ($LASTEXITCODE -ne 0 -or $target -ne '../../.github/skills/func-v5-bug-bash') {
        Add-ValidationError "$claudeRelativePath points to '$target'."
    }
}

if ($errors.Count -gt 0) {
    Write-Host 'Skill validation failed:' -ForegroundColor Red
    foreach ($validationError in $errors) {
        Write-Host " - $validationError" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Validated ${skillName}: frontmatter, local links, required paths, PowerShell syntax, root index, and Claude symlink."
