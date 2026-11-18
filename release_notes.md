# Azure Functions CLI 4.17.0

#### Host Version

- Host Runtime Version: 4.1055.300
- In-Proc CLI:
  - CLI Version: 4.9.0
  - Host Runtime Version: 4.52.200 (includes 4.852.200, 4.652.200)

#### Changes
- Fix `func extensions install --version <version>` being rejected with "Must specify extension package name and version": `--version` after the command is now passed to the action instead of being stripped as the global version flag. (#5926)
