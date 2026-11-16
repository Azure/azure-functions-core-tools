[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'win-x64')]
    [string] $Runtime,
    [string] $Command = 'all',
    [ValidateSet('short', 'dry')]
    [string] $Job = 'short',
    [string] $OutputDirectory,
    [ValidateRange(1024, 65535)]
    [int] $Port = 5010
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Split-Path $PSScriptRoot -Parent
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
# Crank's SDK installer does not quote its internal paths; keep its workspace separate from user output paths.
$agentWork = Join-Path ([IO.Path]::GetTempPath()) ("func-perf-agent-" + [Guid]::NewGuid().ToString('N'))
if ($agentWork.Contains(' ')) {
    throw "Crank requires a temporary directory without spaces. Set TEMP/TMP to a suitable directory before running."
}
if (Test-Path -LiteralPath $inputDirectory) {
    throw "Benchmark inputs already exist in '$OutputDirectory'. Choose a fresh output directory."
}
New-Item -ItemType Directory -Path $cliDirectory, $results -Force | Out-Null

$agent = $null
Push-Location $repo
try {
    dotnet restore src/Func/Func.csproj -r $Runtime -p:SelfContained=true
    dotnet publish src/Func/Func.csproj -c release -r $Runtime --self-contained true --no-restore `
        -p:PublishSingleFile=true -o $cliDirectory

    foreach ($file in 'Func.Performance.csproj', 'Program.cs', 'CommandCase.cs', 'CommandRunner.cs',
        'BenchmarkEnvironment.cs', 'CommandBenchmarks.cs', 'Directory.Build.props',
        'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $inputDirectory
    }
    # The agent builds the uploaded harness with the same SDK as the product build.
    Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination $inputDirectory
    dotnet tool restore --tool-manifest (Join-Path $PSScriptRoot '.config/dotnet-tools.json') `
        --configfile (Join-Path $PSScriptRoot 'NuGet.Config')

    $executable = Join-Path $cliDirectory $(if ($IsWindows) { 'func.exe' } else { 'func' })
    dotnet build perf/Func.Performance.csproj -c release
    $allCommands = @(dotnet run --project perf/Func.Performance.csproj -c release --no-build -- --list-commands |
        ConvertFrom-Json)
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
        dotnet tool run crank -- --config (Join-Path $PSScriptRoot 'commands.benchmarks.yml') `
            --scenario commands --profile local `
            --variable "agentUrl=http://127.0.0.1:$Port" `
            --application.sources.benchmarks.localFolder $inputDirectory `
            --application.sdkVersion $metadata.sdk `
            --application.variables.command $Command --application.variables.benchmarkJob $Job `
            --application.options.downloadFilesOutput $results `
            --application.options.downloadBuildLogOutput (Join-Path $results 'build.log') `
            --application.options.downloadOutputOutput (Join-Path $results 'benchmark.log') `
            --json $json 2>&1 | Tee-Object -FilePath (Join-Path $results 'controller.log')
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
