# Azure Functions CLI 4.11.0

#### Host Version

- Host Runtime Version: 4.1049.200
- In-Proc CLI:
  - CLI Version: 4.4.0
  - Host Runtime Version: 4.46.100 (includes 4.846.100, 4.646.100)

#### Changes

- Updated target framework to .NET 10
    - Migrated from deprecated `Microsoft.DotNet.PlatformAbstractions` to `System.Runtime.InteropServices.RuntimeInformation`
    - Migrated from deprecated `X509Certificate2` constructor to `X509CertificateLoader`
    - Bumped `Microsoft.Extensions.DependencyInjection` to 10.0.0
    - Bumped `Microsoft.Extensions.Logging` / `Logging.Abstractions` to 10.0.0 / 10.0.3
    - Bumped `Azure.Identity` to 1.20.0, `Azure.Security.KeyVault.Secrets` to 4.9.0
    - Bumped `Microsoft.Identity.Client` to 4.83.3
    - Bumped `Newtonsoft.Json` to 13.0.4, `WindowsAzure.Storage` to 9.3.3
    - Removed unnecessary transitive pinning of `System.Text.Json`, `System.Formats.Asn1`, `System.Private.Uri`
    - Updated worker versions to match host requirements (NodeJs 3.13.0, Python 4.44.0)

