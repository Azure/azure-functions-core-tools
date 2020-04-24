Set-Location ".\build"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Unzip([string]$zipfilePath, [string]$outputpath) {
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zipfilePath, $outputpath)
        LogSuccess "Unzipped:$zipfilePath at $outputpath"
    }
    catch {
        LogErrorAndExit "Unzip failed for:$zipfilePath" $_.Exception
    }
}


function Zip([string]$directoryPath, [string]$zipPath) {
    try {
        LogSuccess "start zip:$directoryPath to $zipPath"

        [System.IO.Compression.ZipFile]::CreateFromDirectory($directoryPath, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false);
        LogSuccess "Zipped:$directoryPath to $zipPath"
    }
    catch {
        LogErrorAndExit "Zip operation failed for:$directoryPath" $_.Exception
    }
}


function LogErrorAndExit($errorMessage, $exception) {
    Write-Output $errorMessage
    if ($exception -ne $null) {
        Write-Output $exception|format-list -force
    }    
    Exit 1
}

function LogSuccess($message) {
    Write-Output `n
    Write-Output $message
}

try 
{
    $artifactsPath = Resolve-Path "..\artifacts\"
    $tempDirectoryPath = "..\artifacts\temp\"

    if (Test-Path $tempDirectoryPath)
    {
        Remove-Item $tempDirectoryPath -Force -Recurse
    }

    # Runtimes with signed binaries
    $runtimesIdentifiers = @("min.win-x86","min.win-x64")
    $tempDirectory = New-Item $tempDirectoryPath -ItemType Directory
    LogSuccess "$tempDirectoryPath created"

    # Unzip the coretools artifact to add signed binaries
    foreach($rid in $runtimesIdentifiers)
    {
        $files= Get-ChildItem -Recurse -Path "..\artifacts\*.zip"
        foreach($file in $files)
        {
            if ($file.Name.Contains($rid))
            {
                $fileName = [io.path]::GetFileNameWithoutExtension($file.Name)

                $targetDirectory = Join-Path $tempDirectoryPath $fileName
                $dir = New-Item $targetDirectory -ItemType Directory
                $targetDirectory = Resolve-Path $targetDirectory 
                $filePath = Resolve-Path $file.FullName
                Unzip $filePath $targetDirectory       
                   
                # Removing file after extraction
                Remove-Item $filePath
                LogSuccess "Removed $filePath"
            }
        }
    }

    # Store file count before replacing the binaries
    $fileCountBefore = (Get-ChildItem $tempDirectoryPath -Recurse | Measure-Object).Count

    # copy authenticode signed binaries into extracted directories
    $authenticodeDirectory = "..\artifacts\ToSign\Authenticode\"
    $authenticodeDirectories = Get-ChildItem $authenticodeDirectory -Directory

    foreach($directory in $authenticodeDirectories)
    {
        $sourcePath = $directory.FullName
        Copy-Item -Path $sourcePath -Destination $tempDirectoryPath -Recurse -Force
    }

    # copy thirdparty signed directory into extracted directories
    $thirdPathDirectory  = "..\artifacts\ToSign\ThirdParty\"
    $thirdPathDirectories  = Get-ChildItem $thirdPathDirectory -Directory

    foreach($directory in $thirdPathDirectories)
    {
        $sourcePath = $directory.FullName
        Copy-Item -Path $sourcePath -Destination $tempDirectoryPath -Recurse -Force
    }

    $fileCountAfter = (Get-ChildItem $tempDirectoryPath -Recurse | Measure-Object).Count

    if ($fileCountBefore -ne $fileCountAfter)
    {
        LogErrorAndExit "File count does not match. File count before copy: $fileCountBefore != file count after copy:$fileCountAfter" $_.Exception
    }

    $tempDirectories  = Get-ChildItem $tempDirectoryPath -Directory
    foreach($directory in $tempDirectories)
    {
       $directoryName = $directory.Name
       $zipPath = Join-Path $artifactsPath $directoryName
       $zipPath = $zipPath + ".zip"
       $directoryPath = $directory.FullName
       Zip $directoryPath $zipPath 
    }
    
}
catch {
    LogErrorAndExit "Execution Failed" $_.Exception
}