[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackageRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'upstream-sources.json') -Raw | ConvertFrom-Json
$qualifiedConfigurations = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'upstream-configurations.json') -Raw | ConvertFrom-Json -NoEnumerate
$output = [IO.Path]::GetFullPath($OutputDirectory)
$separator = [IO.Path]::DirectorySeparatorChar
$outputPrefix = $output.TrimEnd($separator) + $separator

# Check every input before replacing the generated staging directory.
foreach ($package in $sources.packages) {
    $id = $package.id.ToLowerInvariant()
    $archive = Join-Path $PackageRoot "$id/$($package.version)/$id.$($package.version).nupkg"
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -cne $package.sha256) {
        throw "Upstream template archive digest does not match the declared baseline: $($package.id) $($package.version)."
    }
}

if (Test-Path -LiteralPath $output) {
    # Delete only this pack target's dedicated generated leaf, never its parent.
    if ([IO.Path]::GetFileName($output.TrimEnd($separator)) -cne 'native-dotnet') {
        throw 'The staging directory must be the dedicated native-dotnet leaf.'
    }
    Remove-Item -LiteralPath $output -Recurse -Force
}
[IO.Directory]::CreateDirectory($output) | Out-Null

foreach ($package in $sources.packages) {
    $id = $package.id.ToLowerInvariant()
    $archive = Join-Path $PackageRoot "$id/$($package.version)/$id.$($package.version).nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $configurations = @($zip.Entries | Where-Object FullName -like 'content/*/.template.config/template.json')
        if ($configurations.Count -ne $package.rawConfigurationCount) {
            throw "Unexpected raw configuration inventory in $($package.id)."
        }
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.StartsWith('content/', [StringComparison]::Ordinal)) {
                $relative = $entry.FullName.Replace('/', $separator)
            } elseif ($entry.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal)) {
                $relative = "provenance/$($package.id).nuspec.xml".Replace('/', $separator)
            } else {
                continue
            }
            $target = [IO.Path]::GetFullPath([IO.Path]::Combine($output, $relative))
            if (-not $target.StartsWith($outputPrefix, [StringComparison]::Ordinal) -or $entry.FullName.Contains('\')) {
                throw "Upstream archive contains an unsafe entry: $($entry.FullName)."
            }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            if ($entry.Name.Length -gt 0) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
            }
        }
        foreach ($entry in $configurations) {
            $configPath = Join-Path $output $entry.FullName
            $configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -AsHashtable
            if ($configuration.tags.type -cne $package.type -or $configuration.tags.language -cnotin @('C#', 'F#')) {
                throw "Unexpected .NET template type or language at $($entry.FullName)."
            }
            $qualified = @($qualifiedConfigurations | Where-Object { $_[0] -ceq $configuration.identity })
            if ($qualified.Count -ne 1 -or $qualified[0][1] -cne $package.type -or $qualified[0][2] -cne $configuration.tags.language -or
                $qualified[0][3] -cne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
                throw "Native configuration does not match the qualified CLI adapter source: $($entry.FullName)."
            }
            # Keep template.json and every source byte unchanged. CLI aliases live in host metadata only.
            $hostMetadata = @{ symbolInfo = @{} }
            if ($package.type -ceq 'project') {
                $hostMetadata.symbolInfo.Framework = @{ longName = 'target-framework'; shortName = 'tfm' }
            }
            $hostPath = Join-Path ([IO.Path]::GetDirectoryName($configPath)) 'func.host.json'
            $hostMetadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $hostPath -Encoding utf8NoBOM
        }
    } finally {
        $zip.Dispose()
    }
}
