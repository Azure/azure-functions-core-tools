#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Capture a finite Azure Functions CLI bug-bash command into a local,
    reviewable Markdown evidence file.

.DESCRIPTION
    Uses one literal isolated CLI executable for version, verbose identity, and
    the reproduction command. Before executing the reproduction, verifies the
    exact CLI version, install-root containment, and five allowlisted bug-bash
    settings against caller-supplied expected values. Captures stdout/stderr in
    memory, applies conservative redaction, and writes only sanitized evidence.

    This helper is not for prompts, cancellation testing, dashboard/TUI
    behavior, or long-running commands such as func run.

.PARAMETER CliPath
    Literal path to the isolated func executable. Command names and aliases such
    as func or func5 are rejected.

.PARAMETER ExpectedCliVersion
    Exact version from the live bug-bash wiki.

.PARAMETER ExpectedInstallPath
    Approved isolated install directory that must contain CliPath.

.PARAMETER ExpectedConfigurationJson
    JSON object containing expected values for exactly these keys:
    FUNC_CLI_WORKLOADS_SOURCE, FUNC_CLI_QUICKSTART_MANIFEST_URL,
    FUNC_CLI_WORKLOADS_PRERELEASE, FUNC_CLI_HOME, and
    FUNC_CLI_WORKLOADS_HOME. Values are compared in memory and never emitted.

.PARAMETER ArgumentList
    Exact CLI argument tokens when invoking this script directly from
    PowerShell.

.PARAMETER ArgumentsJson
    JSON array of exact CLI argument tokens. Use this with pwsh -File from
    PowerShell, bash, or zsh.

.PARAMETER Stack
    Relevant runtime versions to collect: node, python, dotnet, go, powershell.

.PARAMETER HostVersion
    Relevant actual Host version, when known.

.PARAMETER WorkloadVersionsJson
    Optional JSON object mapping relevant workload/package names to actual
    installed versions.

.PARAMETER TargetProfile
    Relevant Azure Functions target-profile name. This is not a shell profile.

.PARAMETER TemplateId
    Relevant template ID.

.PARAMETER OutputDirectory
    New directory that will contain evidence.md.

.PARAMETER ShellName
    Optional participant shell name when automatic detection is insufficient.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $CliPath,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ExpectedCliVersion,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ExpectedInstallPath,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ExpectedConfigurationJson,
    [string[]] $ArgumentList = @(),
    [string] $ArgumentsJson,
    [ValidateSet('node', 'python', 'dotnet', 'go', 'powershell')]
    [string[]] $Stack = @(),
    [string] $HostVersion,
    [string] $WorkloadVersionsJson,
    [string] $TargetProfile,
    [string] $TemplateId,
    [string] $OutputDirectory,
    [string] $ShellName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$configurationVariables = [ordered]@{
    FUNC_CLI_WORKLOADS_SOURCE = 'value'
    FUNC_CLI_QUICKSTART_MANIFEST_URL = 'value'
    FUNC_CLI_WORKLOADS_PRERELEASE = 'value'
    FUNC_CLI_HOME = 'path'
    FUNC_CLI_WORKLOADS_HOME = 'path'
}

function Assert-SafeCommand {
    param(
        [string] $Executable,
        [string[]] $Arguments
    )

    $tokens = @($Executable) + $Arguments
    $blockedPatterns = @(
        '(?i)^--?(?:password|passwd|token|secret|api[-_]?key|account[-_]?key|connection[-_]?string|client[-_]?secret)(?:=|$)',
        '(?i)(?:^|[?&])(?:sig|token|code|key|secret|password|client_secret)=',
        '(?i)\bAuthorization\s*[:=]',
        '(?i)\b(?:AccountKey|SharedAccessSignature|ConnectionString)\s*='
    )

    foreach ($token in $tokens) {
        foreach ($pattern in $blockedPatterns) {
            if ($token -match $pattern) {
                throw 'Refusing to capture a command that may contain sensitive material. Use synthetic input or a manually redacted report.'
            }
        }
    }

    $executableName = [System.IO.Path]::GetFileNameWithoutExtension($Executable).ToLowerInvariant()
    if ($executableName -in @('bash', 'cmd', 'powershell', 'pwsh', 'sh', 'zsh')) {
        throw 'Refusing to capture an opaque shell executable. Pass the literal isolated func executable.'
    }
}

function Resolve-LiteralExecutable {
    param([string] $Path)

    if ($Path -match '[\r\n]') {
        throw 'CliPath must not contain line breaks.'
    }

    $hasDirectorySeparator = $Path.Contains([System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.Path]::AltDirectorySeparatorChar -ne [System.IO.Path]::DirectorySeparatorChar) {
        $hasDirectorySeparator = $hasDirectorySeparator -or $Path.Contains([System.IO.Path]::AltDirectorySeparatorChar)
    }

    if (-not [System.IO.Path]::IsPathRooted($Path) -and -not $hasDirectorySeparator) {
        throw "CliPath must be a literal file path, not a command name or alias: $Path"
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "CLI executable does not exist: $Path"
    }

    $item = Get-Item -LiteralPath $Path -Force
    if ($item.LinkType -eq 'SymbolicLink') {
        $target = $item.ResolveLinkTarget($true)
        if ($null -eq $target) {
            throw "CLI executable symbolic link cannot be resolved: $Path"
        }

        return $target.FullName
    }

    return $item.FullName
}

function Get-NormalizedPath {
    param([string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        return $fullPath.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return $fullPath
}

function Test-PathEquals {
    param(
        [string] $Left,
        [string] $Right
    )

    try {
        $leftPath = Get-NormalizedPath $Left
        $rightPath = Get-NormalizedPath $Right
    }
    catch {
        return $false
    }

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    return [string]::Equals($leftPath, $rightPath, $comparison)
}

function Test-PathWithin {
    param(
        [string] $Candidate,
        [string] $Root
    )

    try {
        $candidatePath = Get-NormalizedPath $Candidate
        $rootPath = Get-NormalizedPath $Root
    }
    catch {
        return $false
    }

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    if ([string]::Equals($candidatePath, $rootPath, $comparison)) {
        return $false
    }

    $rootWithSeparator = $rootPath + [System.IO.Path]::DirectorySeparatorChar
    return $candidatePath.StartsWith($rootWithSeparator, $comparison)
}

function ConvertFrom-StringMapJson {
    param(
        [AllowNull()]
        [string] $Json,
        [string] $Name,
        [string[]] $RequiredKeys = @(),
        [string[]] $AllowedKeys = @()
    )

    if ([string]::IsNullOrWhiteSpace($Json)) {
        if ($RequiredKeys.Count -gt 0) {
            throw "$Name is required."
        }

        return [ordered]@{}
    }

    try {
        $map = ConvertFrom-Json -InputObject $Json -AsHashtable
    }
    catch {
        throw "$Name must be a JSON object with string values: $($_.Exception.Message)"
    }

    if ($map -isnot [System.Collections.IDictionary]) {
        throw "$Name must be a JSON object with string values."
    }

    foreach ($key in $map.Keys) {
        if ($key -isnot [string] -or $map[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($map[$key])) {
            throw "$Name must contain only non-empty string keys and values."
        }
        if ($AllowedKeys.Count -gt 0 -and $key -notin $AllowedKeys) {
            throw "$Name contains an unsupported key: $key"
        }
    }

    foreach ($key in $RequiredKeys) {
        if (-not $map.Contains($key)) {
            throw "$Name is missing required key: $key"
        }
    }

    $orderedMap = [ordered]@{}
    foreach ($key in $map.Keys) {
        $orderedMap[$key] = $map[$key]
    }

    return $orderedMap
}

function Invoke-CapturedProcess {
    param(
        [string] $Executable,
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) {
            throw "Failed to start '$Executable'."
        }

        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        return [pscustomobject]@{
            Started = $true
            ExitCode = $process.ExitCode
            StdOut = $stdoutTask.GetAwaiter().GetResult()
            StdErr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    catch {
        return [pscustomobject]@{
            Started = $false
            ExitCode = $null
            StdOut = ''
            StdErr = $_.Exception.Message
        }
    }
    finally {
        $process.Dispose()
    }
}

function Protect-Text {
    param(
        [AllowNull()]
        [string] $Text,
        [ref] $RedactionCount,
        [string[]] $OmittedValues = @()
    )

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    $result = $Text
    $rules = @(
        @{
            Pattern = '(?i)\bgh[pousr]_[A-Za-z0-9_]{20,}\b'
            Replacement = '[REDACTED_GITHUB_TOKEN]'
        },
        @{
            Pattern = '\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b'
            Replacement = '[REDACTED_JWT]'
        },
        @{
            Pattern = '(?i)([?&](?:sig|token|code|key|secret|password|client_secret)=)[^&\s''"]+'
            Replacement = '$1[REDACTED]'
        },
        @{
            Pattern = '(?im)(\bAuthorization\s*[:=]\s*)(?:Bearer\s+)?[^\s]+'
            Replacement = '$1[REDACTED]'
        },
        @{
            Pattern = '(?im)(\b(?:password|passwd|token|access[_-]?token|refresh[_-]?token|secret|client[_-]?secret|api[_-]?key|account[_-]?key|connection[_-]?string)\b\s*[:=]\s*)(?:"[^"]*"|''[^'']*''|[^\s;]+)'
            Replacement = '$1[REDACTED]'
        },
        @{
            Pattern = '(?im)^.*\b(?:DefaultEndpointsProtocol|AccountEndpoint|BlobEndpoint|QueueEndpoint|TableEndpoint|FileEndpoint|SharedAccessSignature)\s*=.*$'
            Replacement = '[REDACTED_CONNECTION_STRING]'
        }
    )

    foreach ($rule in $rules) {
        $matches = [regex]::Matches($result, $rule.Pattern)
        if ($matches.Count -gt 0) {
            $RedactionCount.Value += $matches.Count
            $result = [regex]::Replace($result, $rule.Pattern, $rule.Replacement)
        }
    }

    foreach ($omittedValue in $OmittedValues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        $pattern = [regex]::Escape($omittedValue)
        if ($omittedValue -match '^[A-Za-z0-9._-]+$') {
            $pattern = "(?<![A-Za-z0-9._-])$pattern(?![A-Za-z0-9._-])"
        }

        $matches = [regex]::Matches($result, $pattern)
        if ($matches.Count -gt 0) {
            $RedactionCount.Value += $matches.Count
            $result = [regex]::Replace($result, $pattern, '[OMITTED_CONFIGURATION_VALUE]')
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($HOME)) {
        $homePattern = [regex]::Escape((Get-NormalizedPath $HOME))
        $homeMatches = [regex]::Matches(
            $result,
            $homePattern,
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($homeMatches.Count -gt 0) {
            $RedactionCount.Value += $homeMatches.Count
            $result = [regex]::Replace(
                $result,
                $homePattern,
                '~',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }

    return $result.TrimEnd()
}

function Format-CommandToken {
    param([string] $Token)

    if ($Token -match '^[A-Za-z0-9_./:\\=@+-]+$') {
        return $Token
    }

    return "'$($Token.Replace("'", "''"))'"
}

function Resolve-Application {
    param([string] $Command)

    $resolved = Get-Command $Command -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $resolved) {
        return $null
    }

    return $resolved.Source
}

function Get-VersionResult {
    param(
        [string[]] $Candidates,
        [string[]] $Arguments
    )

    foreach ($candidate in $Candidates) {
        $executable = Resolve-Application $candidate
        if ($null -eq $executable) {
            continue
        }

        $result = Invoke-CapturedProcess -Executable $executable -Arguments $Arguments
        $combined = @($result.StdOut, $result.StdErr) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $text = ($combined -join [Environment]::NewLine).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = "exit code $($result.ExitCode)"
        }

        return $text
    }

    return 'not found'
}

function ConvertTo-MarkdownCell {
    param([string] $Value)

    $encoded = [System.Net.WebUtility]::HtmlEncode($Value)
    return $encoded.Replace('|', '\|').Replace("`r`n", '<br>').Replace("`n", '<br>')
}

function Get-CodeFence {
    param([string[]] $Values)

    $maximum = 2
    foreach ($value in $Values) {
        foreach ($match in [regex]::Matches($value, '`+')) {
            if ($match.Value.Length -gt $maximum) {
                $maximum = $match.Value.Length
            }
        }
    }

    return '`' * ($maximum + 1)
}

if ($PSBoundParameters.ContainsKey('ArgumentList') -and -not [string]::IsNullOrWhiteSpace($ArgumentsJson)) {
    throw 'Use either -ArgumentList or -ArgumentsJson, not both.'
}

$effectiveArguments = $ArgumentList
if (-not [string]::IsNullOrWhiteSpace($ArgumentsJson)) {
    if ($ArgumentsJson -notmatch '^\s*\[') {
        throw 'ArgumentsJson must be a JSON array of strings.'
    }

    try {
        $parsedArguments = @(ConvertFrom-Json -InputObject $ArgumentsJson)
    }
    catch {
        throw "ArgumentsJson must be a JSON array of strings: $($_.Exception.Message)"
    }

    foreach ($argument in $parsedArguments) {
        if ($argument -isnot [string]) {
            throw 'ArgumentsJson must contain only strings.'
        }
    }

    $effectiveArguments = [string[]] $parsedArguments
}

$resolvedCli = Resolve-LiteralExecutable $CliPath
Assert-SafeCommand -Executable $resolvedCli -Arguments $effectiveArguments

$expectedConfiguration = ConvertFrom-StringMapJson `
    -Json $ExpectedConfigurationJson `
    -Name 'ExpectedConfigurationJson' `
    -RequiredKeys @($configurationVariables.Keys) `
    -AllowedKeys @($configurationVariables.Keys)
$workloadVersions = ConvertFrom-StringMapJson `
    -Json $WorkloadVersionsJson `
    -Name 'WorkloadVersionsJson'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Get-Location) "func-v5-evidence-$([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory"
}

$pathStatus = if (Test-PathWithin $resolvedCli $ExpectedInstallPath) { 'set' } else { 'mismatch' }
$configurationStatus = [ordered]@{}
$omittedConfigurationValues = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $configurationVariables.GetEnumerator()) {
    $actualValue = [Environment]::GetEnvironmentVariable($entry.Key)
    $omittedConfigurationValues.Add($expectedConfiguration[$entry.Key])
    if (-not [string]::IsNullOrWhiteSpace($actualValue)) {
        $omittedConfigurationValues.Add($actualValue)
    }

    if ([string]::IsNullOrWhiteSpace($actualValue)) {
        $configurationStatus[$entry.Key] = 'missing'
        continue
    }

    $expectedValue = $expectedConfiguration[$entry.Key]
    $matches = if ($entry.Value -eq 'path') {
        Test-PathEquals $actualValue $expectedValue
    }
    else {
        [string]::Equals($actualValue, $expectedValue, [System.StringComparison]::Ordinal)
    }

    $configurationStatus[$entry.Key] = if ($matches) { 'set' } else { 'mismatch' }
}

$canInspectCli = $pathStatus -eq 'set' -and -not (
    $configurationStatus.Values |
        Where-Object { $_ -ne 'set' } |
        Select-Object -First 1)
$versionResult = if ($canInspectCli) {
    Invoke-CapturedProcess -Executable $resolvedCli -Arguments @('--version')
}
else {
    [pscustomobject]@{
        Started = $false
        ExitCode = $null
        StdOut = ''
        StdErr = 'CLI identity was not queried because install-path or configuration verification failed.'
    }
}
$actualVersion = $versionResult.StdOut.Trim()
$versionStatus = if (-not $versionResult.Started -or [string]::IsNullOrWhiteSpace($actualVersion)) {
    'missing'
}
elseif ($versionResult.ExitCode -eq 0 -and $actualVersion -ceq $ExpectedCliVersion) {
    'set'
}
else {
    'mismatch'
}

$gateStatus = [ordered]@{
    'Executable under expected install path' = $pathStatus
    'Exact CLI version' = $versionStatus
}

$gateFailures = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $gateStatus.GetEnumerator()) {
    if ($entry.Value -ne 'set') {
        $gateFailures.Add("$($entry.Key): $($entry.Value)")
    }
}
foreach ($entry in $configurationStatus.GetEnumerator()) {
    if ($entry.Value -ne 'set') {
        $gateFailures.Add("$($entry.Key): $($entry.Value)")
    }
}

$verboseResult = if ($gateFailures.Count -eq 0) {
    Invoke-CapturedProcess -Executable $resolvedCli -Arguments @('--verbose')
}
else {
    [pscustomobject]@{
        Started = $false
        ExitCode = $null
        StdOut = ''
        StdErr = 'CLI verbose identity was not queried because the verification gate failed.'
    }
}

$commandResult = if ($gateFailures.Count -eq 0) {
    Invoke-CapturedProcess -Executable $resolvedCli -Arguments $effectiveArguments
}
else {
    [pscustomobject]@{
        Started = $false
        ExitCode = $null
        StdOut = ''
        StdErr = 'Command was not run because the isolation or configuration verification gate failed.'
    }
}

$runtimeVersions = [ordered]@{}
foreach ($stackName in $Stack | Select-Object -Unique) {
    switch ($stackName) {
        'node' {
            $runtimeVersions['Node.js'] = Get-VersionResult -Candidates @('node') -Arguments @('--version')
        }
        'python' {
            $runtimeVersions['Python'] = Get-VersionResult -Candidates @('python3', 'python') -Arguments @('--version')
        }
        'dotnet' {
            $runtimeVersions['.NET SDK'] = Get-VersionResult -Candidates @('dotnet') -Arguments @('--version')
        }
        'go' {
            $runtimeVersions['Go'] = Get-VersionResult -Candidates @('go') -Arguments @('version')
        }
        'powershell' {
            $runtimeVersions['PowerShell'] = $PSVersionTable.PSVersion.ToString()
        }
    }
}

$detectedShell = if (-not [string]::IsNullOrWhiteSpace($ShellName)) {
    $ShellName
}
elseif (-not $IsWindows -and -not [string]::IsNullOrWhiteSpace($env:SHELL)) {
    Split-Path -Leaf $env:SHELL
}
else {
    "PowerShell $($PSVersionTable.PSVersion)"
}

$commandDisplay = (@($resolvedCli) + $effectiveArguments | ForEach-Object { Format-CommandToken $_ }) -join ' '
$redactionCount = 0
$omittedValues = $omittedConfigurationValues.ToArray()
$safeExecutable = Protect-Text -Text $resolvedCli -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeCommand = Protect-Text -Text $commandDisplay -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeStdOut = Protect-Text -Text $commandResult.StdOut -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeStdErr = Protect-Text -Text $commandResult.StdErr -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeVersionStdOut = Protect-Text -Text $versionResult.StdOut -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeVersionStdErr = Protect-Text -Text $versionResult.StdErr -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeVerboseStdOut = Protect-Text -Text $verboseResult.StdOut -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeVerboseStdErr = Protect-Text -Text $verboseResult.StdErr -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeShell = Protect-Text -Text $detectedShell -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeHostVersion = Protect-Text -Text $HostVersion -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeTargetProfile = Protect-Text -Text $TargetProfile -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
$safeTemplateId = Protect-Text -Text $TemplateId -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues

foreach ($key in @($runtimeVersions.Keys)) {
    $runtimeVersions[$key] = Protect-Text -Text $runtimeVersions[$key] -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
}
$safeWorkloadVersions = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $workloadVersions.GetEnumerator()) {
    $safeWorkloadVersions.Add([pscustomobject]@{
        Name = Protect-Text -Text $entry.Key -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
        Version = Protect-Text -Text $entry.Value -RedactionCount ([ref] $redactionCount) -OmittedValues $omittedValues
    })
}

$fence = Get-CodeFence -Values @(
    $safeCommand,
    $safeStdOut,
    $safeStdErr,
    $safeVersionStdOut,
    $safeVersionStdErr,
    $safeVerboseStdOut,
    $safeVerboseStdErr)

$exitCode = if ($commandResult.Started) { $commandResult.ExitCode.ToString() } else { 'not started' }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Azure Functions CLI v5 bug-bash evidence')
$lines.Add('')
$lines.Add("> Generated locally on $([DateTimeOffset]::Now.ToString('o')). Review every line before sharing.")
$lines.Add('> Expected and actual configuration values are intentionally omitted; only the reviewed executable path is retained.')
$lines.Add('> Output is exact except for explicit `[REDACTED...]` or `[OMITTED...]` substitutions and the home-directory replacement `~`.')
$lines.Add('')
$lines.Add('## Verification gate')
$lines.Add('')
$lines.Add('| Check | Status |')
$lines.Add('| --- | --- |')
foreach ($entry in $gateStatus.GetEnumerator()) {
    $lines.Add("| $($entry.Key) | $($entry.Value) |")
}
if ($gateFailures.Count -gt 0) {
    $lines.Add('')
    $lines.Add('The reproduction command was not run because one or more checks were not `set`.')
}
$lines.Add('')
$lines.Add('## Reproduction command')
$lines.Add('')
$lines.Add("- Executable: ``$safeExecutable``")
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($safeCommand)
$lines.Add($fence)
$lines.Add('')
$lines.Add("- Started: ``$(if ($commandResult.Started) { 'yes' } else { 'no' })``")
$lines.Add("- Exit code: ``$exitCode``")
$lines.Add("- Sanitizer replacements: ``$redactionCount``")
$lines.Add('')
$lines.Add('### stdout')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeStdOut)) { '<empty>' } else { $safeStdOut }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('### stderr')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeStdErr)) { '<empty>' } else { $safeStdErr }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('## Azure Functions CLI identity')
$lines.Add('')
$lines.Add("### ``--version`` (exit code: $(if ($versionResult.Started) { $versionResult.ExitCode } else { 'not started' }))")
$lines.Add('')
$lines.Add('#### stdout')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeVersionStdOut)) { '<empty>' } else { $safeVersionStdOut }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('#### stderr')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeVersionStdErr)) { '<empty>' } else { $safeVersionStdErr }))
$lines.Add($fence)
$lines.Add('')
$lines.Add("### ``--verbose`` (exit code: $(if ($verboseResult.Started) { $verboseResult.ExitCode } else { 'not started' }))")
$lines.Add('')
$lines.Add('#### stdout')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeVerboseStdOut)) { '<empty>' } else { $safeVerboseStdOut }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('#### stderr')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeVerboseStdErr)) { '<empty>' } else { $safeVerboseStdErr }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('## Environment')
$lines.Add('')
$lines.Add('| Field | Value |')
$lines.Add('| --- | --- |')
$lines.Add("| OS | $(ConvertTo-MarkdownCell ([System.Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim())) |")
$lines.Add("| OS architecture | $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) |")
$lines.Add("| Process architecture | $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) |")
$lines.Add("| Shell | $(ConvertTo-MarkdownCell $safeShell) |")
$lines.Add("| Capture host | PowerShell $($PSVersionTable.PSVersion) |")
foreach ($entry in $runtimeVersions.GetEnumerator()) {
    $lines.Add("| $($entry.Key) | $(ConvertTo-MarkdownCell $entry.Value) |")
}
if (-not [string]::IsNullOrWhiteSpace($safeHostVersion)) {
    $lines.Add("| Host version | $(ConvertTo-MarkdownCell $safeHostVersion) |")
}
foreach ($entry in $safeWorkloadVersions) {
    $lines.Add("| Workload $(ConvertTo-MarkdownCell $entry.Name) | $(ConvertTo-MarkdownCell $entry.Version) |")
}
if (-not [string]::IsNullOrWhiteSpace($safeTargetProfile)) {
    $lines.Add("| Target profile | $(ConvertTo-MarkdownCell $safeTargetProfile) |")
}
if (-not [string]::IsNullOrWhiteSpace($safeTemplateId)) {
    $lines.Add("| Template ID | $(ConvertTo-MarkdownCell $safeTemplateId) |")
}
$lines.Add('')
$lines.Add('## Bug-bash configuration')
$lines.Add('')
$lines.Add('`set` means the current value exactly matched the caller-supplied expected value. Values are omitted.')
$lines.Add('')
$lines.Add('| Variable | Status |')
$lines.Add('| --- | --- |')
foreach ($entry in $configurationStatus.GetEnumerator()) {
    $lines.Add("| ``$($entry.Key)`` | $($entry.Value) |")
}
$lines.Add('')
$lines.Add('## Review checklist')
$lines.Add('')
$lines.Add('- [ ] The literal executable is the intended isolated preview.')
$lines.Add('- [ ] The command uses only synthetic or public test data.')
$lines.Add('- [ ] No secret, token, key, connection string, customer data, or private endpoint is present.')
$lines.Add('- [ ] stdout and stderr are limited to the relevant reproduction.')
$lines.Add('- [ ] Host, workload, runtime, target-profile, and template details are accurate.')
$lines.Add('- [ ] Any attachment was selected and reviewed separately.')

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$reportPath = Join-Path $OutputDirectory 'evidence.md'
$lines | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host "Evidence written to: $reportPath"
Write-Host "Sanitizer replacements: $redactionCount"
Write-Host 'Review the file before copying it into a report or uploading anything.'

if ($gateFailures.Count -gt 0) {
    exit 3
}
if (-not $commandResult.Started) {
    exit 2
}

exit $commandResult.ExitCode
