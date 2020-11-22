param (
    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $StorageAccountName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $StorageAccountKey,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $SourcePath
)

function WriteLog
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Message,

        [Switch]
        $Throw
    )

    $Message = (Get-Date -Format G)  + " -- $Message"

    if ($Throw)
    {
        throw $Message
    }

    Write-Host $Message
}

WriteLog -Message "Script started."

$CONTAINER_NAME = "builds"
$FUNC_RUNTIME_VERSION = '3'

if (-not (Test-Path $SourcePath))
{
    throw "SourcePath '$SourcePath' does not exist."
}

WriteLog "Validating source path '$SourcePath'."
$filesToUploaded = @(Get-ChildItem -Path "$SourcePath/*.zip" | ForEach-Object {$_.FullName})
if ($filesToUploaded.Count -eq 0)
{
    WriteLog -Message "'$SourcePath' does not contain any zip files to upload." -Throw
}

if (-not (Get-command New-AzStorageContext -ea SilentlyContinue))
{
    WriteLog "Installing Az.Storage."
    Install-Module Az.Storage -Force -Verbose -AllowClobber -Scope CurrentUser
}

$context = $null
try
{
    WriteLog "Connecting to storage account..."
    $context = New-AzStorageContext -StorageAccountName $StorageAccountName -StorageAccountKey $StorageAccountKey -ErrorAction Stop
}
catch
{
    $message = "Failed to authenticate with Azure. Please verify the StorageAccountName and StorageAccountKey. Exception information: $_"
    WriteLog -Message $message -Throw
}

# Validate and read manifest file
$manifestFileName = "integrationTestsBuildManifest.json"
$manifestFilePath = Join-Path $SourcePath $manifestFileName

if (-not (Test-Path $manifestFilePath))
{
    WriteLog -Message "File '$manifestFilePath' does not exist." -Throw
}

$manifest = $null
try
{
    WriteLog -Message "Reading $manifestFileName."
    $manifest = Get-Content $manifestFilePath -Raw | ConvertFrom-Json -ErrorAction Stop
    $filesToUploaded += $manifestFilePath
}
catch
{
    WriteLog -Message "Failed to parse '$manifestFilePath'. Please make sure the file content is a valid JSON." -Throw
}

# Create a version.txt file from the integrationTestBuildManifest.json and add it to the list of files to upload
WriteLog -Message "Creating version.txt file..."
$version = $manifest.CoreToolsVersion
$versionFilePath = Join-Path $SourcePath "version.txt"
$version | Set-Content -Path $versionFilePath
$filesToUploaded += $versionFilePath

# These are the destination paths in the storage account
# "https://<storageAccountName>.blob.core.windows.net/builds/$FUNC_RUNTIME_VERSION/latest/Azure.Functions.Cli.$os-$arch.zip"
# "https://<storageAccountName>.blob.core.windows.net/builds/$FUNC_RUNTIME_VERSION/$version/Azure.Functions.Cli.$os-$arch.zip"
$latestDestinationPath = "$FUNC_RUNTIME_VERSION/latest"
$versionDestinationPath = "$FUNC_RUNTIME_VERSION/$($version)"

# Delete the files in the latest folder if it is not empty
$filesToDelete = @(Get-AzStorageBlob -Container $CONTAINER_NAME -Context $context -ErrorAction SilentlyContinue | Where-Object {$_.Name -like "*$latestDestinationPath*" })
if ($filesToDelete.Count -gt 0)
{
    WriteLog -Message "Deleting files in the latest folder...."
    $filesToDelete | ForEach-Object {
        Remove-AzStorageBlob -Container $CONTAINER_NAME  -Context $context -Blob $_.Name -Force -ErrorAction SilentlyContinue | Out-Null
    }
}

foreach ($path in @($latestDestinationPath, $versionDestinationPath))
{
    foreach ($file in $filesToUploaded)
    {
        $fileName = Split-Path $file -Leaf
        $destinationPath = Join-Path $path $fileName

        if ($destinationPath -like "*latest*")
        {
            # Remove the Core Tools version from the path for latest
            $destinationPath = $destinationPath.Replace("." + $version, "")
        }

        try
        {
            WriteLog -Message "Uploading '$fileName' to '$destinationPath'."

            Set-AzStorageBlobContent -File $file `
                                     -Container $CONTAINER_NAME `
                                     -Blob $destinationPath `
                                     -Context $context `
                                     -StandardBlobTier Hot `
                                     -ErrorAction Stop `
                                     -Force | Out-Null
        }
        catch
        {
            WriteLog -Message "Failed to upload file '$file' to storage account. Exception information: $_" -Throw
        }
    }
}

WriteLog -Message "Script completed."
