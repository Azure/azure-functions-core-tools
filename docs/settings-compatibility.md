# Local settings compatibility

Azure Functions CLI v5 does not decrypt encrypted `local.settings.json` values.
An explicit `IsEncrypted: true` marker (including the string `"true"`) identifies
encrypted application values. The CLI retains their setting names, not their
contents, in its local-settings snapshot.

- Plaintext values (`IsEncrypted: false`) and the existing missing-marker
  behavior are unchanged.
- Encrypted values cannot supply a worker-runtime fallback or be projected into
  CLI project metadata or ordinary Host application configuration.
- Reading configuration does not fail just because an encrypted file exists.
  Other safe configuration sources remain available.
- Before project preparation, startup fails if an encrypted value would still
  be needed. A process-environment override, including an empty string, suppresses
  that local value as before. Resolved worker hints and existing Host, bundle,
  and matching-stack environment overrides also avoid consuming the local value.
- Guaranteed later replacements also make an encrypted local value unnecessary:
  the real Host launcher supplies `AzureWebJobsScriptRoot`,
  `AZURE_FUNCTIONS_ENVIRONMENT`, `WEBSITE_HOSTNAME`, and
  `Logging__LogLevel__Microsoft.Hosting.Lifetime`. Successful Python project
  preparation supplies `PYTHON_ISOLATE_WORKER_DEPENDENCIES` and
  `languageWorkers__python__defaultExecutablePath`. The Python guarantees apply
  only to the resolved Python project, and launcher guarantees do not apply to
  demo mode, which does not launch the Host.
- Conditional defaults or best-effort replacements are not exempt:
  `ASPNETCORE_SUPPRESSSTATUSMESSAGES` and `FUNCTIONS_WORKER_RUNTIME_VERSION`
  still require a safe resolution. Any other unresolved encrypted application
  setting still fails before startup notices or preparation.
- The separate `Host` metadata projection is unchanged.

The startup diagnostic is fixed and does not include setting names or values:

> Encrypted local.settings.json values are not supported by this Azure Functions CLI path. Use a supported decryption or migration workflow to provide plaintext local settings, or override the settings through the process environment.

Use an independently supported decryption or migration workflow to prepare
plaintext local settings, or supply the settings through the process environment.
This guard does not decrypt, migrate, or rewrite local settings, access key
stores, or write project configuration. Encryption management and broader
settings compatibility remain separate work.
