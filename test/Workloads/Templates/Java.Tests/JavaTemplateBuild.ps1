$ErrorActionPreference = 'Stop'

$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\src\Workloads\Templates\Java')).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) "func-java-templates-$([guid]::NewGuid().ToString('N'))"
$hive = Join-Path $root 'hive'
$project = Join-Path $root 'project'
$packageName = 'com.example.functions'

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command' failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $root | Out-Null
try {
    Invoke-Checked 'dotnet' @('new', 'install', $templateRoot, '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'func-java', '--name', 'Sample.App', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    $starterSource = Join-Path $project 'src\main\java\com\example\functions\FunctionHelloWorldSample.java'
    $originalStarter = Get-Content -Raw -LiteralPath $starterSource
    $conflictOutput = & dotnet new httptrigger --name HelloWorldSample --output $project --PackageName $packageName --debug:custom-hive $hive 2>&1
    if ($LASTEXITCODE -eq 0 -or (Get-Content -Raw -LiteralPath $starterSource) -ne $originalStarter) {
        throw "The template did not protect the starter function: $conflictOutput"
    }
    Invoke-Checked 'dotnet' @('new', 'httptrigger', '--name', 'HelloWorld', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'httptrigger', '--name', 'Orders', '--output', $project,
        '--PackageName', $packageName, '--AuthLevel', 'anonymous', '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'httptrigger', '--name', 'Get-Orders', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    $ordersSource = Join-Path $project 'src\main\java\com\example\functions\FunctionGetOrders.java'
    $originalOrders = Get-Content -Raw -LiteralPath $ordersSource
    $conflictOutput = & dotnet new httptrigger --name GetOrders --output $project --PackageName $packageName --debug:custom-hive $hive 2>&1
    if ($LASTEXITCODE -eq 0 -or (Get-Content -Raw -LiteralPath $ordersSource) -ne $originalOrders) {
        throw "The template did not protect the existing function: $conflictOutput"
    }
    Invoke-Checked 'dotnet' @('new', 'timertrigger', '--name', 'Cleanup', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'httptrigger', '--name', 'class', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'timertrigger', '--name', 'CON', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    Invoke-Checked 'dotnet' @('new', 'timertrigger', '--name', 'host', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)
    # Maven replaces the staged lib directory when copying dependencies.
    Invoke-Checked 'dotnet' @('new', 'httptrigger', '--name', 'lib', '--output', $project,
        '--PackageName', $packageName, '--debug:custom-hive', $hive)

    Push-Location $project
    try {
        Invoke-Checked 'mvn' @('--batch-mode', '-q', 'clean', 'package', '-DskipTests')
    }
    finally {
        Pop-Location
    }

    $staged = Join-Path $project 'target\azure-functions\sample-app'
    if (-not (Test-Path -LiteralPath (Join-Path $staged 'host.json'))) {
        throw 'The Functions host.json was not staged at the app root.'
    }

    $expectedEntries = @{
        HelloWorldSample = "$packageName.FunctionHelloWorldSample.run"
        HelloWorld = "$packageName.FunctionHelloWorld.run"
        Orders = "$packageName.FunctionOrders.run"
        GetOrders = "$packageName.FunctionGetOrders.run"
        Cleanup = "$packageName.FunctionCleanup.run"
        Class = "$packageName.FunctionClass.run"
        FunctionCON = "$packageName.FunctionFunctionCON.run"
        FunctionHost = "$packageName.FunctionFunctionHost.run"
        FunctionLib = "$packageName.FunctionFunctionLib.run"
    }
    foreach ($name in $expectedEntries.Keys) {
        $metadata = Get-Content -Raw -LiteralPath (Join-Path $staged "$name\function.json") | ConvertFrom-Json
        if ($metadata.entryPoint -ne $expectedEntries[$name]) {
            throw "Unexpected entry point for '$name': $($metadata.entryPoint)"
        }
    }

    $http = Get-Content -Raw -LiteralPath (Join-Path $staged 'Orders\function.json') | ConvertFrom-Json
    $timer = Get-Content -Raw -LiteralPath (Join-Path $staged 'Cleanup\function.json') | ConvertFrom-Json
    if (($http.bindings | Where-Object type -EQ 'httpTrigger').authLevel -ne 'ANONYMOUS') {
        throw 'The generated HTTP authorization level was not packaged.'
    }

    if (($timer.bindings | Where-Object type -EQ 'timerTrigger').schedule -ne '0 */5 * * * *') {
        throw 'The generated timer schedule was not packaged.'
    }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
