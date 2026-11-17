[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'win-x64')]
    [string] $Runtime,
    [string] $Command = 'all',
    [ValidateSet('short', 'dry')]
    [string] $Job = 'short',
    [string] $OutputDirectory,
    [ValidateRange(1024, 65535)]
    [int] $Port = 5010,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Runtime) {
    $Runtime = if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else {
        throw 'Unsupported OS. Run on Windows or Linux x64, or pass -Runtime.'
    }
}
if (($Runtime -eq 'linux-x64' -and -not $IsLinux) -or ($Runtime -eq 'win-x64' -and -not $IsWindows)) {
    throw "Run the benchmark on the OS matching '$Runtime'."
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repo 'out/perf'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$inputDirectory = Join-Path $OutputDirectory 'input'
$results = Join-Path $OutputDirectory 'results'
$cliDirectory = Join-Path $inputDirectory 'cli'
$agentWork = Join-Path ([IO.Path]::GetTempPath()) ("func-perf-agent-" + [Guid]::NewGuid().ToString('N'))
foreach ($stale in $inputDirectory, $results) {
    if ((Test-Path -LiteralPath $stale) -and -not $Force) {
        throw "Benchmark inputs already exist in '$OutputDirectory'. Pass -Force to replace them or choose a fresh output directory."
    }
}
if ($Force) {
    foreach ($stale in $inputDirectory, $results) {
        if (Test-Path -LiteralPath $stale) {
            Remove-Item -LiteralPath $stale -Recurse -Force
        }
    }
}
New-Item -ItemType Directory -Path $inputDirectory, $results -Force | Out-Null

$agent = $null
Push-Location $repo
try {
    dotnet restore perf/EndToEnd/Performance.EndToEnd.csproj -r $Runtime -p:SelfContained=true
    dotnet publish perf/EndToEnd/Performance.EndToEnd.csproj -c release -r $Runtime `
        --self-contained true --no-restore -o $inputDirectory
    dotnet restore src/Func/Func.csproj -r $Runtime -p:SelfContained=true
    dotnet publish src/Func/Func.csproj -c release -r $Runtime --self-contained true --no-restore `
        -p:PublishSingleFile=true -o $cliDirectory

    dotnet tool restore --tool-manifest (Join-Path $PSScriptRoot '.config/dotnet-tools.json') `
        --configfile (Join-Path $repo 'NuGet.Config')

    $executable = Join-Path $cliDirectory $(if ($IsWindows) { 'func.exe' } else { 'func' })
    $harnessName = 'Azure.Functions.Cli.Performance.EndToEnd'
    if ($IsWindows) {
        $harnessName += '.exe'
    }
    $harness = Join-Path $inputDirectory $harnessName
    $allCommandsOutput = & $harness --list-commands
    $allCommands = @($allCommandsOutput | ConvertFrom-Json)
    $expected = @($allCommands | Where-Object { $Command -eq 'all' -or $_ -eq $Command })
    if ($expected.Count -eq 0) {
        throw "Unknown benchmark command '$Command'."
    }
    $metadata = [ordered]@{
        commit = (git rev-parse HEAD)
        runtime = $Runtime
        sdk = (dotnet --version)
        tools = (Get-Content (Join-Path $PSScriptRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json).tools
        executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
        executableBytes = (Get-Item -LiteralPath $executable).Length
        commands = $expected
        job = $Job
        cache = 'warm filesystem; isolated CLI home; no workloads; seeded update cache'
        timestampUtc = [DateTime]::UtcNow.ToString('o')
    }
    $metadata | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $results 'provenance.json')

    Push-Location $PSScriptRoot
    try {
        $dotnet = (Get-Command dotnet -CommandType Application).Source
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
        try {
            $listener.Start()
        }
        finally {
            $listener.Stop()
        }
        $agent = Start-Process -FilePath $dotnet -ArgumentList @(
            'tool', 'run', 'crank-agent', '--', '--url', "http://127.0.0.1:$Port",
            '--build-path', "`"$agentWork`""
        ) -PassThru -RedirectStandardOutput (Join-Path $results 'agent.log') `
            -RedirectStandardError (Join-Path $results 'agent.stderr.log')
        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ($agent.HasExited) {
                throw "Crank agent exited with $($agent.ExitCode); see agent logs."
            }
            try {
                Invoke-RestMethod "http://127.0.0.1:$Port/info" -TimeoutSec 1 | Out-Null
                $ready = $true
                break
            }
            catch [System.Net.Http.HttpRequestException] {
                Start-Sleep -Seconds 1
            }
            catch [System.Threading.Tasks.TaskCanceledException] {
                Start-Sleep -Seconds 1
            }
        }
        if (-not $ready) {
            throw 'Crank agent did not become ready.'
        }
        $json = Join-Path $results 'results.json'
        $harnessExecutable = "benchmarks/$harnessName"
        $crankArguments = @(
            '--config', (Join-Path $PSScriptRoot 'commands.benchmarks.yml'),
            '--scenario', 'commands',
            '--profile', 'local',
            '--variable', "agentUrl=http://127.0.0.1:$Port",
            '--application.sources.benchmarks.localFolder', $inputDirectory,
            '--application.executable', $harnessExecutable,
            '--application.variables.command', $Command,
            '--application.variables.benchmarkJob', $Job
        )
        if ($IsLinux) {
            $crankArguments += @('--application.beforeScript', "chmod +x $harnessExecutable")
        }
        $crankArguments += @(
            '--application.options.downloadFilesOutput', $results,
            '--application.options.downloadOutputOutput', (Join-Path $results 'benchmark.log'),
            '--json', $json
        )
        dotnet tool run crank -- @crankArguments 2>&1 |
            Tee-Object -FilePath (Join-Path $results 'controller.log')
        & (Join-Path $PSScriptRoot 'validate-results.ps1') -Path $json -ExpectedCommands $expected
        $reports = @(Get-ChildItem -Path $results -Recurse -Filter '*-default.md')
        if ($reports.Count -eq 0) {
            throw 'Crank did not download the readable benchmark report.'
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($null -ne $agent -and -not $agent.HasExited) {
        $agent.Kill($true)
        $agent.WaitForExit()
    }
    if ($null -ne $agent) {
        $agent.Dispose()
    }
    if (Test-Path -LiteralPath $agentWork) {
        Remove-Item -LiteralPath $agentWork -Recurse -Force
    }
    Pop-Location
}
