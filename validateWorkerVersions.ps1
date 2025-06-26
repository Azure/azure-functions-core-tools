<#
    .SYNOPSIS
        Validates or updates worker package versions in Azure.Functions.Cli.csproj

    .EXAMPLE
        ./validateWorkerVersions.ps1

    .EXAMPLE
        ./validateWorkerVersions.ps1 -Update -HostVersion 4.1037.0

    .EXAMPLE
        ./validateWorkerVersions.ps1 -TargetFramework net8.0
#>

param (
    [Switch]$Update,
    [string]$hostVersion,
    [string]$TargetFramework = "net8.0"
)

function removeBomIfExists([string]$data) {
    if ($data.StartsWith(0xFEFF)) {
        $data = $data.Substring(1)
    }
    return $data
}

$cliCsprojPath = "$PSScriptRoot/src/Azure.Functions.Cli/Azure.Functions.Cli.csproj"
$cliCsprojContent = removeBomIfExists(Get-Content $cliCsprojPath)
$cliCsprojXml = [xml]$cliCsprojContent

# For WebHost only: lookup by TargetFramework
function getWebHostVersion() {
    $groups = $cliCsprojXml.Project.ItemGroup | Where-Object {
        $_.Condition -match $TargetFramework
    }
    foreach ($group in $groups) {
        foreach ($pkg in $group.PackageReference) {
            if ($pkg.Include -eq "Microsoft.Azure.WebJobs.Script.WebHost") {
                return $pkg.Version
            }
        }
    }
    throw "Could not find Microsoft.Azure.WebJobs.Script.WebHost for $TargetFramework"
}

function setWebHostVersion([string]$newVersion) {
    $groups = $cliCsprojXml.Project.ItemGroup | Where-Object {
        $_.Condition -match $TargetFramework
    }
    foreach ($group in $groups) {
        foreach ($pkg in $group.PackageReference) {
            if ($pkg.Include -eq "Microsoft.Azure.WebJobs.Script.WebHost") {
                $oldVersion = $pkg.Version
                $pkg.Version = $newVersion
                Write-Output "Updated WebHost from $oldVersion to $newVersion for $TargetFramework"
                return
            }
        }
    }
    throw "Failed to find WebHost package reference for $TargetFramework"
}

# For workers: unconditional lookup
function getWorkerVersion([string]$packageName) {
    foreach ($group in $cliCsprojXml.Project.ItemGroup) {
        foreach ($pkg in $group.PackageReference) {
            if ($pkg.Include -eq $packageName) {
                return $pkg.Version
            }
        }
    }
    throw "Failed to find $packageName in CLI project"
}

function setWorkerVersion([string]$packageName, [string]$newVersion) {
    foreach ($group in $cliCsprojXml.Project.ItemGroup) {
        foreach ($pkg in $group.PackageReference) {
            if ($pkg.Include -eq $packageName) {
                $oldVersion = $pkg.Version
                $pkg.Version = $newVersion
                Write-Output "Updated $packageName from $oldVersion to $newVersion"
                return
            }
        }
    }
    throw "Failed to find $packageName in CLI project"
}

# WebHost version
if (-Not $hostVersion) {
    $hostVersion = getWebHostVersion
} elseif ($Update) {
    setWebHostVersion $hostVersion
}

# Validate the tag exists
$tagUri = "https://api.github.com/repos/Azure/azure-functions-host/git/refs/tags/v$hostVersion"
$result = Invoke-WebRequest -Uri $tagUri
if ($result.StatusCode -ne 200) {
    throw "Host tag version $hostVersion does not exist."
}
Write-Output "Host version: $hostVersion"

# Fetch worker versions from remote
function getWorkerPropsFileFromHost([string]$filePath) {
    $uri = "https://raw.githubusercontent.com/Azure/azure-functions-host/refs/tags/v$hostVersion/$filePath"
    $content = removeBomIfExists((Invoke-WebRequest -Uri $uri).Content)
    return [xml]$content
}

$workerPropsToWorkerName = @{
    "eng/build/Workers.Node.props"       = @("NodeJsWorker")
    "eng/build/Workers.Java.props"       = @("JavaWorker")
    "eng/build/Workers.Python.props"     = @("PythonWorker")
    "eng/build/Workers.Powershell.props" = @("PowerShellWorker.PS7.0", "PowerShellWorker.PS7.2", "PowerShellWorker.PS7.4")
}

$failedValidation = $false

foreach ($key in $workerPropsToWorkerName.Keys) {
    Write-Output "----------------------------------------------"
    $workerPropsContent = getWorkerPropsFileFromHost $key
    $workerList = $workerPropsToWorkerName[$key]

    foreach ($worker in $workerList) {
        Write-Output "Validating $worker version..."
        $packageName = "Microsoft.Azure.Functions.$worker"

        # Host version from .props file
        $node = $workerPropsContent.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq $packageName }
        if (-not $node) {
            throw "Could not find $packageName in $key"
        }
        $hostWorkerVersion = $node.Version
        $cliWorkerVersion = getWorkerVersion $packageName

        Write-Output "CLI version: $cliWorkerVersion | Host version: $hostWorkerVersion"

        if ($Update -and $hostWorkerVersion -ne $cliWorkerVersion) {
            setWorkerVersion $packageName $hostWorkerVersion
        } elseif ($hostWorkerVersion -ne $cliWorkerVersion) {
            Write-Output "Mismatch for $packageName → CLI: $cliWorkerVersion vs Host: $hostWorkerVersion"
            $failedValidation = $true
        }
    }
}
Write-Output "----------------------------------------------"

# Save if updated
if ($Update) {
    $cliCsprojXml.Save($cliCsprojPath)
    Write-Output "Updated worker versions! 🚀"
} elseif ($failedValidation) {
    Write-Output "You can run './validateWorkerVersions.ps1 -Update' to fix them."
    throw "Worker versions did not match. 😢"
} else {
    Write-Output "Worker versions match! 🥳"
}
