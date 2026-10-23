param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [Parameter(Mandatory)]
    [string] $Version,
    [Parameter(Mandatory)]
    [string[]] $PackageNames,
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$Version = $Version.Trim().TrimStart('v', 'V')

# Packing needs no installed dependencies; prepack only copies repository metadata.
npm version $Version --offline --no-update-notifier --ignore-scripts --no-git-tag-version --allow-same-version --prefix $PackageDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Failed to set npm package version to '$Version'."
}

foreach ($packageName in $PackageNames) {
    npm pkg set "name=$packageName" --offline --no-update-notifier --prefix $PackageDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set npm package name to '$packageName'."
    }

    # Keep the locked dependency graph intact instead of invoking npm install.
    $shrinkwrapPath = Join-Path $PackageDirectory 'npm-shrinkwrap.json'
    $shrinkwrap = Get-Content -LiteralPath $shrinkwrapPath -Raw | ConvertFrom-Json -AsHashtable
    $shrinkwrap.name = $packageName
    $shrinkwrap.packages[''].name = $packageName
    $shrinkwrap | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $shrinkwrapPath -Encoding utf8NoBOM

    $packageOutputDirectory = Join-Path $OutputDirectory $packageName
    New-Item -ItemType Directory -Path $packageOutputDirectory -Force | Out-Null
    npm pack $PackageDirectory --offline --no-update-notifier --pack-destination $packageOutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create the '$packageName' npm package."
    }
}
