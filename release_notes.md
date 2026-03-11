# Azure Functions CLI 4.8.0

#### Host Version

- Host Runtime Version: 4.1046.100
- In-Proc CLI:
  - CLI Version: 4.4.0
  - Host Runtime Version: 4.46.100 (includes 4.846.100, 4.646.100)

#### Changes

- Fix `AzureFunctionsJobHost__logging__logLevel__Function` override from `local.settings.json` being ignored due to the host pre-setting the environment variable before user configuration was loaded (#4815)
- Fix `ArgumentNullException: Value cannot be null. (Parameter 'input')` during `func azure functionapp publish` for non-.NET runtimes (PowerShell, Node.js, Python, Java) on Windows function apps (#4822)
