param(
    [Parameter(Mandatory=$false)]
    [Switch]
    $SkipStorageEmulator,
    [Parameter(Mandatory=$false)]
    [Switch]
    $NoWait
)

$DebugPreference = 'Continue'
$ErrorActionPreference = 'Stop'

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
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000/" -TimeoutSec 5
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

function Install-Azurite()
{
    Write-Host "Installing Azurite..."
    
    # Try multiple times with different registries
    $registries = @(
        "https://registry.npmjs.org/"
    )
    
    foreach ($registry in $registries)
    {
        try
        {
            Write-Host "Trying registry: $registry"
            if ($IsWindows -or $assumeWindows)
            {
                $result = npm install -g azurite --registry $registry 2>&1
            }
            else
            {
                $result = sudo npm install -g azurite --registry $registry 2>&1
            }
            
            Write-Host "npm install result: $result"
            
            # Check if azurite was installed successfully
            if ($IsWindows -or $assumeWindows)
            {
                $azuriteCmd = Get-Command azurite.cmd -ErrorAction SilentlyContinue
                if ($azuriteCmd)
                {
                    Write-Host "Azurite installed successfully at: $($azuriteCmd.Source)"
                    return $true
                }
            }
            else
            {
                $azuriteCmd = Get-Command azurite -ErrorAction SilentlyContinue
                if ($azuriteCmd)
                {
                    Write-Host "Azurite installed successfully at: $($azuriteCmd.Source)"
                    return $true
                }
            }
        }
        catch
        {
            Write-Host "Failed with registry $registry : $_"
            continue
        }
    }
    
    Write-Host "Failed to install Azurite with any registry"
    return $false
}

function Start-AzuriteProcess()
{
    try
    {
        if ($IsWindows -or $assumeWindows)
        {
            # Check if azurite.cmd exists
            $azuriteCmd = Get-Command azurite.cmd -ErrorAction SilentlyContinue
            if (!$azuriteCmd)
            {
                Write-Host "azurite.cmd not found. Attempting to install..."
                $installSuccess = Install-Azurite
                if (!$installSuccess)
                {
                    throw "Failed to install Azurite"
                }
                $azuriteCmd = Get-Command azurite.cmd -ErrorAction SilentlyContinue
                if (!$azuriteCmd)
                {
                    throw "azurite.cmd still not found after installation"
                }
            }
            
            Write-Host "Starting Azurite with command: $($azuriteCmd.Source)"
            Start-Process $azuriteCmd.Source -ArgumentList "--silent --skipApiVersionCheck" -WindowStyle Hidden
        }
        else
        {
            # Linux/Mac
            $azuriteCmd = Get-Command azurite -ErrorAction SilentlyContinue
            if (!$azuriteCmd)
            {
                Write-Host "azurite not found. Attempting to install..."
                $installSuccess = Install-Azurite
                if (!$installSuccess)
                {
                    throw "Failed to install Azurite"
                }
            }
            
            sudo mkdir -p azurite
            sudo azurite --silent --skipApiVersionCheck --location azurite --debug azurite/debug.log &
        }
        
        return $true
    }
    catch
    {
        Write-Host "Failed to start Azurite: $_"
        return $false
    }
}

if (!$SkipStorageEmulator)
{
    Write-Host "------"
    Write-Host ""
    Write-Host "---Starting Storage emulator---"
    $storageEmulatorRunning = IsStorageEmulatorRunning

    if ($storageEmulatorRunning -eq $false)
    {
        $azuriteStarted = Start-AzuriteProcess
        if ($azuriteStarted)
        {
            $startedStorage = $true
            Write-Host "Azurite start command executed successfully"
        }
        else
        {
            Write-Host "Failed to start Azurite. Continuing without storage emulator..."
            Write-Host "Tests may fail if they require storage emulation."
        }
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
    $maxWaitTime = 60 # seconds
    $waitTime = 0
    $storageEmulatorRunning = IsStorageEmulatorRunning
    
    while ($storageEmulatorRunning -eq $false -and $waitTime -lt $maxWaitTime)
    {
        Write-Host "Storage emulator not ready. Waiting... ($waitTime/$maxWaitTime seconds)"
        Start-Sleep -Seconds 5
        $waitTime += 5
        $storageEmulatorRunning = IsStorageEmulatorRunning
    }
    
    if ($storageEmulatorRunning)
    {
        Write-Host "Storage emulator ready."
    }
    else
    {
        Write-Host "Storage emulator did not start within $maxWaitTime seconds."
        Write-Host "Continuing anyway - tests may fail if they require storage."
    }
    Write-Host "------"
    Write-Host
}