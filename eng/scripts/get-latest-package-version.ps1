function Get-LatestPackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$packageName,
        
        [Parameter(Mandatory = $true)]
        [hashtable]$headers
    )
  
    Write-Host "Fetching package: $packageName"
    
    $feedUrl = "https://feeds.dev.azure.com/azfunc/internal/_apis/packaging/feeds/core-tools-nightly-build/packages"
    $apiVersion = "7.1"

    $searchUrl = "$feedUrl?packageNameQuery=$packageName&api-version=$apiVersion"
    Write-Host "Search URL: $searchUrl"
    
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
    $package = $searchResponse.value | Where-Object { $_.name -eq $packageName } | Select-Object -First 1
    
    if (!$package) {
        Write-Warning "Package '$packageName' not found in the feed."
        return $null
    }
    
    $packageUrl = "$feedUrl/$($package.id)?api-version=$apiVersion"
    $packageDetails = Invoke-RestMethod -Uri $packageUrl -Headers $headers -Method Get
    return $packageDetails.versions[0].version
}
