# Azure Functions CLI

#### Host Version

- Host Runtime Version: 4.1055.300
- In-Proc CLI:
  - CLI Version: 4.9.0
  - Host Runtime Version: 4.52.200 (includes 4.852.200, 4.652.200)

#### Breaking Changes

- **Python 3.9 is no longer supported.** This version has reached end-of-life. Please upgrade to Python 3.10 or later to continue using Azure Functions Core Tools.

#### Changes
- Fix `func extensions install --version <version>` being rejected with "Must specify extension package name and version": `--version` after the command is now passed to the action instead of being stripped as the global version flag. (#5926)
- Fixed `func kubernetes deploy` not completing for LoadBalancer services whose ingress reports only a hostname (such as Docker Desktop). The command now falls back to the ingress hostname when no IP is set, and keeps polling until an ingress entry has an address. (#3215)
