# Define constants
$rootDir = Join-Path $PSScriptRoot "../.." # Path to the root of the repository
$rootDir = Resolve-Path $rootDir

$srcProjectPath = "$rootDir/src/Cli/func/"
$constantsFile = Join-Path $srcProjectPath "Common/Constants.cs"
$telemetryKeyToReplace = "00000000-0000-0000-0000-000000000000"
$instrumentationKey = $env:TELEMETRY_INSTRUMENTATION_KEY

# Check if the key is not null or empty
if (![string]::IsNullOrWhiteSpace($instrumentationKey)) {
    # Read the file contents
    $constantsFileText = Get-Content -Path $constantsFile -Raw

    # Count how many times the placeholder appears
    $matchCount = ([regex]::Matches($constantsFileText, [regex]::Escape($telemetryKeyToReplace))).Count

    if ($matchCount -ne 1) {
        throw "Could not find exactly one '$telemetryKeyToReplace' in '$constantsFile' to replace. Found: $matchCount"
    }

    # Replace the key
    $constantsFileText = $constantsFileText -replace [regex]::Escape($telemetryKeyToReplace), $instrumentationKey

    # Write the modified content back to the file
    Set-Content -Path $constantsFile -Value $constantsFileText
    Write-Host "Telemetry key updated"
}
