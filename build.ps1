.paket\paket.exe install

packages\FAKE\tools\fake .\build.fsx clean platform=x86
packages\FAKE\tools\fake .\build.fsx clean platform=x64

$isReleaseBuild = $null
$simulateReleaseBuild = $null
if (-not([bool]::TryParse($env:IsReleaseBuild, [ref] $isReleaseBuild) -and
    [bool]::TryParse($env:SimulateReleaseBuild, [ref] $simulateReleaseBuild)))
{
    throw "IsReleaseBuild and SimulateReleaseBuild can only be set to true or false."
}

if ($isReleaseBuild -or $simulateReleaseBuild)
{
    packages\FAKE\tools\fake .\build.fsx platform=x86 -ev sign -ev generateSBOM
    packages\FAKE\tools\fake .\build.fsx platform=x64 -ev sign -ev generateSBOM
}
else
{
    packages\FAKE\tools\fake .\build.fsx platform=x86 -ev sign
    packages\FAKE\tools\fake .\build.fsx platform=x64 -ev sign
}