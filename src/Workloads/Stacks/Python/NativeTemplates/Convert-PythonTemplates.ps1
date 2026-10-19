# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$channels = @(
    @{ name = 'stable'; id = 'Microsoft.Azure.Functions.ExtensionBundle'; version = '4.35.0'; prefix = 'python-source/stable-'; sha256 = '68D3926BA750E678FA93DB03D118B7CF58B127AA14D0583EAB402037B1C6E783'; resourcesSha256 = 'D5F8FD8CE4BCF92370E307E2205F3CECA6CD7061996F6325083C78C9B8E52CCC' },
    @{ name = 'preview'; id = 'Microsoft.Azure.Functions.ExtensionBundle.Preview'; version = '4.42.0'; prefix = 'python-preview-source/'; sha256 = 'CBFC507BB4EA48E1EE498028F8E9092ED69DC55D5BE102A42400DE9FAAF4DE66'; resourcesSha256 = 'A7FD8AE9C6ACDD0424F7A86167F097E30170932E5986AA7618ACD34E0E456AC3' },
    @{ name = 'experimental'; id = 'Microsoft.Azure.Functions.ExtensionBundle.Experimental'; version = '4.6.0'; prefix = 'python-experimental-source/'; sha256 = '785DC1C5A932AA17432DEEF852900AA1DE5D7E6229FA02AA11D69E1E73380460'; resourcesSha256 = 'D5F8FD8CE4BCF92370E307E2205F3CECA6CD7061996F6325083C78C9B8E52CCC' }
)
$userPromptsSha256 = '57D6A62E005B585748A6A8E63C5B1F10ABA409EDB9B0D10D6F5A3C59627EE713'
$extensionsSha256 = @{
    stable = '194785B2EC6ABFFEDBAA552E5066372D9EE846151A867EA00F059B58ACD6819F'
    preview = '69189EF492F902ED84A5154E404E045360277EC87BA41BD013991DA2FA209EF2'
    experimental = '8CE8FF581595997177D0B3A009EF9B0F551F33E5991B10C87BCEC6C089750FC4'
}
$requirements = Get-Content -Raw (Join-Path $PSScriptRoot 'source/binding-requirements.json') | ConvertFrom-Json -AsHashtable
$hidden = @('DurableFunctionsEntityTrigger-Python', 'DurableFunctionsOrchestration-Python')
$nativeFilenameCorrections = @(
    'DurableFunctionsEntityTrigger-Python',
    'DurableFunctionsOrchestration-Python',
    'EventGridTrigger-Python',
    'ServiceBusTopicTrigger-Python'
)
$records = [ordered]@{}
$inventory = [System.Collections.Generic.List[object]]::new()
$promptsById = @{}
$resources = @{}
$applications = [System.Collections.Generic.List[object]]::new()

function Write-Text([string] $Path, [string] $Text) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Write-Json([string] $Path, [object] $Value) {
    $json = ConvertTo-Json -InputObject (Get-OrderedJsonValue $Value) -Depth 100
    Write-Text $Path ($json.Replace("`r`n", "`n") + "`n")
}

function Get-OrderedJsonValue([object] $Value) {
    if ($Value -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in ($Value.Keys | Sort-Object -CaseSensitive)) {
            $ordered[$key] = Get-OrderedJsonValue $Value[$key]
        }
        return $ordered
    }
    if ($Value -is [array]) {
        return ,@($Value | ForEach-Object { Get-OrderedJsonValue $_ })
    }
    return $Value
}

function Get-Alias([string] $Name) {
    $result = [System.Text.StringBuilder]::new()
    for ($i = 0; $i -lt $Name.Length; $i++) {
        $ch = $Name[$i]
        if ($i -gt 0 -and [char]::IsUpper($ch)) {
            if (-not [char]::IsUpper($Name[$i - 1]) -or ($i + 1 -lt $Name.Length -and [char]::IsLower($Name[$i + 1]))) {
                $result.Append('-') | Out-Null
            }
        }
        $result.Append([char]::ToLowerInvariant($ch)) | Out-Null
    }
    return $result.ToString()
}

foreach ($channel in $channels) {
    $templatesPath = Join-Path $SourceRoot ($channel.prefix + 'templates.json')
    if ((Get-FileHash $templatesPath -Algorithm SHA256).Hash -ne $channel.sha256) {
        throw "Unexpected source digest for $($channel.name)."
    }
    $promptsPath = Join-Path $SourceRoot ($channel.prefix + 'userPrompts.json')
    $resourcesPath = Join-Path $SourceRoot ($channel.prefix + 'resources.json')
    $extensionsPath = Join-Path $SourceRoot ($channel.prefix + 'extensions.json')
    if ((Get-FileHash $promptsPath -Algorithm SHA256).Hash -ne $userPromptsSha256 -or
        (Get-FileHash $resourcesPath -Algorithm SHA256).Hash -ne $channel.resourcesSha256) {
        throw "Unexpected prompt or resource digest for $($channel.name)."
    }
    if ((Get-FileHash $extensionsPath -Algorithm SHA256).Hash -ne $extensionsSha256[$channel.name]) {
        throw "Unexpected bundle binding metadata digest for $($channel.name)."
    }
    $extensionsDestination = Join-Path $PSScriptRoot "source/bundles/$($channel.name)/extensions.json"
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($extensionsDestination)) | Out-Null
    [System.IO.File]::WriteAllBytes($extensionsDestination, [System.IO.File]::ReadAllBytes($extensionsPath))
    $templates = @(Get-Content -Raw $templatesPath | ConvertFrom-Json -AsHashtable | Where-Object language -eq 'python')
    $prompts = @(Get-Content -Raw $promptsPath | ConvertFrom-Json -AsHashtable)
    $localized = Get-Content -Raw $resourcesPath | ConvertFrom-Json -AsHashtable
    foreach ($prompt in $prompts) {
        if ($promptsById.ContainsKey($prompt.id) -and ($promptsById[$prompt.id] | ConvertTo-Json -Depth 100 -Compress) -cne ($prompt | ConvertTo-Json -Depth 100 -Compress)) {
            throw "Prompt '$($prompt.id)' differs across channels; preserve the variants explicitly."
        }
        $promptsById[$prompt.id] = $prompt
    }
    foreach ($entry in $localized.en.GetEnumerator()) {
        if ($resources.ContainsKey($entry.Key) -and $resources[$entry.Key] -cne $entry.Value) {
            throw "Resource '$($entry.Key)' differs across channels; preserve the variants explicitly."
        }
        $resources[$entry.Key] = $entry.Value
    }
    foreach ($template in $templates) {
        if ($records.Contains($template.id)) {
            $existing = $records[$template.id].template
            if (($existing | ConvertTo-Json -Depth 100 -Compress) -cne ($template | ConvertTo-Json -Depth 100 -Compress)) {
                throw "Template '$($template.id)' differs across channels; preserve the variants explicitly."
            }
            $records[$template.id].channels.Add($channel.name)
        }
        else {
            $records[$template.id] = @{ template = $template; channels = [System.Collections.Generic.List[string]]::new() }
            $records[$template.id].channels.Add($channel.name)
        }
    }
    $inventory.Add([ordered]@{
        channel = $channel.name
        bundleId = $channel.id
        bundleVersion = $channel.version
        url = "https://cdn.functions.azure.com/public/ExtensionBundles/$($channel.id)/$($channel.version)/StaticContent/v2/templates/templates.json"
        sha256 = $channel.sha256.ToLowerInvariant()
        userPromptsSha256 = $userPromptsSha256.ToLowerInvariant()
        resourcesSha256 = $channel.resourcesSha256.ToLowerInvariant()
        extensionsSha256 = $extensionsSha256[$channel.name].ToLowerInvariant()
        bundleArchiveUrl = "https://cdn.functions.azure.com/public/ExtensionBundles/$($channel.id)/$($channel.version)/$($channel.id).$($channel.version).zip"
        templateIds = @($templates.id)
    })
}

$source = Join-Path $PSScriptRoot 'source'
Write-Text (Join-Path $source 'binding-requirements.json') (
    [System.IO.File]::ReadAllText((Join-Path $source 'binding-requirements.json')).Replace("`r`n", "`n"))
Write-Json (Join-Path $source 'channels.json') @($inventory)
Write-Json (Join-Path $source 'templates.json') @($records.Values | ForEach-Object { $_.template })
Write-Json (Join-Path $source 'userPrompts.json') @($promptsById.Values | Sort-Object id)
Write-Json (Join-Path $source 'resources.json') $resources

foreach ($record in $records.Values) {
    $template = $record.template
    $id = $template.id
    if (-not $requirements.Contains($id)) {
        throw "Template '$id' has no explicit binding requirements."
    }
    $stem = $id -replace '-Python$', ''
    $path = Join-Path $PSScriptRoot "content/items/$stem"
    $createJob = @($template.jobs | Where-Object type -eq 'CreateNewApp')
    $appendJob = @($template.jobs | Where-Object type -eq 'AppendToFile')
    if ($createJob.Count -ne 1 -or $appendJob.Count -ne 1) {
        throw "Template '$id' must have exactly one create and append job."
    }
    $symbols = [ordered]@{
        FunctionIdentifier = [ordered]@{
            type = 'derived'; valueSource = 'name'; valueTransform = 'pythonIdentifier'; replaces = '__FUNCTION_NAME__'
        }
    }
    $symbolInfo = [ordered]@{}
    $substitutions = @{ '$(FUNCTION_NAME_INPUT)' = '__FUNCTION_NAME__' }
    foreach ($input in $createJob[0].inputs) {
        if ($input.assignTo -in @('$(APP_FILENAME)', '$(FUNCTION_NAME_INPUT)')) {
            continue
        }
        $doc = $promptsById[$input.paramId]
        $symbol = [ordered]@{
            type = 'parameter'
            datatype = 'string'
            replaces = $input.assignTo
            isRequired = ($input.ContainsKey('required') -and $input.required)
        }
        if ($input.ContainsKey('defaultValue')) { $symbol.defaultValue = $input.defaultValue }
        elseif ($null -ne $doc -and $doc.ContainsKey('defaultValue')) { $symbol.defaultValue = $doc.defaultValue }
        if ($null -ne $doc) {
            $description = $doc.label
            if ($description.StartsWith('$') -and $resources.ContainsKey($description.Substring(1))) {
                $description = $resources[$description.Substring(1)]
            }
            $symbol.description = $description
            if ($doc.ContainsKey('enum') -and $doc.enum.Count -gt 0) {
                $symbol.datatype = 'choice'
                $symbol.choices = @($doc.enum | ForEach-Object { @{ choice = $_.value } })
            }
        }
        $symbols[$input.paramId] = $symbol
        $symbolInfo[$input.paramId] = @{ longName = Get-Alias $input.paramId }
    }
    $functionInput = @($createJob[0].inputs | Where-Object assignTo -eq '$(FUNCTION_NAME_INPUT)')
    $defaultName = if ($functionInput.Count -gt 0) { $functionInput[0].defaultValue } else { $id }
    $createAction = @($template.actions | Where-Object { $_.name -in $createJob[0].actions -and $_.type -eq 'WriteToFile' })
    $appendAction = @($template.actions | Where-Object { $_.name -in $appendJob[0].actions -and $_.type -eq 'AppendToFile' })
    if ($createAction.Count -ne 1 -or $appendAction.Count -ne 1) {
        throw "Template '$id' must have exactly one selected create and append target."
    }
    $createTarget = $createAction[0].filePath
    $appendTarget = $appendAction[0].filePath
    foreach ($input in $createJob[0].inputs) {
        if ($input.ContainsKey('defaultValue')) { $createTarget = $createTarget.Replace($input.assignTo, $input.defaultValue) }
    }
    foreach ($input in $appendJob[0].inputs) {
        if ($input.ContainsKey('defaultValue')) { $appendTarget = $appendTarget.Replace($input.assignTo, $input.defaultValue) }
    }
    if ($createTarget -notin @('function_app.py', 'function_app.py.py') -or $appendTarget -ne 'function_app.py') {
        throw "Template '$id' has an unqualified app target; review its native migration explicitly."
    }
    $legacyCreateTarget = $createTarget
    if ($id -cin $nativeFilenameCorrections) {
        if ($legacyCreateTarget -cne 'function_app.py.py') {
            throw "Template '$id' no longer matches the approved native-only filename correction."
        }
        $createTarget = 'function_app.py'
    }
    elseif ($legacyCreateTarget -cne 'function_app.py') {
        throw "Template '$id' requires a separate filename decision; no general normalization is applied."
    }
    if ($createAction[0].filePath -ceq '$(APP_FILENAME)') {
        $filenameInputs = @($createJob[0].inputs | Where-Object assignTo -ceq '$(APP_FILENAME)')
        if ($filenameInputs.Count -ne 1 -or $filenameInputs[0].defaultValue -cne 'function_app.py') {
            throw "Template '$id' requires one original app filename input with the unchanged default."
        }
        $filenameInput = $filenameInputs[0]
        $filenamePrompt = $promptsById[$filenameInput.paramId]
        if ($null -eq $filenamePrompt -or $filenamePrompt.FileExtension -cne '.py') {
            throw "Template '$id' requires the original Python filename prompt."
        }
        $description = $filenamePrompt.label
        if ($description.StartsWith('$') -and $resources.ContainsKey($description.Substring(1))) {
            $description = $resources[$description.Substring(1)]
        }
        $symbols[$filenameInput.paramId] = [ordered]@{
            type = 'parameter'; datatype = 'string'; defaultValue = 'function_app.py'
            description = $description; fileRename = 'function_app.py'; isRequired = $false
        }
        $symbolInfo[$filenameInput.paramId] = @{ longName = 'app-file-name' }
    }
    foreach ($candidate in @('function_app.py', 'function_app.py.py')) {
        if ($candidate -ne $createTarget -and (Test-Path (Join-Path $path $candidate))) {
            throw "Template '$id' retains a stale generated target '$candidate'; review and remove that exact owned file before regeneration."
        }
    }
    $bundleArgs = @($channels | Where-Object { $_.name -in $record.channels } | ForEach-Object {
        [ordered]@{ id = $_.id; version = '[4.0.0,)' }
    })
    $config = [ordered]@{
        '$schema' = 'http://json.schemastore.org/template'
        author = $template.author
        identity = "Azure.Functions.Python.$stem"
        groupIdentity = "Azure.Functions.Python.$stem"
        name = $template.name
        shortName = @($id)
        tags = @{ type = 'item'; language = 'Python' }
        defaultName = $defaultName
        preferNameDirectory = $false
        symbols = $symbols
        forms = [ordered]@{
            pythonCharacters = @{ identifier = 'replace'; pattern = '[^A-Za-z0-9_]'; replacement = '_' }
            pythonLeadingDigit = @{ identifier = 'replace'; pattern = '^(?=[0-9])'; replacement = '_' }
            pythonIdentifier = @{ identifier = 'chain'; steps = @('pythonCharacters', 'pythonLeadingDigit') }
        }
        constraints = [ordered]@{
            host = @{ type = 'host'; args = @(@{ hostname = 'func' }) }
            stack = @{ type = 'func-workload'; args = 'python' }
            bundle = @{ type = 'func-bundle'; args = $bundleArgs }
            bindings = @{ type = 'func-bundle-bindings'; args = @($requirements[$id]) }
        }
    }
    if ($template.ContainsKey('description')) {
        $description = $template.description
        if ($description.StartsWith('$') -and $resources.ContainsKey($description.Substring(1))) { $description = $resources[$description.Substring(1)] }
        $config.description = $description
    }
    Write-Json (Join-Path $path '.template.config/template.json') $config
    Write-Json (Join-Path $path '.template.config/func.host.json') @{ isHidden = ($id -in $hidden); symbolInfo = $symbolInfo }
    $helperIdentity = $config.identity + '.Append'
    $helperPath = Join-Path $PSScriptRoot "content/append/$stem"
    $helperConfig = $config | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable
    $helperConfig.identity = $helperIdentity
    $helperConfig.groupIdentity = $helperIdentity
    $helperConfig.shortName = @("__python-append-$stem")
    $helperConfig.tags['func-role'] = 'python-append-helper'
    $helperConfig.primaryOutputs = @(@{ path = 'function_body.py' })
    Write-Json (Join-Path $helperPath '.template.config/template.json') $helperConfig
    Write-Json (Join-Path $helperPath '.template.config/func.host.json') @{ isHidden = $true; symbolInfo = $symbolInfo }
    $create = $template.files.'function_app.py'
    $snippet = $template.files.'function_body.py'
    foreach ($entry in $substitutions.GetEnumerator()) {
        $create = $create.Replace($entry.Key, $entry.Value)
        $snippet = $snippet.Replace($entry.Key, $entry.Value)
    }
    Write-Text (Join-Path $path $createTarget) $create
    Write-Text (Join-Path $PSScriptRoot "snippets/$stem/function_body.py") $snippet
    $applications.Add([ordered]@{
        templateIdentity = $config.identity
        createTarget = $createTarget
        legacyCreateTarget = $legacyCreateTarget
        nativeFilenameCorrected = ($legacyCreateTarget -cne $createTarget)
        appendTarget = $appendTarget
        createSource = "content/items/$stem/$createTarget"
        appendSource = "snippets/$stem/function_body.py"
        createSourceSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($create))).ToLowerInvariant()
        appendSourceSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($snippet))).ToLowerInvariant()
        configuration = "content/items/$stem/.template.config/template.json"
        configurationSha256 = (Get-FileHash (Join-Path $path '.template.config/template.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        appendHelper = [ordered]@{
            templateIdentity = $helperIdentity
            role = 'internal-expansion'
            configuration = "content/append/$stem/.template.config/template.json"
            configurationSha256 = (Get-FileHash (Join-Path $helperPath '.template.config/template.json') -Algorithm SHA256).Hash.ToLowerInvariant()
            source = "content/append/$stem/function_body.py"
            sourceSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($snippet))).ToLowerInvariant()
            expectedOutput = 'function_body.py'
            expectedOutputKind = 'File'
        }
    })
}

Write-Json (Join-Path $source 'applications.json') ([ordered]@{
    schemaVersion = 1
    packageId = 'Azure.Functions.Templates.Python'
    version = '__PACKAGE_VERSION__'
    sources = @('templates.json', 'userPrompts.json', 'resources.json', 'channels.json', 'binding-requirements.json') | ForEach-Object {
        [ordered]@{
            path = "source/$_"
            sha256 = (Get-FileHash (Join-Path $source $_) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    project = [ordered]@{
        templateIdentity = 'Azure.Functions.Python.Basic'
        sources = '__PROJECT_SOURCES__'
    }
    applications = @($applications)
})
Write-Host "Converted $($records.Count) Python items and internal append helpers from $($inventory.Count) pinned channels."
