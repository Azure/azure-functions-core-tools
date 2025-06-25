# Usage: build.ps1 -rid <RID> [-artifactDirectory <path>] [-isMinified <true|false>]
# Example: build.ps1 -rid win-x64

param (
    [Parameter(Mandatory = $true)]
    [string]$rid,

    [string]$artifactDirectory = (Join-Path (Get-Location) "_artifacts"),

    [bool]$isMinified = $false
)

# Set default/out-of-proc CLI directory names
$defaultDirectoryName = "core-tools-default";
$cliSubDirectoryName = "func-cli";

# Set in-proc CLI directory names
$inprocDirectoryName = "core-tools-inproc";
$inproc6SubDirectoryName = "inproc6";
$inproc8SubDirectoryName = "inproc8";

# Set host directory names
$hostDirectoryName = "core-tools-host";
$windowsSubDirectoryName = "windows";
$linuxSubDirectoryName = "linux";

# ------ Set environment variables for artifact assembler ------
# Set directories for the artifacts
$env:OUT_OF_PROC_ARTIFACT_ALIAS = $defaultDirectoryName
$env:IN_PROC_ARTIFACT_ALIAS = $inprocDirectoryName
$env:CORETOOLS_HOST_ARTIFACT_ALIAS = $hostDirectoryName

# Set subdirectories for the artifacts
$env:IN_PROC6_ARTIFACT_NAME = $inproc6SubDirectoryName
$env:IN_PROC8_ARTIFACT_NAME = $inproc8SubDirectoryName
$env:OUT_OF_PROC_ARTIFACT_NAME = $cliSubDirectoryName
$env:CORETOOLS_HOST_WINDOWS_ARTIFACT_NAME = $windowsSubDirectoryName
$env:CORETOOLS_HOST_LINUX_ARTIFACT_NAME = $linuxSubDirectoryName

# Create artifact directory if it doesn't exist
if (-not (Test-Path -Path $artifactDirectory)) {
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
}

# Build shared MSBuild properties
$minifiedProp = if ($isMinified) { "/p:IsMinified=true" } else { "" }

# --- Out-of-proc build ---
Write-Host "`n--- Building Out-of-Proc CLI (net8.0) ---`n"

dotnet build src/Cli/func -c release -f "net8.0" -r $rid --self-contained $minifiedProp

dotnet publish src/Cli/func -c release -f "net8.0" -r $rid --self-contained --no-build `
    /p:ZipAfterPublish=true `
    /p:ZipArtifactsPath="$artifactDirectory/$defaultDirectoryName/$cliSubDirectoryName" `
    $minifiedProp

# --- In-proc builds (net8.0 and net6.0) ---
foreach ($fw in "net8.0", "net6.0") {
    Write-Host "`n--- Building In-Proc CLI ($fw) ---`n"

    $zipName = if ($fw -eq "net8.0") { $inproc8SubDirectoryName } else { $inproc6SubDirectoryName }

    dotnet build src/Cli/func -c release -f $fw -r $rid --self-contained `
        /p:InProcBuild=true $minifiedProp

    dotnet publish src/Cli/func -c release -f $fw -r $rid --self-contained --no-build `
        /p:ZipAfterPublish=true `
        /p:ZipArtifactsPath="$artifactDirectory/$inprocDirectoryName/$zipName" `
        /p:InProcBuild=true `
        $minifiedProp
}

# --- Build Core Tools Host ---
Write-Host "`n--- Building Core Tools Host ---`n"

# Determine current OS platform
if ($IsWindows) {
    $hostRuntimes = @("win-x64", "win-arm64")
} elseif ($IsLinux) {
    $hostRuntimes = @("linux-x64")
} else {
    Write-Warning "CoreToolsHost build is not supported on this OS."
    $hostRuntimes = @()
}

foreach ($runtime in $hostRuntimes) {
    $platform = if ($runtime -like "win*") { $windowsSubDirectoryName } else { $linuxSubDirectoryName }
    $outputPath = Join-Path -Path $artifactDirectory -ChildPath "$hostDirectoryName/$platform/$runtime"

    Write-Host "Building host for $runtime -> $outputPath"

    dotnet publish src/CoreToolsHost -c Release -r $runtime -o $outputPath
}

# Assemble CLI artifacts
Push-Location "$artifactDirectory"

$artifactName = "Azure.Functions.Cli.$rid"

Write-Host "`n--- Assembling CLI artifacts: $artifactName ---`n"
dotnet run --project ../src/Cli/ArtifactAssembler/Azure.Functions.Cli.ArtifactAssembler.csproj -- $artifactName

Write-Host "`n--- Zipping CLI artifacts: $artifactName ---`n"
dotnet run --project ../src/Cli/ArtifactAssembler/Azure.Functions.Cli.ArtifactAssembler.csproj -- zip

Write-Host  "`n--- OUTPUT: $artifactDirectory/release ---`n"
Pop-Location
