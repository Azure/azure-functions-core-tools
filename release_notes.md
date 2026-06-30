# Azure Functions CLI 4.13.0

#### Host Version

- Host Runtime Version: 4.1051.300
- In-Proc CLI:
  - CLI Version: 4.6.0
  - Host Runtime Version: 4.49.100 (includes 4.849.100, 4.649.100)

#### Changes

- Removed warning log for remote build with Python 3.14 Flex apps, as remote build is now supported (#5375)
- Fixed npm postinstall silently swallowing extraction errors (#5281)
- Added lazy first-use install for npm RFC #868 compatibility (#5291)
- Added warning when key vault references fail to resolve (#5373)
- Replaced deprecated `url.parse` in npm installer (#5371)
- Enhanced func CLI static gitignore and streamlined Azurite entries (#5084)
- Bumped dotnet templates version to 4.0.5590 (#5271)
- Bumped https-proxy-agent dependency (#5335)
- Updated target framework to .NET 10 (#4850)
