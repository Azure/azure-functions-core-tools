# Azure Functions CLI 4.7.0

#### Host Version

- Host Runtime Version: 4.1045.200
- In-Proc CLI:
  - CLI Version: 4.3.0
  - Host Runtime Version: 4.44.100 (includes 4.844.100, 4.644.100)

#### Changes

- Fix .gitignore to allow PowerShell module bin folders (#4574)
- Refactor to use msbuild for determining .NET target framework & add support multiple TFMs (#4715)
  - When using `func init --docker-only` on a .NET project with multiple target frameworks, the CLI will now
   prompt the user to select which target framework to use for the Dockerfile.
- Enhanced dotnet installation discovery by adopting the same `Muxer` logic used by the .NET SDK itself (#4732)
- Update .NET templates package version to 4.0.5337 (#4728)
- Fix `func pack --build-native-deps` failure on Windows for Python 3.13+ (#4742)
- Cleaned up `func --help` output and fixed validation errors when using the `--help` flag for specific commands (#4748)
- Improved `func init --help` output to better display options for each worker runtime (#4748)
- Fix F# project & template initialization via `func init | new` (#4749)
- [Breaking] Remove support for Python 3.7 & 3.8 (#4756)
- Added end-of-life warnings for all runtime versions during `func azure functionapp publish`. (#4760)
- Reduced console output noise by moving informational messages to verbose logging. (#4768)
- Fixed an issue where creating an MCP Tool trigger function would fail with "Unknown template 'McpToolTrigger'" error. (#4768)
- Added new `func bundles` commands for managing extension bundles (#4769)
  - `func bundles download` - Download the extension bundle configured in host.json with optional `--force` flag to re-download
  - `func bundles list` - List all downloaded extension bundles
  - `func bundles path` - Get the path to the downloaded extension bundle
  - `func bundles add` - Add extension bundle configuration to host.json with `--channel` flag to select GA (default), Preview, or Experimental bundles
  - Support for custom bundle download paths via `AzureFunctionsJobHost__extensionBundle__downloadPath` environment variable
- Added `--bundles-channel` option to `func init` command to specify extension bundle channel (GA, Preview, or Experimental) during project initialization
