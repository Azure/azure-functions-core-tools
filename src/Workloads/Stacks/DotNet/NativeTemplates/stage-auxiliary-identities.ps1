[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $ConfigurationPath,
    [Parameter(Mandatory)] [object] $Configuration,
    [Parameter(Mandatory)] [object[]] $Pins
)

$ErrorActionPreference = 'Stop'
$matches = @($Pins | Where-Object identity -CEQ $Configuration.identity)
if ($matches.Count -eq 0) {
    return
}
if ($matches.Count -ne 1 -or $Configuration.tags.type -cne 'item' -or $Configuration.tags.language -cne 'C#') {
    throw "Ambiguous auxiliary-identity adaptation: $($Configuration.identity)."
}
$pin = $matches[0]
$sourcePath = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($ConfigurationPath))) "$($Configuration.sourceName).cs"
$expectedPath = [IO.Path]::GetFullPath((Join-Path $OutputDirectory $pin.path))
if ($sourcePath -cne $expectedPath -or
    (Get-FileHash -LiteralPath $ConfigurationPath -Algorithm SHA256).Hash -cne $pin.configurationSha256 -or
    (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -cne $pin.sha256) {
    throw "Auxiliary-identity source or configuration does not match the qualified baseline: $($pin.path)."
}
$originalPath = Join-Path $OutputDirectory "provenance/upstream-content/$($pin.path)"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($originalPath)) | Out-Null
[IO.File]::Copy($sourcePath, $originalPath)
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
foreach ($replacement in $pin.replacements) {
    if ([regex]::Matches($source, [regex]::Escape($replacement.from)).Count -ne $replacement.count) {
        throw "Unexpected qualified auxiliary-identity replacement in $($pin.path)."
    }
    $source = $source.Replace($replacement.from, $replacement.to)
}
if ($null -ne $pin.nest) {
    $indent = ' ' * $pin.nest.indent
    $closing = "`n$indent}`n`n"
    $boundary = "$closing$indent$($pin.nest.declaration)`n"
    $split = $source.IndexOf($boundary, [StringComparison]::Ordinal)
    if ($split -lt 0 -or $split -ne $source.LastIndexOf($boundary, [StringComparison]::Ordinal) -or
        -not $source.EndsWith($pin.nest.suffix, [StringComparison]::Ordinal)) {
        throw "Unexpected qualified auxiliary-type layout in $($pin.path)."
    }
    $helper = $source.Substring($split + $closing.Length, $source.Length - $split - $closing.Length - $pin.nest.suffix.Length)
    $nested = ($helper.TrimEnd("`n").Split("`n") | ForEach-Object { if ($_.Length -gt 0) { "    $_" } else { '' } }) -join "`n"
    $source = $source.Substring(0, $split) + "`n`n$nested`n$indent}`n" + $pin.nest.suffix
}
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
$Configuration.identity
