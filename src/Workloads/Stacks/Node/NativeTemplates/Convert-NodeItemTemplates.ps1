# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SourceDirectory,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [Parameter(Mandatory = $true)] [string] $ProvenanceDirectory,
    [Parameter(Mandatory = $true)] [string] $PackageId,
    [Parameter(Mandatory = $true)] [string] $PackageVersion
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
$reviewedBodies = @{
    'DaprPublishOutputBinding-JavaScript' = 'BB0CD1A84EB4D34590A7319738AF495E8EA44A1FA018EAE5F0851703A661DB4B'
    'DaprServiceInvocationTrigger-JavaScript' = '3A5A71262E434CBBD82516C0F831C3F40F0BD49FC3DA16476A76D82479B9DF71'
    'BlobTrigger-JavaScript' = '773499147F9E697C074458C1B4D4152F32870FFEEE5A0301AC2EE4F95F135B02'
    'BlobTrigger-TypeScript' = 'BD456F4A0FCAE70EF4EC73CD364215B3B76F07A3D3337150114488750C891527'
    'CosmosDBTrigger-JavaScript' = 'A90F64FD9F3CB713EB54CAAC2F7235806C39246DB30FE4604AA41699DB1DAEF3'
    'CosmosDBTrigger-TypeScript' = '6F59394D49AB665B37BAA32F68B0E92CFD6C152EC563815B6BEEE2EEAD00EBAC'
    'EventGridBlobTrigger-JavaScript' = 'D9F6EE6440831BE28725966D1C29D0D214E8F6D578EA1D8515E4C95CA6A1975A'
    'EventGridBlobTrigger-TypeScript' = 'DE9FDAB6587A18ECA548E6E49E9A14A616CB9C98E379BDF93ABB5F41F9B3D35A'
    'EventHubTrigger-JavaScript' = 'E6F56D5053168C3768D607F66472F9A66B6CEAF4B050209396D545C723A859D1'
    'EventHubTrigger-TypeScript' = 'E5546062A26396BF0290A8AC481004E714E6BA786DB3532EAD6B6AEF59BCB75A'
    'QueueTrigger-JavaScript' = 'CF8C0BCE5551F941D5D6854109C76E7AA7B145311178C2B39594E7242191637D'
    'QueueTrigger-TypeScript' = '3C76C6362E0CE9979776DA0E442CD648BD3A4163B1947A75F16F67725C06EE4E'
    'ServiceBusQueueTrigger-JavaScript' = 'D9229D627355FFE8502D3CFE42024F1C36C95856CA05E1239866CD00043C7A12'
    'ServiceBusQueueTrigger-TypeScript' = 'C8022455CE0534CF3EF838859165AB694F922238F79847DFEB522DFB62805015'
    'ServiceBusTopicTrigger-JavaScript' = 'E5A29EEBB6F74659B3BFDBB849A81C88E0F5C82A2B2675DEBD1F294DC8F1208F'
    'ServiceBusTopicTrigger-TypeScript' = 'F369B1B548DF93C16B22F5F79F3032AEFCD7B6B807A1DAC406E52C73B04E8A11'
    'TimerTrigger-JavaScript' = 'CAF047F47111295F4DF1AC15C7791082855ED7727952CF889FE9A501C8A7E5A1'
    'TimerTrigger-TypeScript' = 'FFACD7BBBF87FDF7518D188A5A6E75CF44398F05CA5768155626A04EAE8A29A5'
}

function Resolve-Resource([string] $value) {
    if ($value.StartsWith('$') -and $resources.Contains($value.Substring(1))) { return $resources[$value.Substring(1)] }
    return $value
}

function ConvertTo-OptionName([string] $value) {
    $value = [regex]::Replace($value, '([A-Z]+)([A-Z][a-z])', '$1-$2')
    return ([regex]::Replace($value, '([^A-Z])([A-Z])', '$1-$2')).ToLowerInvariant()
}

function Get-PropertyMapping([string] $id, $templateInput) {
    $kind = ($id -split '-')[0]
    if ($kind -eq 'EventGridBlobTrigger') { $kind = 'BlobTrigger' }
    $key = "$kind|$($templateInput.paramId)|$($templateInput.assignTo)"
    switch -CaseSensitive ($key) {
        'TimerTrigger|timerTrigger-schedule|$(SCHEDULE_INPUT)' { return @('schedule', "'0 */5 * * * *'") }
        'QueueTrigger|queueTrigger-connection|$(CONNECTION_INPUT)' { return @('connection', "''") }
        'QueueTrigger|queueTrigger-name|$(QUEUE_NAME_INPUT)' { return @('queueName', "'js-queue-items'") }
        'BlobTrigger|blobTrigger-connection|$(CONNECTION_INPUT)' { return @('connection', "''") }
        'BlobTrigger|blobTrigger-path|$(PATH_INPUT)' { return @('path', "'samples-workitems/{name}'") }
        'EventHubTrigger|eventHubTrigger-name|$(EVENT_HUB_NAME_INPUT)' { return @('eventHubName', "'samples-workitems'") }
        'ServiceBusQueueTrigger|serviceBusTrigger-connection|$(CONNECTION_INPUT)' { return @('connection', "''") }
        'ServiceBusQueueTrigger|serviceBusTrigger-name|$(QUEUE_NAME_INPUT)' { return @('queueName', "'myinputqueue'") }
        'ServiceBusTopicTrigger|serviceBusTrigger-connection|$(CONNECTION_INPUT)' { return @('connection', "''") }
        'ServiceBusTopicTrigger|serviceBusTrigger-topic|$(TOPIC_NAME_INPUT)' { return @('topicName', "'mytopic'") }
        'ServiceBusTopicTrigger|serviceBusTrigger-subscription|$(SUBSCRIPTION_NAME_INPUT)' { return @('subscriptionName', "'mysubscription'") }
        'CosmosDBTrigger|cosmosTrigger-connection|$(CONNECTION_INPUT)' { return @('connectionStringSetting', "''") }
        'CosmosDBTrigger|cosmosTrigger-database|$(DATABASE_INPUT)' { return @('databaseName', "''") }
        'CosmosDBTrigger|cosmosTrigger-collection|$(COLLECTION_INPUT)' { return @('collectionName', "''") }
        'CosmosDBTrigger|cosmosTrigger-createLease|$(CREATE_LEASE_INPUT)' { return @('createLeaseCollectionIfNotExists', 'true') }
        default { throw "Review the property association for '$key' before converting '$id'." }
    }
}

foreach ($template in $templates) {
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
    $hasDaprHelper = $id -cin @('DaprPublishOutputBinding-JavaScript', 'DaprServiceInvocationTrigger-JavaScript')
    if ($hasDaprHelper -or @($job.inputs | Where-Object { $_.assignTo -cne '$(FUNCTION_NAME_INPUT)' }).Count -gt 0) {
        $normalized = $content.Replace("`r`n", "`n")
        $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($normalized)))
        if ($reviewedBodies.Keys -cnotcontains $id -or $hash -cne $reviewedBodies[$id]) {
            throw "Review the changed owned body/declaration association for '$id' before converting it."
        }
        if ($id -ceq 'DaprPublishOutputBinding-JavaScript') {
            $content = $content.Replace("app.generic('DaprTopicTriggerNodeV4', {", "app.generic('`$(FUNCTION_NAME_INPUT)-Topic', {")
        }
        elseif ($id -ceq 'DaprServiceInvocationTrigger-JavaScript') {
            $content = $content.Replace("app.http('InvokeOutputBinding', {", "app.http('`$(FUNCTION_NAME_INPUT)-Invoke', {")
            $content = $content.Replace('route: "invoke/{appId}/{methodName}"', 'route: "$(FUNCTION_NAME_INPUT)/invoke/{appId}/{methodName}"')
            $content = $content.Replace('http://localhost:7071/api/invoke/functionapp/{yourFunctionName}',
                'http://localhost:7071/api/$(FUNCTION_NAME_INPUT)/invoke/functionapp/$(FUNCTION_NAME_INPUT)')
            $content = $content.Replace('--method {yourFunctionName}', '--method $(FUNCTION_NAME_INPUT)')
        }
    }
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
        $mapping = Get-PropertyMapping $id $templateInput
        $property = $mapping[0]
        $literal = $mapping[1]
        $declaration = "    ${property}: $literal,"
        $pattern = '(?m)^' + [regex]::Escape($declaration) + '(?=\r?$)'
        if ([regex]::Matches($content, $pattern).Count -ne 1) {
            throw "The owned '$property' declaration in '$id' changed or is ambiguous. Review its parameter association."
        }
        $token = "__FUNC_NODE_$($templateInput.paramId)__"
        $isBoolean = $templateInput.paramId -ceq 'cosmosTrigger-createLease'
        if ($isBoolean) {
            if ($symbol.datatype -cne 'choice' -or (@($symbol.choices.choice) -join ',') -cne 'true,false') {
                throw "The owned Boolean choices in '$id' changed."
            }
            $symbols.Add("$($templateInput.paramId)-NodeBoolean", @{
                type = 'derived'; valueSource = $templateInput.paramId; valueTransform = 'lowerCase'; replaces = $token
            })
            $replacement = "    ${property}: $token,"
        }
        else {
            if ($symbol.datatype -cne 'string') { throw "The owned string parameter in '$id' changed type." }
            $symbols.Add("$($templateInput.paramId)-NodeString", @{
                type = 'derived'; valueSource = $templateInput.paramId; valueTransform = 'nodeString'; replaces = $token
            })
            $replacement = "    ${property}: '$token',"
        }
        $content = [regex]::Replace($content, $pattern, [System.Text.RegularExpressions.MatchEvaluator] { param($match) $replacement })
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
        forms = @{
            nodeString = @{ identifier = 'chain'; steps = @('jsonEncode', 'nodeStringContent', 'nodeApostrophe') }
            nodeStringContent = @{ identifier = 'replace'; pattern = '^"|"$'; replacement = '' }
            nodeApostrophe = @{ identifier = 'replace'; pattern = "'"; replacement = '\u0027' }
        }
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
$mappings = @($templates | ForEach-Object {
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
$projects = @('project-js', 'project-ts') | ForEach-Object {
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
