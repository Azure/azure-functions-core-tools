# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string[]] $WorkloadArchives,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression

function Read-ArchiveBytes($archive, [string] $path) {
    $entry = $archive.GetEntry($path)
    if ($null -eq $entry) { throw "Missing owned archive entry '$path'." }
    $inputStream = $entry.Open()
    $outputStream = [System.IO.MemoryStream]::new()
    try {
        $inputStream.CopyTo($outputStream)
        return ,$outputStream.ToArray()
    }
    finally {
        $inputStream.Dispose()
        $outputStream.Dispose()
    }
}

function Get-Hash([byte[]] $bytes) {
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

$cohorts = @(
    foreach ($path in $WorkloadArchives) {
        $outer = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
        try {
            $assets = ([System.Text.Encoding]::UTF8.GetString((Read-ArchiveBytes $outer 'bundled-templates.json')) | ConvertFrom-Json).assets
            if ($assets.Count -ne 1 -or $assets[0].packageId -cne 'Azure.Functions.Templates.Node.Basic') {
                throw 'Qualification requires exactly the owned Node native asset.'
            }
            $bytes = Read-ArchiveBytes $outer $assets[0].path
            if ((Get-Hash $bytes) -ine $assets[0].sha256) { throw 'The bundled Node archive digest changed.' }
            $stream = [System.IO.MemoryStream]::new($bytes, $false)
            $inner = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read)
            try {
                $manifest = [System.Text.Encoding]::UTF8.GetString((Read-ArchiveBytes $inner 'provenance/node/compatibility.json')) | ConvertFrom-Json
                if ($manifest.packageId -cne $assets[0].packageId -or $manifest.packageVersion -cne $assets[0].version -or $manifest.sources.Count -ne 4) {
                    throw 'Node conversion provenance does not match the exact selected asset.'
                }
                foreach ($source in $manifest.sources) {
                    if ((Get-Hash (Read-ArchiveBytes $inner $source.path)) -ine $source.sha256) {
                        throw "The original Node source '$($source.path)' changed."
                    }
                }
                $original = [System.Text.Encoding]::UTF8.GetString((Read-ArchiveBytes $inner 'provenance/node/v2/templates/templates.json')) | ConvertFrom-Json -AsHashtable
                $templates = @(
                    foreach ($mapping in $manifest.templates) {
                        $config = Read-ArchiveBytes $inner $mapping.configuration.path
                        $hostConfig = Read-ArchiveBytes $inner $mapping.host.path
                        $content = Read-ArchiveBytes $inner $mapping.content.path
                        if ((Get-Hash $config) -ine $mapping.configuration.sha256 -or (Get-Hash $hostConfig) -ine $mapping.host.sha256 `
                            -or (Get-Hash $content) -ine $mapping.content.sha256) {
                            throw "The native output for '$($mapping.identity)' changed."
                        }
                        $raw = @($original | Where-Object { $_.id -ceq $mapping.legacyId })
                        if ($raw.Count -ne 1) { throw 'Native output must have one exact original source identity.' }
                        [ordered]@{
                            identity = $mapping.identity
                            legacyId = $mapping.legacyId
                            configuration = [ordered]@{ path = $mapping.configuration.path; definition = ([System.Text.Encoding]::UTF8.GetString($config) | ConvertFrom-Json -AsHashtable) }
                            host = [ordered]@{ path = $mapping.host.path; definition = ([System.Text.Encoding]::UTF8.GetString($hostConfig) | ConvertFrom-Json -AsHashtable) }
                            content = [ordered]@{
                                path = $mapping.content.path
                                sha256 = Get-Hash ([System.Text.Encoding]::UTF8.GetBytes([System.Text.Encoding]::UTF8.GetString($content).Replace("`r`n", "`n").Replace("`r", "`n")))
                            }
                        }
                    }
                )
                [ordered]@{ packageId = $manifest.packageId; sources = @($manifest.sources); templates = $templates }
            }
            finally {
                $inner.Dispose()
                $stream.Dispose()
            }
        }
        finally {
            $outer.Dispose()
        }
    }
)
if ($cohorts.Count -eq 0) { throw 'At least one reviewed physical Node workload archive is required.' }
$result = [ordered]@{ schemaVersion = 1; cohorts = $cohorts }
[System.IO.File]::WriteAllText($OutputPath, ($result | ConvertTo-Json -Depth 100) + "`n")
