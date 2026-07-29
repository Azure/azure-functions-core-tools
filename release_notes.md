# Azure Functions CLI 4.13.0

#### Host Version

- Host Runtime Version: 4.1051.300
- In-Proc CLI:
  - CLI Version: 4.7.0
  - Host Runtime Version: 4.51.100 (includes 4.851.100, 4.651.100)

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
- Fix Flex Health Check to use `defaultHostName` instead of `enabledHostNames` (#5462)
- Changed `func start` to bind to the IPv4 loopback address (`127.0.0.1`) by default instead of `0.0.0.0`, and added an opt-in `--address` flag (and `Host.LocalHttpAddress` setting in `local.settings.json`) to override the bind address (#5484)
  - **Potential breaking change:** the host now binds to `127.0.0.1` by default, so it is only reachable from the local machine. Setups that relied on binding to `0.0.0.0` — for example reaching the host from outside a Docker container via a published port — will no longer connect. Workaround: start the host with `func start --address 0.0.0.0` (or set `"LocalHttpAddress": "0.0.0.0"` under `Host` in `local.settings.json`) to restore the previous behavior.
