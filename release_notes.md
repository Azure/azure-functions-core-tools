# Azure Functions CLI 4.11.1

#### Host Version

- Host Runtime Version: 4.1048.200
- In-Proc CLI:
  - CLI Version: 4.5.0
  - Host Runtime Version: 4.48.100 (includes 4.848.100, 4.648.100)

#### Changes

- Fix `HttpsProxyAgent is not a constructor` error in `install.js` when installing behind a proxy (https-proxy-agent v9 requires a named import).
