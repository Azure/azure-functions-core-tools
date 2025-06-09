<#
    .SYNOPSIS
        Used to validate and/or update worker package versions
    .EXAMPLE
        ./validate-worker-versions.ps1

        Validates the workers match the existing host version and throws an error if they don't
    .EXAMPLE
        ./validate-worker-versions.ps1 -Update -HostVersion 4.1037.0

        Updates the host reference to 4.1037.0 and the workers to their matching versions
#>
param (
    [Switch]$Update,

    # An explicit host version, otherwise the host version from Directory.Packages.props will be used
    [string]$hostVersion
)

# the xml will fail to parse if the data is encoded with a bom character
function removeBomIfExists([string]$data)
{
    if ($data.StartsWith(0xFEFF)) {
        $data = $data.Substring(1)
    }
    return $data
}

$rootDir = Join-Path $PSScriptRoot "../.." | Resolve-Path
$packagesPropsPath = "$rootDir/eng/Directory.Packages.props"
$packagesPropsContent = removeBomIfExists(Get-Content $packagesPropsPath)
$packagesPropsXml = [xml]$packagesPropsContent

function getPackageVersion([string]$packageName, [xml]$propsXml, [bool]$isPackageReference = $false)
{
    if ($isPackageReference) {
        $xpath =  "/Project/ItemGroup/PackageReference[@Include='$packageName']"
    } else {
        $xpath = "/Project/ItemGroup/PackageVersion[@Include='$packageName']"
    }

    $node = Select-Xml -Xml $propsXml -XPath $xpath | Select-Object -ExpandProperty Node
    if ($node) {
        return $node.Version
    } else {
        throw "Failed to find version for package $packageName in Directory.Packages.props"
    }
}

function setPackageVersionInProps([string]$packageName, [string]$newVersion)
{
    $node = Select-Xml -Xml $packagesPropsXml -XPath "/Project/ItemGroup/PackageVersion[@Include='$packageName']" | Select-Object -ExpandProperty Node
    if (-Not $node) {
        throw "Failed to find reference for package $packageName in Directory.Packages.props"
    }
    $oldVersion = $node.Version
    $node.Version = $newVersion
    Write-Output "Updated $packageName from $oldVersion to $newVersion"
}

$hostPackageName = "Microsoft.Azure.WebJobs.Script.WebHost"
if (-Not $hostVersion) {
    $hostVersion = getPackageVersion $hostPackageName $packagesPropsXml
} elseif ($Update) {
    setPackageVersionInProps $hostPackageName $hostVersion
}

$tagUri = "https://api.github.com/repos/Azure/azure-functions-host/git/refs/tags/v$hostVersion"
$result = Invoke-WebRequest -Uri $tagUri
if ($result.StatusCode -ne 200) {
    throw "Host tag version $hostVersion does not exist, check that the host version provide is a real tag in the Host repo. Note: new host versions may take a different format such as 4.1038.100"
}

Write-Output "Host version: $hostVersion"

function getWorkerPropsFileFromHost([string]$filePath) {
    $uri = "https://raw.githubusercontent.com/Azure/azure-functions-host/refs/tags/v$hostVersion/$filePath"
    $content = removeBomIfExists((Invoke-WebRequest -Uri $uri).Content)
    return [xml]$content
}

$workerPropsToWorkerName = @{
    "eng/build/Workers.Node.props"      = @("NodeJsWorker")
    "eng/build/Workers.Java.props"      = @("JavaWorker")
    "eng/build/Workers.Python.props"    = @("PythonWorker")
    "eng/build/Workers.Powershell.props" = @("PowerShellWorker.PS7.0", "PowerShellWorker.PS7.2", "PowerShellWorker.PS7.4")
}

$failedValidation = $false

# Iterate through each worker and validate versions
foreach ($key in $workerPropsToWorkerName.Keys) {
    Write-Output "----------------------------------------------"
    $workerPropsContent = getWorkerPropsFileFromHost $key
    # Get the list associated with the key
    $workerList = $workerPropsToWorkerName[$key]

    foreach ($worker in $workerList) {
        Write-Output "Validating $worker version..."
        $packageName = "Microsoft.Azure.Functions.$worker"

        # Get versions from the host and our repo (from Directory.Packages.props)
        $hostWorkerVersion = getPackageVersion $packageName $workerPropsContent $true
        $cliWorkerVersion = getPackageVersion $packageName $packagesPropsXml

        Write-Output "CLI version: $cliWorkerVersion | Host version: $hostWorkerVersion"

        if ($Update -AND $hostWorkerVersion -ne $cliWorkerVersion) {
            setPackageVersionInProps $packageName $hostWorkerVersion
        } elseif ($hostWorkerVersion -ne $cliWorkerVersion) {
            Write-Output "Reference to $worker in the host ($hostWorkerVersion) does not match version in the CLI ($cliWorkerVersion)"
            $failedValidation = $true
        }
    }
}
Write-Output "----------------------------------------------"

# Save updated versions if necessary
if ($Update) {
    $packagesPropsXml.Save($packagesPropsPath)
    Write-Output "Updated worker versions! 🚀"
} elseif ($failedValidation) {
    Write-Output "You can run './validate-worker-versions.ps1 -Update' locally to fix worker versions."
    throw "Not all worker versions matched. 😢 See output for more info"
} else {
    Write-Output "Worker versions match! 🥳"
}
