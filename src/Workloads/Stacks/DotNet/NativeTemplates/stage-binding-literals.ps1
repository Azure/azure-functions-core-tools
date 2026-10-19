function Set-NativeBindingLiterals {
    param(
        [string] $Output,
        [string] $ConfigurationPath,
        [System.Collections.IDictionary] $Configuration,
        [object] $Pin
    )

    $sourcePath = Join-Path $Output $Pin.path
    $originalPath = Join-Path $Output "provenance/upstream-content/$($Pin.path)"
    $original = if (Test-Path -LiteralPath $originalPath) { $originalPath } else { $sourcePath }
    if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -cne $Pin.sha256) {
        throw "Binding-literal source does not match the qualified baseline: $($Pin.path)."
    }
    if ($original -eq $sourcePath) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($originalPath)) | Out-Null
        [IO.File]::Copy($sourcePath, $originalPath)
    }
    $originalConfiguration = Join-Path $Output "provenance/upstream-content/$($Pin.path.Substring(0, $Pin.path.LastIndexOf('/')))/.template.config/template.json.original"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($originalConfiguration)) | Out-Null
    [IO.File]::Copy($ConfigurationPath, $originalConfiguration)
    if ($Configuration.identity -ceq 'Azure.Function.FSharp.Isolated.CosmosDBTrigger.4.x') {
        if ($Configuration.shortName -cne 'CosmosDB') {
            throw 'Unexpected F# Cosmos template short name.'
        }
        $Configuration.shortName = @('CosmosDB', 'cosmos')
    }
    $source = [IO.File]::ReadAllText($sourcePath)
    $parameters = @($Configuration.symbols.Keys | Where-Object {
        $Configuration.symbols[$_].type -ceq 'parameter' -and $_ -cne 'namespace' -and $Configuration.symbols[$_].datatype -cne 'choice'
    })
    if ($parameters.Count -ne $Pin.parameters.Count -or $Configuration.Contains('forms')) {
        throw "Unexpected binding-literal parameter/form inventory: $($Pin.identity)."
    }
    $form = if ($Configuration.tags.language -ceq 'C#') { 'csharpStringContent' } else { 'fsharpStringContent' }
    $Configuration.forms = [ordered]@{
        literalContent = [ordered]@{ identifier = 'replace'; pattern = '\A"|"\z'; replacement = '' }
        $form = [ordered]@{ identifier = 'chain'; steps = @('jsonEncode', 'literalContent') }
    }
    foreach ($mapping in $Pin.parameters) {
        $name = $mapping[0]
        $literal = $mapping[1]
        $symbol = $Configuration.symbols[$name]
        # A missing optional value must retain its original authored placeholder.
        $token = if ($mapping.Count -eq 3) { $mapping[2] } else { "__FuncBinding_$($name)__" }
        if ($name -cnotin $parameters -or -not $symbol.Contains('replaces') -or $symbol.Contains('forms') -or
            $Configuration.symbols.Contains("FuncBinding_$name") -or
            ($mapping.Count -eq 3 -and ($symbol.Contains('defaultValue') -or $token -cne $symbol.replaces -or
                $source.IndexOf($token, [StringComparison]::Ordinal) -ne $source.LastIndexOf($token, [StringComparison]::Ordinal))) -or
            ($mapping.Count -ne 3 -and $source.Contains($token))) {
            throw "Unexpected binding-literal symbol mapping: $($Pin.identity) $name."
        }
        $index = $source.IndexOf($literal, [StringComparison]::Ordinal)
        if ($index -lt 0 -or $index -ne $source.LastIndexOf($literal, [StringComparison]::Ordinal) -or
            $literal.IndexOf($symbol.replaces, [StringComparison]::Ordinal) -lt 0) {
            throw "Unexpected binding-literal source context: $($Pin.identity) $name."
        }
        $source = $source.Replace($literal, $literal.Replace($symbol.replaces, $token))
        $symbol.Remove('replaces') | Out-Null
        $Configuration.symbols["FuncBinding_$name"] = [ordered]@{
            type = 'derived'; valueSource = $name; valueTransform = $form; replaces = $token
        }
    }
    [IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
    $json = ($Configuration | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText($ConfigurationPath, $json, [Text.UTF8Encoding]::new($false))
}
