#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory=$true)]
    [ValidatePattern('^\d+\.\d+$')]
    [string]$PythonVersion
)

$ErrorActionPreference = 'Stop'

$packageId = 'Azure.Functions.Cli.Workloads.Workers.Python'
$runtimePayloadPaths = [ordered]@{
    'win-x64' = 'WINDOWS/X64'
    'win-arm64' = 'WINDOWS/Arm64'
    'linux-x64' = 'LINUX/X64'
    'linux-arm64' = 'LINUX/Arm64'
    'osx-x64' = 'OSX/X64'
    'osx-arm64' = 'OSX/Arm64'
}

function Read-WorkloadManifest {
    param([System.IO.FileInfo]$Package)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $entry = $archive.GetEntry('workload.json')
        if ($null -eq $entry) {
            throw "Package '$($Package.Name)' does not contain workload.json."
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            return ConvertFrom-Json -InputObject $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
    } finally {
        $archive.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Package directory '$PackageDirectory' does not exist."
}

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter "$packageId.*.nupkg" -File |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' })

$ridPrefixes = @($runtimePayloadPaths.Keys | ForEach-Object { "$packageId.$_." })
$pointerPackages = @($packages | Where-Object {
    $name = $_.Name
    -not ($ridPrefixes | Where-Object { $name.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
})

if ($pointerPackages.Count -ne 1) {
    throw "Expected exactly one Python worker RID pointer package in '$PackageDirectory', found $($pointerPackages.Count)."
}

$pointerManifest = Read-WorkloadManifest -Package $pointerPackages[0]
if ($pointerManifest.kind -ne 'rid-pointer') {
    throw "Package '$($pointerPackages[0].Name)' has workload kind '$($pointerManifest.kind)', expected 'rid-pointer'."
}

foreach ($runtimeIdentifier in $runtimePayloadPaths.Keys) {
    $expectedPackageId = "$packageId.$runtimeIdentifier"
    $referencedPackageId = $pointerManifest.packages.PSObject.Properties[$runtimeIdentifier].Value
    if ($referencedPackageId -ne $expectedPackageId) {
        throw "RID pointer maps '$runtimeIdentifier' to '$referencedPackageId', expected '$expectedPackageId'."
    }

    $ridPackages = @($packages | Where-Object {
        $_.Name.StartsWith("$expectedPackageId.", [StringComparison]::OrdinalIgnoreCase)
    })
    if ($ridPackages.Count -ne 1) {
        throw "Expected exactly one '$runtimeIdentifier' Python worker package, found $($ridPackages.Count)."
    }

    $ridPackage = $ridPackages[0]
    $manifest = Read-WorkloadManifest -Package $ridPackage
    if ($manifest.kind -ne 'content' -or $manifest.runtimeIdentifier -ne $runtimeIdentifier) {
        throw "Package '$($ridPackage.Name)' has invalid workload metadata for '$runtimeIdentifier'."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ridPackage.FullName)
    try {
        if ($runtimeIdentifier -eq 'win-arm64') {
            $packagedVersions = @($archive.Entries.FullName |
                Where-Object { $_ -match '^tools/win-arm64/(?<version>\d+\.\d+)/' } |
                ForEach-Object { [regex]::Match($_, '^tools/win-arm64/(?<version>\d+\.\d+)/').Groups['version'].Value } |
                Sort-Object -Unique)
            if ($packagedVersions.Count -ne 1 -or $packagedVersions[0] -ne '3.15') {
                throw "Package '$($ridPackage.Name)' must contain only the Python 3.15 version directory; found '$($packagedVersions -join ', ')'."
            }
        }

        $workerConfigPath = "tools/$runtimeIdentifier/worker.config.json"
        $workerPath = "tools/$runtimeIdentifier/$PythonVersion/$($runtimePayloadPaths[$runtimeIdentifier])/worker.py"
        foreach ($requiredPath in @($workerConfigPath, $workerPath)) {
            if ($null -eq $archive.GetEntry($requiredPath)) {
                throw "Package '$($ridPackage.Name)' does not contain '$requiredPath'."
            }
        }
    } finally {
        $archive.Dispose()
    }
}

Write-Host "Validated Python $PythonVersion worker payloads for $($runtimePayloadPaths.Count) runtime identifiers."