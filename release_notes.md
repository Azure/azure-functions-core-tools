# Azure Functions CLI 4.10.0

#### Host Version

- Host Runtime Version: 4.1048.200
- In-Proc CLI:
  - CLI Version: 4.4.0
  - Host Runtime Version: 4.46.100 (includes 4.846.100, 4.646.100)

#### Changes

- Fixed `func pack --python` stripping `.dist-info` directories from packaged dependencies (#4853)
- Add support for PowerShell 7.6
- Update PowerShell 7.4 worker to 4.0.4759
- Update Node.js worker to 3.13.0
- Update Python worker to 4.43.0
- Skip `NuGet.Packaging` from CVE scan: pinned to 5.11.6 transitively by the Functions host. Cannot upgrade independently until the host moves to NuGet 6.x. Advisory: GHSA-g4vj-cjjj-v7hg (Low).
