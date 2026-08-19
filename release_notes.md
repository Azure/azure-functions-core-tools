# Azure Functions CLI 4.14.0

#### Host Version

- Host Runtime Version: 4.1052.200
- In-Proc CLI:
  - CLI Version: 4.7.0
  - Host Runtime Version: 4.51.100 (includes 4.851.100, 4.651.100)

#### Changes

- Added `durable-functions` dependency to `package.json` when creating Node.js durable function templates (#5495)
- Changed `func start` to bind to the IPv4 loopback address (`127.0.0.1`) by default instead of `0.0.0.0`, and added an opt-in `--address` flag (and `Host.LocalHttpAddress` setting in `local.settings.json`) to override the bind address (#5484)
  - **Potential breaking change:** the host now binds to `127.0.0.1` by default, so it is only reachable from the local machine. Setups that relied on binding to `0.0.0.0` — for example reaching the host from outside a Docker container via a published port — will no longer connect. Workaround: start the host with `func start --address 0.0.0.0` (or set `"LocalHttpAddress": "0.0.0.0"` under `Host` in `local.settings.json`) to restore the previous behavior.
