# Azure Functions CLI 4.12.0

#### Host Version

- Host Runtime Version: 4.1048.200
- In-Proc CLI:
  - CLI Version: 4.5.0
  - Host Runtime Version: 4.48.100 (includes 4.848.100, 4.648.100)

#### Changes

- Fix `HttpsProxyAgent is not a constructor` error in `install.js` when installing behind a proxy (https-proxy-agent v9 requires a named import).
- Remove `azure-functions-core-tools` from the `devDependencies` of generated function app templates (`package-js.json`, `package-js-v4.json`, `package-ts.json`, `package-ts-v4.json`). Users get the CLI from their system install; pulling it in again via `npm install` doubled disk usage and made every project's install fragile to Core Tools postinstall regressions.
- Add **preview** Go language support to `func init`, `func start`, `func pack`, and `func publish` (#4875, #4892, #4943). Behaviour, build/publish flags, and deployment layout may change before GA.
