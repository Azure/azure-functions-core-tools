param (
    [string]$constantsFilePath = "src\Cli\func\Common\Constants.cs"
)

$telemetryKeyToReplace = "00000000-0000-0000-0000-000000000000"
$instrumentationKey = $env:TELEMETRY_INSTRUMENTATION_KEY

Write-Host "Replacing telemetry key in '$constantsFilePath'."

# Check if the key is not null or empty
if (![string]::IsNullOrWhiteSpace($instrumentationKey)) {
    # Read the file contents
    $constantsFileText = Get-Content -Path $constantsFilePath -Raw

    # Count how many times the placeholder appears
    $matchCount = ([regex]::Matches($constantsFileText, [regex]::Escape($telemetryKeyToReplace))).Count

    if ($matchCount -ne 1) {
        throw "Could not find exactly one '$telemetryKeyToReplace' in '$constantsFilePath' to replace. Found: $matchCount."
    }

    # Replace the key
    $constantsFileText = $constantsFileText -replace [regex]::Escape($telemetryKeyToReplace), $instrumentationKey

    # Ensure the content ends with a single newline
    $constantsFileText = $constantsFileText.TrimEnd() + "`n"

    # Write the modified content back to the file
    Set-Content -Path $constantsFilePath -Value $constantsFileText -NoNewline
    Write-Host "Telemetry key updated."
}
else {
    Write-Host "No telemetry key provided. Skipping replacement."
}
