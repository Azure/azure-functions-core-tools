.paket\paket.exe install

dir "AxImp.exe" -Path C:\ -Recurse

packages\FAKE\tools\fake .\build.fsx clean platform=x86
packages\FAKE\tools\fake .\build.fsx clean platform=x64

packages\FAKE\tools\fake .\build.fsx platform=x86 -ev sign
packages\FAKE\tools\fake .\build.fsx platform=x64 -ev sign