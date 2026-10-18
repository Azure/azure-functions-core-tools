[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackageRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'upstream-sources.json') -Raw | ConvertFrom-Json
$qualifiedConfigurations = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'upstream-configurations.json') -Raw | ConvertFrom-Json -NoEnumerate
$supportingTypes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'supporting-types.json') -Raw | ConvertFrom-Json -NoEnumerate
$auxiliaryIdentities = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'auxiliary-identities.json') -Raw | ConvertFrom-Json -NoEnumerate
$nullableModels = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'nullable-models.json') -Raw | ConvertFrom-Json -NoEnumerate
$adaptedAuxiliaries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$adaptedModels = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$bindingLiterals = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'binding-literals.json') -Raw | ConvertFrom-Json -NoEnumerate
$qualifiedLiterals = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'binding-literals-qualified.json') -Raw | ConvertFrom-Json -NoEnumerate
$fsharpControls = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fsharp-control-sources.json') -Raw | ConvertFrom-Json -NoEnumerate
. (Join-Path $PSScriptRoot 'stage-binding-literals.ps1')
$adaptedIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$literalIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$controlIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
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
            $adaptation = @($supportingTypes | Where-Object identity -CEQ $configuration.identity)
            if ($adaptation.Count -gt 0) {
                if ($adaptation.Count -ne 1 -or $package.type -cne 'item' -or $configuration.tags.language -cne 'C#') {
                    throw "Ambiguous supporting-type adaptation at $($entry.FullName)."
                }
                $pin = $adaptation[0]
                $sourceRelative = $entry.FullName.Replace('.template.config/template.json', "$($configuration.sourceName).cs")
                $sourcePath = Join-Path $output $sourceRelative
                if ($sourceRelative -cne $pin.path -or (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -cne $pin.sha256) {
                    throw "Supporting-type source does not match the qualified baseline: $sourceRelative."
                }
                $originalPath = Join-Path $output "provenance/upstream-content/$sourceRelative"
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($originalPath)) | Out-Null
                [IO.File]::Copy($sourcePath, $originalPath)
                $source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
                foreach ($replacement in $pin.replacements) {
                    $index = $source.IndexOf($replacement.from, [StringComparison]::Ordinal)
                    if ($index -lt 0 -or $index -ne $source.LastIndexOf($replacement.from, [StringComparison]::Ordinal)) {
                        throw "Unexpected qualified authoring replacement in $sourceRelative."
                    }
                    $source = $source.Replace($replacement.from, $replacement.to)
                }
                if ([regex]::Matches($source, '\bToDoItem\b').Count -ne $pin.references) {
                    throw "Unexpected supporting-type references in $sourceRelative."
                }
                # Native sourceName substitution also renames the nested DTO, including when the function is named ToDoItem.
                $helperName = "$($configuration.sourceName)Item"
                $source = [regex]::Replace($source, '\bToDoItem\b', $helperName)
                $boundary = "`n}`n`npublic class $helperName`n"
                $split = $source.IndexOf($boundary, [StringComparison]::Ordinal)
                if ($split -lt 0 -or $split -ne $source.LastIndexOf($boundary, [StringComparison]::Ordinal)) {
                    throw "Unexpected supporting-type layout in $sourceRelative."
                }
                $helper = $source.Substring($split + 4).TrimEnd("`n")
                $nested = ($helper.Split("`n") | ForEach-Object { "    $_" }) -join "`n"
                [IO.File]::WriteAllText($sourcePath, $source.Substring(0, $split) + "`n`n$nested`n}`n",
                    [Text.UTF8Encoding]::new($false))
                if (-not $adaptedIdentities.Add($configuration.identity)) {
                    throw "Duplicate supporting-type adaptation: $($configuration.identity)."
                }
            }
            $auxiliary = & (Join-Path $PSScriptRoot 'stage-qualified-source.ps1') -OutputDirectory $output `
                -ConfigurationPath $configPath -Configuration $configuration -Pins $auxiliaryIdentities
            if ($null -ne $auxiliary -and -not $adaptedAuxiliaries.Add($auxiliary)) {
                throw "Duplicate auxiliary-identity adaptation: $auxiliary."
            }
            $model = & (Join-Path $PSScriptRoot 'stage-qualified-source.ps1') -OutputDirectory $output `
                -ConfigurationPath $configPath -Configuration $configuration -Pins $nullableModels
            if ($null -ne $model -and -not $adaptedModels.Add($model)) {
                throw "Duplicate nullable-model adaptation: $model."
            }
            $literalPin = @($bindingLiterals | Where-Object identity -CEQ $configuration.identity)
            if ($literalPin.Count -gt 0) {
                if ($literalPin.Count -ne 1 -or $package.type -cne 'item' -or
                    $entry.FullName -cne ($literalPin[0].path.Substring(0, $literalPin[0].path.LastIndexOf('/')) + '/.template.config/template.json')) {
                    throw "Ambiguous binding-literal adaptation at $($entry.FullName)."
                }
                Set-NativeBindingLiterals -Output $output -ConfigurationPath $configPath -Configuration $configuration -Pin $literalPin[0]
                $literalQualification = @($qualifiedLiterals | Where-Object identity -CEQ $configuration.identity)
                if ($literalQualification.Count -ne 1 -or
                    (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash -cne $literalQualification[0].configurationSha256 -or
                    (Get-FileHash -LiteralPath (Join-Path $output $literalPin[0].path) -Algorithm SHA256).Hash -cnotin $literalQualification[0].sourceSha256) {
                    throw "Adapted binding-literal source/configuration requires qualification: $($configuration.identity)."
                }
                if (-not $literalIdentities.Add($configuration.identity)) {
                    throw "Duplicate binding-literal adaptation: $($configuration.identity)."
                }
            } elseif ($package.type -ceq 'item' -and @($configuration.symbols.Keys | Where-Object {
                $configuration.symbols[$_].type -ceq 'parameter' -and $_ -cne 'namespace' -and $configuration.symbols[$_].datatype -cne 'choice'
            }).Count -gt 0) {
                throw "The native item has an unclassified parameter usage: $($configuration.identity)."
            }
            $control = @($fsharpControls | Where-Object identity -CEQ $configuration.identity)
            if ($control.Count -gt 0) {
                if ($control.Count -ne 1 -or $package.type -cne 'item' -or $configuration.tags.language -cne 'F#' -or
                    $entry.FullName -cne ($control[0].path.Substring(0, $control[0].path.LastIndexOf('/')) + '/.template.config/template.json') -or
                    (Get-FileHash -LiteralPath (Join-Path $output $control[0].path) -Algorithm SHA256).Hash -cne $control[0].sha256 -or
                    -not $controlIdentities.Add($configuration.identity)) {
                    throw "F# control source does not match its qualified native source: $($configuration.identity)."
                }
            }
            # CLI aliases live in host metadata only.
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
if ($adaptedIdentities.Count -ne $supportingTypes.Count) {
    throw 'The qualified supporting-type inventory was not completely adapted.'
}
if ($adaptedAuxiliaries.Count -ne $auxiliaryIdentities.Count) {
    throw 'The qualified auxiliary-identity inventory was not completely adapted.'
}
if ($adaptedModels.Count -ne $nullableModels.Count) {
    throw 'The qualified nullable-model inventory was not completely adapted.'
}
if ($literalIdentities.Count -ne $bindingLiterals.Count -or $literalIdentities.Count -ne $qualifiedLiterals.Count) {
    throw 'The qualified binding-literal inventory was not completely adapted.'
}
if ($controlIdentities.Count -ne $fsharpControls.Count) {
    throw 'The qualified F# control source inventory was not completely verified.'
}
