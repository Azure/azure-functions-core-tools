.paket\paket.exe install

packages\FAKE\tools\fake .\build.fsx clean
packages\FAKE\tools\fake .\build.fsx sign platform=x86
packages\FAKE\tools\fake .\build.fsx sign platform=x64