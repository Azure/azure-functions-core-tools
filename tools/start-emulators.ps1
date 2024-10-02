param(
    [Parameter(Mandatory=$false)]
    [Switch]
    $SkipStorageEmulator,
    [Parameter(Mandatory=$false)]
    [Switch]
    $NoWait
)

$DebugPreference = 'Continue'

Write-Host "Skip Storage Emulator: $SkipStorageEmulator"

$startedStorage = $false

if (!$IsWindows -and !$IsLinux -and !$IsMacOs)
{
    # For pre-PS6
    Write-Host "Could not resolve OS. Assuming Windows."
    $IsWindows = $true
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
        if ($IsWindows)
        {
            npm install -g azurite
            Start-Process azurite.cmd -ArgumentList "--silent --skipApiVersionCheck"
        }
        else
        {
            sudo npm install -g azurite
            sudo mkdir azurite
            sudo azurite --silent --skipApiVersionCheck --location azurite --debug azurite\debug.log &
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
    $storageEmulatorRunning = IsStorageEmulatorRunning
    while ($storageEmulatorRunning -eq $false)
    {
        Write-Host "Storage emulator not ready."
        Start-Sleep -Seconds 5
        $storageEmulatorRunning = IsStorageEmulatorRunning
    }
    Write-Host "Storage emulator ready."
    Write-Host "------"
    Write-Host
}