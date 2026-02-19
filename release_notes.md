# Azure Functions CLI 4.8.0

#### Host Version

- Host Runtime Version: 4.1046.100
- In-Proc CLI:
  - CLI Version: 4.3.0
  - Host Runtime Version: 4.44.100 (includes 4.844.100, 4.644.100)

#### Breaking Changes

- **Python 3.7 and 3.8 are no longer supported.** These versions have reached end-of-life. Please upgrade to Python 3.9 or later to continue using Azure Functions Core Tools. (#4756)

#### Changes

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
