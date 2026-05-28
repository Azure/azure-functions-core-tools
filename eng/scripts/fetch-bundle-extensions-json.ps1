#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Downloads `bin/extensions.json` from the latest listed bundle of an
    Azure Functions extension-bundle channel, without fetching the full
    bundle zip.

.DESCRIPTION
    Resolves the active channel's authoritative bundle list from CDN
    `index.json` (per `Microsoft.Azure.Functions.ExtensionBundle.<Channel>`),
    picks the highest listed `4.x` version (publication-order — the last
    `4.x` entry in the array), then extracts `bin/extensions.json` from
    the bundle archive at
    `<id>/<version>/<id>.<version>.zip` using two HTTP Range requests:

      1. Fetch the End-of-Central-Directory (EOCD) record from the last
         ~64 KB of the archive.
      2. Walk the central directory, find the entry whose name is
         `bin/extensions.json`, fetch its local file header + compressed
         data, and inflate.

    Only versions listed in `index.json` are eligible; CDN-hosted but
    unlisted bundle versions are ignored (matches the Functions runtime
    contract for the bundle channel).

    The script is intentionally pure PowerShell with no module
    dependencies so it runs cleanly in CI without an external NuGet
    package or `Install-Module`.

.PARAMETER Channel
    `stable` | `preview` | `experimental`. Selects the bundle id:
        stable        -> Microsoft.Azure.Functions.ExtensionBundle
        preview       -> Microsoft.Azure.Functions.ExtensionBundle.Preview
        experimental  -> Microsoft.Azure.Functions.ExtensionBundle.Experimental

.PARAMETER OutputPath
    Where to write the extracted extensions.json (UTF-8 no BOM).

.PARAMETER VersionOut
    Optional. If provided, writes the resolved bundle version to this file
    so the caller can record build-provenance without re-parsing index.json.

.NOTES
    Bundle archives are sometimes >150 MB. The Range-based extraction
    transfers ~10–20 KB per invocation, so this is safe to call on every
    build without the cost of a full download.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('stable','preview','experimental')]
    [string]$Channel,

    [Parameter(Mandatory=$true)]
    [string]$OutputPath,

    [string]$VersionOut
)

$ErrorActionPreference = 'Stop'

# Normalize path separators upfront. On Linux, backslashes are literal
# filename characters, not directory separators, so any call site that
# passes a Windows-style path produces a single mangled file under the
# parent directory. .NET accepts forward slashes on both platforms.
$OutputPath = $OutputPath -replace '\\', '/'
if ($VersionOut) { $VersionOut = $VersionOut -replace '\\', '/' }

$cdnRoot = 'https://cdn.functions.azure.com/public/ExtensionBundles'
$bundleId = switch ($Channel) {
    'stable'       { 'Microsoft.Azure.Functions.ExtensionBundle' }
    'preview'      { 'Microsoft.Azure.Functions.ExtensionBundle.Preview' }
    'experimental' { 'Microsoft.Azure.Functions.ExtensionBundle.Experimental' }
}

function Resolve-LatestListedVersion {
    param([string]$BundleId)
    # index.json is the authoritative list of usable versions; anything not
    # listed here must not be consumed even if present on CDN.
    $indexUrl = "$cdnRoot/$BundleId/index.json"
    $arr = Invoke-RestMethod -Uri $indexUrl -ErrorAction Stop
    # Publication order: array tail = newest. Filter to v4 only because
    # v5 workloads target the v4 bundle (minBundleVersion >= 4.0.0).
    $fourX = $arr | Where-Object { $_ -like '4.*' }
    if (-not $fourX) { throw "fetch-bundle-extensions-json: no 4.x entries in $indexUrl" }
    return ($fourX | Select-Object -Last 1)
}

function Get-Range {
    param([string]$Uri, [long]$Start, [long]$End)
    # Range header is inclusive on both ends.
    $req = [System.Net.HttpWebRequest]::Create($Uri)
    $req.AddRange('bytes', $Start, $End)
    $req.Method = 'GET'
    $resp = $req.GetResponse()
    try {
        $ms = New-Object System.IO.MemoryStream
        $resp.GetResponseStream().CopyTo($ms)
        return $ms.ToArray()
    } finally {
        $resp.Dispose()
    }
}

function Get-ContentLength {
    param([string]$Uri)
    $req = [System.Net.HttpWebRequest]::Create($Uri)
    $req.Method = 'HEAD'
    $resp = $req.GetResponse()
    try { return [int64]$resp.ContentLength } finally { $resp.Dispose() }
}

function Extract-EntryFromZipViaRange {
    param([string]$ZipUrl, [string]$TargetEntryName)

    # ZIP layout (no ZIP64 support — bundle archives are well under 4 GB):
    #   [local file 1][local file 2]...[central dir entries][EOCD record]
    # EOCD is at most 22 bytes + a comment (<= 64 KB). Fetch the last
    # 64 KB of the file and scan backwards for the EOCD signature.

    $size = Get-ContentLength -Uri $ZipUrl
    $tail = [int64][Math]::Min(65557, $size)        # 65535 (max comment) + 22
    $tailBytes = Get-Range -Uri $ZipUrl -Start ($size - $tail) -End ($size - 1)

    $eocdSig = [byte[]]@(0x50,0x4B,0x05,0x06)
    $eocdOff = -1
    for ($i = $tailBytes.Length - 22; $i -ge 0; $i--) {
        if ($tailBytes[$i] -eq $eocdSig[0] -and
            $tailBytes[$i+1] -eq $eocdSig[1] -and
            $tailBytes[$i+2] -eq $eocdSig[2] -and
            $tailBytes[$i+3] -eq $eocdSig[3]) {
            $eocdOff = $i
            break
        }
    }
    if ($eocdOff -lt 0) { throw "fetch-bundle-extensions-json: EOCD not found in $ZipUrl tail" }

    $cdSize     = [BitConverter]::ToInt32($tailBytes, $eocdOff + 12)
    $cdOffset   = [BitConverter]::ToInt32($tailBytes, $eocdOff + 16)

    # Fetch the central directory.
    $cd = Get-Range -Uri $ZipUrl -Start $cdOffset -End ($cdOffset + $cdSize - 1)

    # Walk central directory entries.
    $cdSig = [byte[]]@(0x50,0x4B,0x01,0x02)
    $i = 0
    $entryOffset = -1
    $entryCompSize = 0
    $entryUncompSize = 0
    $entryMethod = 0
    while ($i -lt $cd.Length) {
        if ($cd[$i] -ne $cdSig[0] -or $cd[$i+1] -ne $cdSig[1] -or
            $cd[$i+2] -ne $cdSig[2] -or $cd[$i+3] -ne $cdSig[3]) {
            throw "fetch-bundle-extensions-json: bad central directory signature at offset $i"
        }
        $method   = [BitConverter]::ToUInt16($cd, $i + 10)
        $compSize = [BitConverter]::ToUInt32($cd, $i + 20)
        $uncSize  = [BitConverter]::ToUInt32($cd, $i + 24)
        $nameLen  = [BitConverter]::ToUInt16($cd, $i + 28)
        $extraLen = [BitConverter]::ToUInt16($cd, $i + 30)
        $commLen  = [BitConverter]::ToUInt16($cd, $i + 32)
        $localOff = [BitConverter]::ToUInt32($cd, $i + 42)
        $name     = [System.Text.Encoding]::UTF8.GetString($cd, $i + 46, $nameLen)

        if ($name -eq $TargetEntryName) {
            $entryOffset    = $localOff
            $entryCompSize  = $compSize
            $entryUncompSize = $uncSize
            $entryMethod    = $method
            break
        }
        $i += 46 + $nameLen + $extraLen + $commLen
    }

    if ($entryOffset -lt 0) { throw "fetch-bundle-extensions-json: entry '$TargetEntryName' not found in zip central directory" }

    # Fetch local file header + compressed payload.
    # Local header layout: fixed 30 bytes + name + extra. We don't know
    # name/extra lengths yet so we read 30 + 64 KB (more than enough) to
    # cover header + start of payload, then fetch the remaining payload.
    $lfhPreambleLen = 30
    $previewLen = [int64]([Math]::Min(($lfhPreambleLen + $entryCompSize + 4096), $size - $entryOffset))
    $preview = Get-Range -Uri $ZipUrl -Start $entryOffset -End ($entryOffset + $previewLen - 1)

    $localNameLen  = [BitConverter]::ToUInt16($preview, 26)
    $localExtraLen = [BitConverter]::ToUInt16($preview, 28)
    $dataStart = $lfhPreambleLen + $localNameLen + $localExtraLen

    # Total bytes needed for the compressed payload starting at dataStart.
    $needed = $dataStart + $entryCompSize
    if ($needed -le $preview.Length) {
        $comp = New-Object byte[] $entryCompSize
        [Array]::Copy($preview, $dataStart, $comp, 0, $entryCompSize)
    } else {
        # Need more — fetch the missing tail. Should be rare for small
        # entries like extensions.json (a few KB), but defensive.
        $extra = Get-Range -Uri $ZipUrl -Start ($entryOffset + $preview.Length) -End ($entryOffset + $needed - 1)
        $comp = New-Object byte[] $entryCompSize
        [Array]::Copy($preview, $dataStart, $comp, 0, $preview.Length - $dataStart)
        [Array]::Copy($extra, 0, $comp, $preview.Length - $dataStart, $extra.Length)
    }

    if ($entryMethod -eq 0) {
        return $comp                          # stored, not compressed
    } elseif ($entryMethod -eq 8) {
        # Deflate
        $msIn = New-Object System.IO.MemoryStream(,$comp)
        $deflate = New-Object System.IO.Compression.DeflateStream($msIn, [System.IO.Compression.CompressionMode]::Decompress)
        $msOut = New-Object System.IO.MemoryStream
        $deflate.CopyTo($msOut)
        $deflate.Dispose()
        return $msOut.ToArray()
    } else {
        throw "fetch-bundle-extensions-json: unsupported compression method $entryMethod for '$TargetEntryName'"
    }
}

$version = Resolve-LatestListedVersion -BundleId $bundleId
$zipUrl  = "$cdnRoot/$bundleId/$version/$bundleId.$version.zip"

Write-Host "fetch-bundle-extensions-json: channel=$Channel id=$bundleId version=$version"
Write-Host "fetch-bundle-extensions-json: extracting bin/extensions.json from $zipUrl"

$bytes = Extract-EntryFromZipViaRange -ZipUrl $zipUrl -TargetEntryName 'bin/extensions.json'

$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllBytes($OutputPath, $bytes)

if ($VersionOut) {
    $vDir = Split-Path -Parent $VersionOut
    if (-not (Test-Path -LiteralPath $vDir)) { New-Item -ItemType Directory -Path $vDir -Force | Out-Null }
    [System.IO.File]::WriteAllText($VersionOut, $version, $utf8NoBom)
}

Write-Host "fetch-bundle-extensions-json: wrote $($bytes.Length) bytes to $OutputPath"
