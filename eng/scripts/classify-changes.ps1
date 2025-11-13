param(
    [string]$BuildReason,
    [string]$TargetBranch,
    [string]$SourceBranch
)

Write-Host "BuildReason: $BuildReason"
Write-Host "TargetBranch: $TargetBranch"
Write-Host "SourceBranch: $SourceBranch"

# Default: not docs-only (for non-PR builds, schedules, etc.)
if ($BuildReason -ne "PullRequest") {
    Write-Host "Non-PR build, forcing DocsOnly = false"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

if (-not $TargetBranch) {
    Write-Host "No target branch provided, defaulting DocsOnly = false"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

# Ensure we have the target branch locally
Write-Host "Fetching target branch: origin/$TargetBranch"
git fetch origin $TargetBranch --depth=1

# List changed files between target and current HEAD
Write-Host "Computing changed files vs origin/$TargetBranch..."
$changedFiles = git diff --name-only "origin/$TargetBranch"...HEAD

if (-not $changedFiles -or $changedFiles.Count -eq 0) {
    Write-Host "No changed files detected, treat as non-docs-only"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

Write-Host "Changed files:"
$changedFiles | ForEach-Object { Write-Host " - $_" }

$docsOnly = $true

foreach ($file in $changedFiles) {
    $leaf = [System.IO.Path]::GetFileName($file)
    $lowerFile = $file.ToLowerInvariant()

    # Docs / meta rules:
    $isDocsPath   = $file.StartsWith("docs/")
    $isMarkdown   = $lowerFile.EndsWith(".md")
    $isVsCodePath = $file.StartsWith(".vscode/")
    $isGitHubPath = $file.StartsWith(".github/")
    $isLicenseLike = $leaf -in @('LICENSE', 'CODEOWNERS')

    $isDocsOrMeta = $isDocsPath -or $isMarkdown -or $isVsCodePath -or $isGitHubPath -or $isLicenseLike

    if (-not $isDocsOrMeta) {
        Write-Host "Found non-docs/meta file: $file"
        $docsOnly = $false
        break
    }
}

$docsOnlyString = if ($docsOnly) { "true" } else { "false" }

Write-Host "DocsOnly = $docsOnlyString"
Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]$docsOnlyString"
