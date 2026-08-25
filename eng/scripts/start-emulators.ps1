param(
    [Parameter(Mandatory=$false)]
    [Switch]
    $SkipStorageEmulator,
    [Parameter(Mandatory=$false)]
    [Switch]
    $NoWait
)

$DebugPreference = 'Continue'
$npmConfigPath = Join-Path $PSScriptRoot '..\..\.npmrc'

Write-Host "Skip Storage Emulator: $SkipStorageEmulator"

$startedStorage = $false

if (!$IsWindows -and !$IsLinux -and !$IsMacOs)
{
    # For pre-PS6
    Write-Host "Could not resolve OS. Assuming Windows."
    $assumeWindows = $true
}

function IsStorageEmulatorRunning()
{
    try
    {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000/"
        $StatusCode = $Response.StatusCode
    }
    catch
    {
        $StatusCode = $_.Exception.Response.StatusCode.value__
    }

    if ($StatusCode -eq 400)
    {
        return $true
    }

    return $false
}

if (!$SkipStorageEmulator)
{
    Write-Host "------"
    Write-Host ""
    Write-Host "---Starting Storage emulator---"
    $storageEmulatorRunning = IsStorageEmulatorRunning

    if ($storageEmulatorRunning -eq $false)
    {
        if ($IsWindows -or $assumeWindows)
        {
            npm install -g azurite --userconfig $npmConfigPath
            Start-Process azurite.cmd -ArgumentList "--silent --skipApiVersionCheck"
        }
        else
        {
            sudo npm install -g azurite --userconfig $npmConfigPath
            sudo mkdir -p azurite
            Start-Process -FilePath "sudo" -ArgumentList "azurite","--silent","--skipApiVersionCheck","--location","azurite","--debug","azurite/debug.log"
        }

        $startedStorage = $true
    }
    else
    {
        Write-Host "Storage emulator is already running."
    }

    Write-Host "------"
    Write-Host
}

if ($NoWait -eq $true)
{
    Write-Host "'NoWait' specified. Exiting."
    Write-Host
    exit 0
}

if (!$SkipStorageEmulator -and $startedStorage -eq $true)
{
    Write-Host "---Waiting for Storage emulator to be running---"
    $maxRetries = 24  # 24 * 5s = 2 minutes
    $retryCount = 0
    $storageEmulatorRunning = IsStorageEmulatorRunning
    while ($storageEmulatorRunning -eq $false)
    {
        $retryCount++
        if ($retryCount -ge $maxRetries)
        {
            Write-Error "Storage emulator failed to start within 2 minutes."
            exit 1
        }
        Write-Host "Storage emulator not ready. Attempt $retryCount/$maxRetries"
        Start-Sleep -Seconds 5
        $storageEmulatorRunning = IsStorageEmulatorRunning
    }
    Write-Host "Storage emulator ready."
    Write-Host "------"
    Write-Host
}