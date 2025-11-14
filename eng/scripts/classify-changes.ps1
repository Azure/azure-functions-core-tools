# eng/ci/scripts/classify-changes.ps1

# Read from environment variables provided by Azure DevOps
$buildReason  = $env:BUILD_REASON
$targetBranch = $env:SYSTEM_PULLREQUEST_TARGETBRANCH
$sourceBranch = $env:SYSTEM_PULLREQUEST_SOURCEBRANCH

Write-Host "BuildReason: $buildReason"
Write-Host "TargetBranch: $targetBranch"
Write-Host "SourceBranch: $sourceBranch"

# Default: not docs-only (for non-PR builds, schedules, etc.)
if ($buildReason -ne "PullRequest") {
    Write-Host "Non-PR build, forcing DocsOnly = false"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

if (-not $targetBranch) {
    Write-Host "No target branch provided, defaulting DocsOnly = false"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

# Normalize target branch (AzDO usually gives refs/heads/main)
$normalizedTarget = $targetBranch
if ($normalizedTarget.StartsWith("refs/heads/")) {
    $normalizedTarget = $normalizedTarget.Substring("refs/heads/".Length)
}
Write-Host "Normalized target branch: $normalizedTarget"

# Fetch that branch
Write-Host "Fetching target branch: origin/$normalizedTarget"
git fetch origin $normalizedTarget --depth=1

# Get changed files between origin/<target> and HEAD
Write-Host "Computing changed files vs origin/$normalizedTarget..."
$changedFiles = git diff --name-only "origin/$normalizedTarget" "HEAD"

if (-not $changedFiles -or $changedFiles.Count -eq 0) {
    Write-Host "No changed files detected, treat as non-docs-only"
    Write-Host "##vso[task.setvariable variable=DocsOnly;isOutput=true]false"
    exit 0
}

Write-Host "Changed files:"
$changedFiles | ForEach-Object { Write-Host " - $_" }

$docsOnly = $true

foreach ($file in $changedFiles) {
    $leaf      = [System.IO.Path]::GetFileName($file)
    $lowerFile = $file.ToLowerInvariant()

    # Docs / meta rules:
    $isDocsPath    = $file.StartsWith("docs/")
    $isMarkdown    = $lowerFile.EndsWith(".md")
    $isVsCodePath  = $file.StartsWith(".vscode/")
    $isGitHubPath  = $file.StartsWith(".github/")
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
