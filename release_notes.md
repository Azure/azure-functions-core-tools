# Azure Functions CLI 4.11.0 //todo: validate this is the right next version

#### Host Version

- Host Runtime Version: 4.1049.100
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
- Add support for PowerShell 7.6
- Updated target framework to .NET 10
- Migrated from deprecated `Microsoft.DotNet.PlatformAbstractions` to `System.Runtime.InteropServices.RuntimeInformation`
- Migrated from `IWebHost` to `IHost` in `StartHostAction`
- Migrated from deprecated `X509Certificate2` constructor to `X509CertificateLoader`
- Bumped `Microsoft.Extensions.DependencyInjection` to 10.0.0
- Bumped `Microsoft.Extensions.Logging` / `Logging.Abstractions` to 10.0.0 / 10.0.3
- Bumped `Azure.Identity` to 1.20.0, `Azure.Security.KeyVault.Secrets` to 4.9.0
- Bumped `Microsoft.Identity.Client` to 4.83.3
- Bumped `Newtonsoft.Json` to 13.0.4, `WindowsAzure.Storage` to 9.3.3
- Removed unnecessary transitive pinning of `System.Text.Json`, `System.Formats.Asn1`, `System.Private.Uri`
- Updated worker versions to match host requirements (NodeJs 3.13.0, PS7.4 4.0.4759, Python 4.44.0)

