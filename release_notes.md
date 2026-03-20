# Azure Functions CLI 4.10.0

#### Host Version

- Host Runtime Version: 4.1048.200
- In-Proc CLI:
  - CLI Version: 4.5.0
  - Host Runtime Version: 4.48.100 (includes 4.848.100, 4.648.100)

#### Changes

- Fix `func pack` throwing cryptic `Unsupported runtime: None` when `local.settings.json` is absent and `FUNCTIONS_WORKER_RUNTIME` is not set (#4829)
- Mark Node.js 24 as GA in stacks.json (add Flex Consumption SKU) (#4867)
- Surface SSL/TLS certificate errors clearly when SSL inspection proxies intercept connections (#4857)
- Fix `func kubernetes deploy` race condition where `kubectl rollout status` ran before the Deployment was registered (#4919)
- Fix `func kubernetes delete` to honor `--no-docker` instead of failing on registry auth (#4919)
- Add `McpPromptTrigger` template for dotnet-isolated `func new` (#4891)
- Fix func azure storage fetch-connection-string failing with "Cannot find storage account" due to ARM eventual consistency (#4884)
- Fixed `func pack --python` stripping `.dist-info` directories from packaged dependencies (#4853)
- Fix `AzureFunctionsJobHost__logging__logLevel__Function` override from `local.settings.json` being ignored due to the host pre-setting the environment variable before user configuration was loaded (#4815)
- Fix `ArgumentNullException: Value cannot be null. (Parameter 'input')` during `func azure functionapp publish` for non-.NET runtimes (PowerShell, Node.js, Python, Java) on Windows function apps (#4822)
- Add support for PowerShell 7.6