param (
    [string]$tempTestsFile
)

# Debugging: Output the temp file path
Write-Host "Temp file path: '$tempTestsFile'"

# Check if the test file exists
if (-not (Test-Path -Path $tempTestsFile)) {
    Write-Host "Error: Test file not provided or does not exist."
    Get-ChildItem -Path (Split-Path -Path $tempTestsFile) -Force  # List files in the directory for debugging
    exit 1
}

# Read the test names from the file into an array
$tests = Get-Content -Path $tempTestsFile
$testCount = $tests.Count
[int]$totalAgents = $env:SYSTEM_TOTALJOBSINPHASE
[int]$agentNumber = $env:SYSTEM_JOBPOSITIONINPHASE

if (-not $totalAgents) { $totalAgents = 1 }
if (-not $agentNumber) { $agentNumber = 1 }

Write-Host "Total agents: $totalAgents"
Write-Host "Agent number: $agentNumber"
Write-Host "Total tests: $testCount"

Write-Host "Target tests:"
$filter = ""
for ($i = $agentNumber; $i -le $testCount; $i += $totalAgents) {
    Write-Host "Current index: $i"
    $targetTestName = $tests[$i - 1]  # Arrays are 0-indexed in PowerShell
    Write-Host "$targetTestName"
    $filter += "|Name=$targetTestName"
}

# Remove the leading "|" from the filter string
$filter = $filter.TrimStart('|')

Write-Host "Value of filter: $filter"

# Set the filter variable for Azure DevOps pipeline
Write-Output "##vso[task.setvariable variable=targetTestsFilter]$filter"