#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Capture a finite Azure Functions CLI bug-bash command into a local,
    reviewable Markdown evidence file.

.DESCRIPTION
    Invokes an executable directly without evaluating a shell string. Captures
    stdout and stderr in memory, applies conservative redaction, and writes only
    the sanitized result. Records allowlisted bug-bash variable presence but
    never their values.

    This helper is not for interactive prompts, cancellation testing, TUI
    behavior, or long-running commands such as func run.

.PARAMETER FilePath
    Executable name or path, normally func.

.PARAMETER ArgumentList
    Exact argument tokens passed to the executable when invoking this script
    directly from PowerShell.

.PARAMETER ArgumentsJson
    JSON array of exact argument tokens. Use this with pwsh -File from
    PowerShell, bash, or zsh.

.PARAMETER Stack
    Relevant runtime versions to collect: node, python, dotnet, go, powershell.

.PARAMETER OutputDirectory
    New directory that will contain evidence.md.

.PARAMETER FuncPath
    Executable name or path used for func --version and func --verbose.

.PARAMETER ShellName
    Optional participant shell name when automatic detection is insufficient.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $FilePath,
    [string[]] $ArgumentList = @(),
    [string] $ArgumentsJson,
    [ValidateSet('node', 'python', 'dotnet', 'go', 'powershell')]
    [string[]] $Stack = @(),
    [string] $OutputDirectory,
    [string] $FuncPath = 'func',
    [string] $ShellName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
                throw "Refusing to capture a command that may contain sensitive material. Use synthetic input or a manually redacted report."
            }
        }
    }

    $executableName = [System.IO.Path]::GetFileNameWithoutExtension($Executable).ToLowerInvariant()
    $shellExecutables = @('bash', 'cmd', 'powershell', 'pwsh', 'sh', 'zsh')
    $evaluationFlags = @('-c', '-command', '-encodedcommand', '-e', '-ec', '/c')
    if ($executableName -in $shellExecutables) {
        foreach ($argument in $Arguments) {
            if ($argument.ToLowerInvariant() -in $evaluationFlags) {
                throw "Refusing to capture an opaque shell command. Invoke the target executable directly with explicit argument tokens."
            }
        }
    }
}

function Resolve-Application {
    param([string] $Command)

    if (Test-Path -LiteralPath $Command -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Command).Path
    }

    $resolved = Get-Command $Command -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $resolved) {
        return $null
    }

    return $resolved.Source
}

function Invoke-CapturedProcess {
    param(
        [string] $Executable,
        [string[]] $Arguments
    )

    if ([string]::IsNullOrWhiteSpace($Executable)) {
        return [pscustomobject]@{
            Started = $false
            ExitCode = $null
            StdOut = ''
            StdErr = 'Executable was not found.'
        }
    }

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
        [ref] $RedactionCount
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

    if (-not [string]::IsNullOrWhiteSpace($HOME)) {
        $homePattern = [regex]::Escape($HOME)
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

    return $Value.Replace('|', '\|').Replace("`r`n", '<br>').Replace("`n", '<br>')
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

Assert-SafeCommand -Executable $FilePath -Arguments $effectiveArguments

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Get-Location) "func-v5-evidence-$([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory"
}

$resolvedCommand = Resolve-Application $FilePath
$commandResult = Invoke-CapturedProcess -Executable $resolvedCommand -Arguments $effectiveArguments

$resolvedFunc = Resolve-Application $FuncPath
$funcVersion = Invoke-CapturedProcess -Executable $resolvedFunc -Arguments @('--version')
$funcVerbose = Invoke-CapturedProcess -Executable $resolvedFunc -Arguments @('--verbose')

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

$variableNames = @(
    'FUNC_CLI_WORKLOADS_SOURCE',
    'FUNC_CLI_QUICKSTART_MANIFEST_URL',
    'FUNC_CLI_WORKLOADS_PRERELEASE'
)
$variableStatus = [ordered]@{}
foreach ($name in $variableNames) {
    $value = [Environment]::GetEnvironmentVariable($name)
    $variableStatus[$name] = if ([string]::IsNullOrWhiteSpace($value)) { 'missing' } else { 'set' }
}

$commandDisplay = (@($FilePath) + $effectiveArguments | ForEach-Object { Format-CommandToken $_ }) -join ' '
$redactionCount = 0
$safeCommand = Protect-Text -Text $commandDisplay -RedactionCount ([ref] $redactionCount)
$safeStdOut = Protect-Text -Text $commandResult.StdOut -RedactionCount ([ref] $redactionCount)
$safeStdErr = Protect-Text -Text $commandResult.StdErr -RedactionCount ([ref] $redactionCount)
$safeFuncVersion = Protect-Text -Text $funcVersion.StdOut -RedactionCount ([ref] $redactionCount)
$safeFuncVersionError = Protect-Text -Text $funcVersion.StdErr -RedactionCount ([ref] $redactionCount)
$safeFuncVerbose = Protect-Text -Text $funcVerbose.StdOut -RedactionCount ([ref] $redactionCount)
$safeFuncVerboseError = Protect-Text -Text $funcVerbose.StdErr -RedactionCount ([ref] $redactionCount)
$safeShell = Protect-Text -Text $detectedShell -RedactionCount ([ref] $redactionCount)

foreach ($key in @($runtimeVersions.Keys)) {
    $runtimeVersions[$key] = Protect-Text -Text $runtimeVersions[$key] -RedactionCount ([ref] $redactionCount)
}

$fence = Get-CodeFence -Values @(
    $safeCommand,
    $safeStdOut,
    $safeStdErr,
    $safeFuncVersion,
    $safeFuncVersionError,
    $safeFuncVerbose,
    $safeFuncVerboseError)

$exitCode = if ($commandResult.Started) { $commandResult.ExitCode.ToString() } else { 'not started' }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Azure Functions CLI v5 bug-bash evidence')
$lines.Add('')
$lines.Add("> Generated locally on $([DateTimeOffset]::Now.ToString('o')). Review every line before sharing.")
$lines.Add('> Output is exact except for explicit `[REDACTED...]` substitutions and the home-directory replacement `~`.')
$lines.Add('')
$lines.Add('## Reproduction command')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($safeCommand)
$lines.Add($fence)
$lines.Add('')
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
$lines.Add('## Azure Functions CLI')
$lines.Add('')
$lines.Add("### ``func --version`` (exit code: $(if ($funcVersion.Started) { $funcVersion.ExitCode } else { 'not started' }))")
$lines.Add('')
$lines.Add('#### stdout')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeFuncVersion)) { '<empty>' } else { $safeFuncVersion }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('#### stderr')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeFuncVersionError)) { '<empty>' } else { $safeFuncVersionError }))
$lines.Add($fence)
$lines.Add('')
$lines.Add("### ``func --verbose`` (exit code: $(if ($funcVerbose.Started) { $funcVerbose.ExitCode } else { 'not started' }))")
$lines.Add('')
$lines.Add('#### stdout')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeFuncVerbose)) { '<empty>' } else { $safeFuncVerbose }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('#### stderr')
$lines.Add('')
$lines.Add($fence + 'text')
$lines.Add($(if ([string]::IsNullOrEmpty($safeFuncVerboseError)) { '<empty>' } else { $safeFuncVerboseError }))
$lines.Add($fence)
$lines.Add('')
$lines.Add('## Environment')
$lines.Add('')
$lines.Add("| Field | Value |")
$lines.Add("| --- | --- |")
$lines.Add("| OS | $(ConvertTo-MarkdownCell ([System.Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim())) |")
$lines.Add("| OS architecture | $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) |")
$lines.Add("| Process architecture | $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) |")
$lines.Add("| Shell | $(ConvertTo-MarkdownCell $safeShell) |")
$lines.Add("| Capture host | PowerShell $($PSVersionTable.PSVersion) |")
foreach ($entry in $runtimeVersions.GetEnumerator()) {
    $lines.Add("| $($entry.Key) | $(ConvertTo-MarkdownCell $entry.Value) |")
}
$lines.Add('')
$lines.Add('## Bug-bash configuration')
$lines.Add('')
$lines.Add('Values are intentionally omitted. Compare them with the live bug-bash wiki before submission.')
$lines.Add('')
$lines.Add('| Variable | Status |')
$lines.Add('| --- | --- |')
foreach ($entry in $variableStatus.GetEnumerator()) {
    $lines.Add("| ``$($entry.Key)`` | $($entry.Value) |")
}
$lines.Add('')
$lines.Add('## Review checklist')
$lines.Add('')
$lines.Add('- [ ] The command uses only synthetic or public test data.')
$lines.Add('- [ ] No secret, token, key, connection string, customer data, or private endpoint is present.')
$lines.Add('- [ ] stdout and stderr are limited to the relevant reproduction.')
$lines.Add('- [ ] Environment and runtime details are accurate.')
$lines.Add('- [ ] Any attachment was selected and reviewed separately.')

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$reportPath = Join-Path $OutputDirectory 'evidence.md'
$lines | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host "Evidence written to: $reportPath"
Write-Host "Sanitizer replacements: $redactionCount"
Write-Host 'Review the file before copying it into a report or uploading anything.'

if (-not $commandResult.Started) {
    exit 2
}

exit $commandResult.ExitCode
