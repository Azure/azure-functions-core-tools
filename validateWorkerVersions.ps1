<#
    .SYNOPSIS
        Used to validate and/or update worker package versions
    .EXAMPLE
        ./validateWorkerVersions.ps1

        Validates the workers match the existing host version and throws an error if they don't
    .EXAMPLE
        ./validateWorkerVersions.ps1 -Update -HostVersion 4.0.0

        Updates the host reference to 4.0.0 and the workers to their matching versions
#>
param (
    [Switch]$Update,
    
    # An explicit host version, otherwise the host version from Azure.Functions.Cli.csproj will be used
    [string]$hostVersion
)

# the xml will fail to parse if the data is encoded with a bom character
function removeBomIfExists([string]$data)
{
    if ($data.StartsWith(0xFEFF)) {
        $data = $data.substring(1)
    }
    return $data
}

$cliCsprojPath = "./src/Azure.Functions.Cli/Azure.Functions.Cli.csproj"
$cliCsprojContent = removeBomIfExists(Get-Content $cliCsprojPath)
$cliCsprojXml = [xml]$cliCsprojContent

function getPackageVersion([string]$packageName, [string]$csprojContent)
{
    $version = (Select-Xml -Content $csprojContent -XPath "/Project//PackageReference[@Include='$packageName']/@Version").ToString()
    if (-Not $version) {
        throw "Failed to find version for package $packageName"
    }
    return $version
}

function setCliPackageVersion([string]$packageName, [string]$version)
{
    $node = $cliCsprojXml.SelectSingleNode("/Project//PackageReference[@Include='$packageName']")
    $node.Version = $version
}

$hostPackageName = "Microsoft.Azure.WebJobs.Script.WebHost"
if (-Not $hostVersion) {
    $hostVersion = getPackageVersion $hostPackageName $cliCsprojContent
} elseif ($Update) {
    setCliPackageVersion $hostPackageName $hostVersion
}

$hostRepoUrlBase = "https://raw.githubusercontent.com/Azure/azure-functions-host/v$hostVersion"
$hostCsprojContent = removeBomIfExists((Invoke-WebRequest -Uri "$hostRepoUrlBase/src/WebJobs.Script/WebJobs.Script.csproj").Content)
$pythonPropsContent = removeBomIfExists((Invoke-WebRequest -Uri "$hostRepoUrlBase/build/python.props").Content)

$workers = "JavaWorker", "NodeJsWorker", "PowerShellWorker.PS7.0", "PowerShellWorker.PS7.2", "PythonWorker"

$failedValidation = $false
foreach($worker in $workers) {
    $packageName = "Microsoft.Azure.Functions.$worker"
    if ($worker -eq "PythonWorker") {
        $hostWorkerVersion = getPackageVersion $packageName $pythonPropsContent
    } else {
        $hostWorkerVersion = getPackageVersion $packageName $hostCsprojContent
    }
    $cliWorkerVersion = getPackageVersion $packageName $cliCsprojContent

    if ($Update) {
        setCliPackageVersion $packageName $hostWorkerVersion
    } elseif ($hostWorkerVersion -ne $cliWorkerVersion) {
        Write-Output "Reference to $worker in the host ($hostWorkerVersion) does not match version in the cli ($cliWorkerVersion)"
        $failedValidation = $true
    }
}

if ($Update) {
    $cliCsprojXml.Save($cliCsprojPath)
    Write-Output "Updated worker versions! 🚀"
} elseif ($failedValidation) {
    Write-Output "You can run './validateWorkerVersions.ps1 -Update' locally to fix worker versions."
    throw "Not all worker versions matched. 😢 See output for more info"
} else {
    Write-Output "Worker versions match! 🥳"
}
