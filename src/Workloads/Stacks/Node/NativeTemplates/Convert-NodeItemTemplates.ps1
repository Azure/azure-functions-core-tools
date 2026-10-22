# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SourceDirectory,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [Parameter(Mandatory = $true)] [string] $ProvenanceDirectory,
    [Parameter(Mandatory = $true)] [string] $PackageId,
    [Parameter(Mandatory = $true)] [string] $PackageVersion,
    [ValidateSet('All', 'Project', 'Function')] [string] $ContentRole = 'All'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$templates = Get-Content -LiteralPath (Join-Path $SourceDirectory 'templates/templates.json') -Raw | ConvertFrom-Json -AsHashtable
$bindings = Get-Content -LiteralPath (Join-Path $SourceDirectory 'templates/_bindings.json') -Raw | ConvertFrom-Json -AsHashtable
$prompts = Get-Content -LiteralPath (Join-Path $SourceDirectory 'bindings/userPrompts.json') -Raw | ConvertFrom-Json -AsHashtable
$resources = Get-Content -LiteralPath (Join-Path $SourceDirectory 'resources/Resources.json') -Raw | ConvertFrom-Json -AsHashtable
$promptById = @{}
foreach ($prompt in $prompts) { $promptById.Add($prompt.id, $prompt) }

# Keep known-id application available for the entries hidden by V2HiddenTemplates.
$hiddenIds = @(
    'BlobTrigger-TypeScript', 'EventGridBlobTrigger-TypeScript',
    'DurableFunctionsEntity-TypeScript', 'DurableFunctionsOrchestrator-TypeScript',
    'DurableFunctionsEntity-JavaScript', 'DurableFunctionsOrchestrator-JavaScript'
)
$httpIds = @('HttpTrigger-JavaScript', 'HttpTrigger-TypeScript')
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Resolve-Resource([string] $value) {
    if ($value.StartsWith('$') -and $resources.Contains($value.Substring(1))) { return $resources[$value.Substring(1)] }
    return $value
}

function ConvertTo-OptionName([string] $value) {
    $value = [regex]::Replace($value, '([A-Z]+)([A-Z][a-z])', '$1-$2')
    return ([regex]::Replace($value, '([^A-Z])([A-Z])', '$1-$2')).ToLowerInvariant()
}

foreach ($template in $templates) {
    if ($ContentRole -eq 'Project') { continue }
    $id = [string] $template.id
    if ($id -notmatch '^[A-Za-z0-9]+-(JavaScript|TypeScript)$' -or -not $seen.Add($id)) {
        throw "Invalid or duplicate Node template id '$id'."
    }
    if (-not $bindings.Contains($id)) { throw "Missing binding requirements for '$id'." }
    if ($httpIds -contains $id) { continue }

    $language = switch ($template.language.ToLowerInvariant()) {
        'javascript' { 'JavaScript' }
        'typescript' { 'TypeScript' }
        default { throw "Unsupported Node language in '$id'." }
    }
    $extension = if ($language -eq 'JavaScript') { 'js' } else { 'ts' }
    if ($template.jobs.Count -ne 1 -or $template.jobs[0].type -ne 'CreateNewApp') {
        throw "Review the changed job graph for '$id' before converting it."
    }
    $job = $template.jobs[0]
    $actions = @($job.actions | ForEach-Object {
        $name = $_
        $matches = @($template.actions | Where-Object { $_.name -eq $name })
        if ($matches.Count -ne 1) { throw "Missing or duplicate action '$name' in '$id'." }
        $matches[0]
    })
    if ($actions.Count -ne 2 -or $actions[0].type -ne 'GetTemplateFileContent' -or $actions[1].type -ne 'WriteToFile' `
        -or $actions[0].assignTo -ne $actions[1].source `
        -or -not $template.files.Contains($actions[0].filePath)) {
        throw "Review the changed file actions for '$id' before converting it."
    }
    $writePath = [string] $actions[1].filePath
    $parts = $writePath.Split('/')
    if ($parts.Count -lt 2 -or $parts[-1] -cne ('$(FUNCTION_NAME_INPUT).' + $extension) `
        -or @($parts[0..($parts.Count - 2)] | Where-Object { $_ -notmatch '^[A-Za-z0-9_.-]+$' -or $_ -in @('.', '..') }).Count -gt 0) {
        throw "The write path for '$id' must have a constant relative directory and a language-matched function-name filename."
    }
    $legacyTarget = ($parts[0..($parts.Count - 2)] -join '/') + '/'
    $content = [string] $template.files[$actions[0].filePath]
    $symbols = [ordered]@{}
    $symbolInfo = [ordered]@{}
    $defaultName = $null
    foreach ($templateInput in $job.inputs) {
        if (-not $promptById.ContainsKey($templateInput.paramId)) { throw "Missing prompt '$($templateInput.paramId)' for '$id'." }
        if ($templateInput.assignTo -eq '$(FUNCTION_NAME_INPUT)') {
            $defaultName = $templateInput.defaultValue
            continue
        }
        $prompt = $promptById[$templateInput.paramId]
        $symbol = [ordered]@{
            type = 'parameter'
            datatype = $(if ($prompt['enum']) { 'choice' } else { 'string' })
            description = Resolve-Resource ([string] $prompt.label)
            isRequired = [bool] ($templateInput['required'] -or $prompt['required'])
        }
        if ($templateInput.Contains('defaultValue')) { $symbol.defaultValue = [string] $templateInput.defaultValue }
        elseif ($prompt.Contains('defaultValue')) { $symbol.defaultValue = [string] $prompt.defaultValue }
        if ($prompt['enum']) {
            $symbol.choices = @($prompt.enum | ForEach-Object { @{ choice = $_.value; displayName = $_.display } })
        }
        # Do not invent substitutions for prompts absent from the owned payload.
        if ($content.Contains([string] $templateInput.assignTo)) { $symbol.replaces = $templateInput.assignTo }
        $symbols.Add($templateInput.paramId, $symbol)
        $symbolInfo.Add($templateInput.paramId, @{ longName = ConvertTo-OptionName $templateInput.paramId })
    }
    if ([string]::IsNullOrWhiteSpace($defaultName)) { throw "Missing default function name for '$id'." }
    $content = $content.Replace('$(FUNCTION_NAME_INPUT)', 'funcNodeItem')
    foreach ($token in [regex]::Matches($content, '\$\([A-Za-z_][A-Za-z0-9_]*\)')) {
        if (@($symbols.Values | Where-Object { $_.Contains('replaces') -and $_.replaces -ceq $token.Value }).Count -eq 0) {
            throw "Unmapped source token '$($token.Value)' in '$id'."
        }
    }
    $symbols.Add('InvocationOutputIntent', @{ type = 'bind'; binding = 'host:func:item-output-intent' })

    $constraints = [ordered]@{
        host = @{ type = 'host'; args = @(@{ hostname = 'func' }) }
        stack = @{ type = 'func-workload'; args = 'node' }
    }
    if ($bindings[$id].Count -gt 0) {
        $constraints.bundle = @{
            type = 'func-bundle'
            args = @(
                @{ id = 'Microsoft.Azure.Functions.ExtensionBundle'; version = '[4.0.0,)' }
                @{ id = 'Microsoft.Azure.Functions.ExtensionBundle.Preview'; version = '[4.0.0,)' }
                @{ id = 'Microsoft.Azure.Functions.ExtensionBundle.Experimental'; version = '[4.0.0,)' }
            )
        }
        # Fail closed until the runtime qualifies binding-aware bundle eligibility.
        $constraints.bindings = @{ type = 'func-bundle-bindings'; args = @($bindings[$id]) }
    }
    $configuration = [ordered]@{
        '$schema' = 'http://json.schemastore.org/template'
        author = $template.author
        identity = $id
        groupIdentity = 'Azure.Functions.Node.' + ($id -split '-')[0]
        name = Resolve-Resource $template.name
        description = Resolve-Resource $template.description
        shortName = @($id, (Resolve-Resource $template.name))
        tags = @{ type = 'item'; language = $language }
        sourceName = 'funcNodeItem'
        defaultName = $defaultName
        preferNameDirectory = $false
        symbols = $symbols
        constraints = $constraints
        sources = @(
            @{ source = './'; target = $legacyTarget; condition = '(InvocationOutputIntent == "legacy")' }
            @{ source = './'; target = './'; condition = '(InvocationOutputIntent != "legacy")' }
        )
    }
    $hostConfiguration = @{ isHidden = ($hiddenIds -contains $id); symbolInfo = $symbolInfo }
    $directory = Join-Path $OutputDirectory $id
    $configDirectory = Join-Path $directory '.template.config'
    [void] (New-Item -ItemType Directory -Path $configDirectory -Force)
    [System.IO.File]::WriteAllText((Join-Path $directory "funcNodeItem.$extension"), $content)
    [System.IO.File]::WriteAllText((Join-Path $configDirectory 'template.json'), ($configuration | ConvertTo-Json -Depth 30))
    [System.IO.File]::WriteAllText((Join-Path $configDirectory 'func.host.json'), ($hostConfiguration | ConvertTo-Json -Depth 30))
}

$sourceFiles = @('templates/templates.json', 'templates/_bindings.json', 'bindings/userPrompts.json', 'resources/Resources.json')
$sources = @($sourceFiles | ForEach-Object {
    $source = Join-Path $SourceDirectory $_
    $destination = Join-Path $ProvenanceDirectory "v2/$_"
    [void] (New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
    [System.IO.File]::Copy($source, $destination, $true)
    @{ path = "provenance/node/v2/$_"; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
})
$mappings = @($templates | Where-Object { $ContentRole -ne 'Project' } | ForEach-Object {
    $id = $_.id
    if ($httpIds -contains $id) {
        $language = if ($id -eq 'HttpTrigger-JavaScript') { 'JavaScript' } else { 'TypeScript' }
        $folder = if ($language -eq 'JavaScript') { 'http-js' } else { 'http-ts' }
        $directory = Join-Path $PSScriptRoot "content/$folder"
        $packageDirectory = "content/$folder"
        $sourceName = 'httpTrigger'
        $identity = "Azure.Functions.Node.Http.$language"
    }
    else {
        $directory = Join-Path $OutputDirectory $id
        $packageDirectory = "content/items/$id"
        $sourceName = 'funcNodeItem'
        $identity = $id
    }
    $extension = if ($_.language -eq 'javascript') { 'js' } else { 'ts' }
    @{
        legacyId = $id
        identity = $identity
        configuration = @{
            path = "$packageDirectory/.template.config/template.json"
            sha256 = (Get-FileHash -LiteralPath (Join-Path $directory '.template.config/template.json') -Algorithm SHA256).Hash
        }
        host = @{
            path = "$packageDirectory/.template.config/func.host.json"
            sha256 = (Get-FileHash -LiteralPath (Join-Path $directory '.template.config/func.host.json') -Algorithm SHA256).Hash
        }
        content = @{
            path = "$packageDirectory/$sourceName.$extension"
            sha256 = (Get-FileHash -LiteralPath (Join-Path $directory "$sourceName.$extension") -Algorithm SHA256).Hash
        }
    }
})
$projects = @('project-js', 'project-ts') | Where-Object { $ContentRole -ne 'Function' } | ForEach-Object {
    $directory = Join-Path $PSScriptRoot "content/$_"
    $configurationPath = Join-Path $directory '.template.config/template.json'
    $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json -AsHashtable
    $packageSource = if ($_ -eq 'project-js') { '../Templates/package-js.json' } else { '../Templates/package-ts.json' }
    @{
        identity = $configuration.identity
        configuration = @{ path = "content/$_/.template.config/template.json"; sha256 = (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash }
        host = @{ path = "content/$_/.template.config/func.host.json"; sha256 = (Get-FileHash -LiteralPath (Join-Path $directory '.template.config/func.host.json') -Algorithm SHA256).Hash }
        package = @{ path = "content/$_/package.json"; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $packageSource) -Algorithm SHA256).Hash }
    }
}
$provenance = [ordered]@{ packageId = $PackageId; packageVersion = $PackageVersion; sources = $sources; templates = $mappings; projects = @($projects) }
[System.IO.File]::WriteAllText((Join-Path $ProvenanceDirectory 'compatibility.json'), ($provenance | ConvertTo-Json -Depth 30))
