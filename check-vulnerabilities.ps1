param
(
    [String[]]
    $CsprojFilePath
)

if (-not $CsprojFilePath) {
    $CsprojFilePath = @(
        "$PSScriptRoot/src/Azure.Functions.Cli/Azure.Functions.Cli.csproj"
        "$PSScriptRoot/test/Azure.Functions.Cli.Tests/Azure.Functions.Cli.Tests.csproj"
        "$PSScriptRoot/build/Build.csproj"
    )
}

$logFilePath = "$PSScriptRoot/build.log"
foreach ($projectFilePath in $CsprojFilePath) {
    Write-Host "`r`nAnalyzing '$projectFilePath' for vulnerabilities..."
    $projectFolder = Split-Path $projectFilePath

    try {
        Push-Location $projectFolder

        # Restore and analyze the project
        & { dotnet restore $projectFilePath }
        if ($LASTEXITCODE -ne 0) {
            throw "Command 'dotnet restore $projectFilePath' failed with exit code $LASTEXITCODE"
        }

        & { dotnet list $projectFilePath package --include-transitive --vulnerable } 3>&1 2>&1 > $logFilePath
        if ($LASTEXITCODE -ne 0) {
            throw "Command 'dotnet list $projectFilePath package --include-transitive --vulnerable' failed with exit code $LASTEXITCODE"
        }

        # Check and report if vulnerabilities are found
        if (-not (Test-Path $logFilePath)) {
            throw "Log file '$logFilePath' was not generated."
        }

        $report = Get-Content $logFilePath -Raw
        $result = $report | Select-String "has no vulnerable packages given the current sources"

        if ($result) {
            Write-Host "No vulnerabilities found" -ForegroundColor Green
        }
        else {
            $output = [System.Environment]::NewLine + "Vulnerabilities found!"
            $output += $report

            Write-Host $output -ForegroundColor Red
            Exit 1
        }
    }
    finally {
        Pop-Location

        if (Test-Path $logFilePath) {
            Remove-Item $logFilePath -Force
        }
    }
}