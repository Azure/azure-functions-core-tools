# Azure Functions CLI

#### Host Version

#### Changes
- Fix `func extensions install --version <version>` being rejected with "Must specify extension package name and version": `--version` after the command is now passed to the action instead of being stripped as the global version flag. (#5926)
- Fixed `func kubernetes deploy` not completing for LoadBalancer services whose ingress reports only a hostname (such as Docker Desktop). The command now falls back to the ingress hostname when no IP is set, and keeps polling until an ingress entry has an address. (#3215)
