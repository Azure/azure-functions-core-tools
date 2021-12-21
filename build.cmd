.paket\paket.exe install

cd "C:\" && dir "AxImp.exe" /s
dir "AxImp.exe" /s

packages\FAKE\tools\fake .\build.fsx clean platform=x86
packages\FAKE\tools\fake .\build.fsx clean platform=x64

packages\FAKE\tools\fake .\build.fsx platform=x86 -ev sign
packages\FAKE\tools\fake .\build.fsx platform=x64 -ev sign