# Fetch the latest version of the func-cli package from the feed for each package
$headers = @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" }

function Get-LatestPackageVersion {
    param($packageName)
    
    $baseUrl = "https://feeds.dev.azure.com/azfunc/internal/_apis/packaging/feeds/core-tools-nightly-build"
    
    $searchUrl = "$baseUrl/packages?packageNameQuery=$packageName&api-version=7.1"
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
    $package = $searchResponse.value | Where-Object { $_.name -eq $packageName } | Select-Object -First 1
    
    if ($package) {
        $packageUrl = "$baseUrl/packages/$($package.id)?api-version=7.1"
        $packageDetails = Invoke-RestMethod -Uri $packageUrl -Headers $headers -Method Get
        return $packageDetails.versions[0].version
    }
    return $null
}

# Get version for each package
$funcCliVersion = Get-LatestPackageVersion -packageName "func-cli"
Write-Host "##vso[task.setvariable variable=FUNC_CLI_VERSION]$funcCliVersion"
Write-Host "func-cli version: $funcCliVersion"

$funcCliInprocVersion = Get-LatestPackageVersion -packageName "func-cli-inproc"
Write-Host "##vso[task.setvariable variable=FUNC_CLI_INPROC_VERSION]$funcCliInprocVersion"
Write-Host "func-cli-inproc version: $funcCliInprocVersion"
