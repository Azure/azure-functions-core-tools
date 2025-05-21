param (
    [Parameter(Mandatory=$true)]
    [string]$ArtifactDirectory
)

# Look for MSI files
Write-Host "Searching for MSI files in directory: $ArtifactDirectory"
$msiFile = Get-ChildItem -Path $ArtifactDirectory -Include "*.msi" -Recurse | Select-Object -First 1

# Check if MSI was found
if ($null -eq $msiFile) {
    Write-Host "No MSI file found in $ArtifactDirectory, falling back to func.dll"
    # Fall back to func.dll if no MSI is found
    $cli = Get-ChildItem -Path $ArtifactDirectory -Include "func.dll" -Recurse | Select-Object -First 1
    
    if ($null -eq $cli) {
        throw "Error: Neither MSI nor func.dll found in $ArtifactDirectory"
    }
    
    Write-Host "Found func.dll at: $($cli.FullName)"
    $cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion
} 
else {
    Write-Host "Found MSI file: $($msiFile.Name)"
    
    # Extract version using regex - now with support for preview versions
    # First try to match the pattern with a preview tag
    if ($msiFile.Name -match 'func-cli-(\d+\.\d+\.\d+)(?:-([a-zA-Z0-9]+))?') {
        $versionNumber = $matches[1]
        
        # Check if we captured a preview tag
        if ($matches.Count -gt 2 -and $matches[2]) {
            $previewTag = $matches[2]
            $cliVersion = "$versionNumber-$previewTag"
            Write-Host "Extracted version with preview tag: $cliVersion"
        } else {
            $cliVersion = $versionNumber
            Write-Host "Extracted version: $cliVersion"
        }
    } 
    else {
        throw "Error: Could not extract version from MSI filename: $($msiFile.Name)"
    }
}

# Throw an error if cliVersion is not found
if ([string]::IsNullOrEmpty($cliVersion)) {
    throw "Error: Could not determine version from artifacts"
}

# Get build ID from environment variable or use timestamp if not available
$buildId = $env:BUILD_BUILDID

# Create SemVer 2.0 compliant version
# If cliVersion already contains a pre-release tag (like "4.0.7312-preview"), 
# we need to use a different format for the build metadata
if ($cliVersion -match '-') {
    # Version already contains a hyphen, use dot for build metadata
    $semVerVersion = "$cliVersion.$buildId"
} else {
    # Standard format
    $semVerVersion = "$cliVersion+$buildId"
}

Write-Host "Generated SemVer 2.0 version: $semVerVersion"

# Set as pipeline variables for use in subsequent tasks - with isOutput=true to share with other jobs
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion;isOutput=true]$semVerVersion"