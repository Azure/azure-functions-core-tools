# Azure Functions CLI 4.9.0

#### Host Version

- Host Runtime Version: 4.1047.100
- In-Proc CLI:
  - CLI Version: 4.4.0
  - Host Runtime Version: 4.46.100 (includes 4.846.100, 4.646.100)

#### Changes

- Fixed `func pack --python` stripping `.dist-info` directories from packaged dependencies (#4853)
- Fix `AzureFunctionsJobHost__logging__logLevel__Function` override from `local.settings.json` being ignored due to the host pre-setting the environment variable before user configuration was loaded (#4815)
- Fix `ArgumentNullException: Value cannot be null. (Parameter 'input')` during `func azure functionapp publish` for non-.NET runtimes (PowerShell, Node.js, Python, Java) on Windows function apps (#4822)
- Fix `func pack` throwing cryptic `Unsupported runtime: None` when `local.settings.json` is absent and `FUNCTIONS_WORKER_RUNTIME` is not set. The command now emits a clear, actionable error. The existing `.dll`-based fallback for `--no-build` is preserved but scoped to top-level files only to avoid false positives (#4829)
