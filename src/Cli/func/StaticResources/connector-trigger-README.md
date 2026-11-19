# {FunctionName} - {ConnectorDisplayName} connector trigger ({Language})

This function was created from the `{TemplateName}` Core Tools template. It uses a
polling connector trigger from `@azure/functions-extensions-connectors`.

For full local testing and deployment instructions, visit
[aka.ms/connectortrigger](https://aka.ms/connectortrigger).

## Files

| File | Purpose |
| --- | --- |
| `src/functions/{FileName}` | The connector trigger function. |
| `local.settings.json` | Local app settings. Core Tools added placeholder values for the settings this trigger needs. |
| `{FunctionName}.README.md` | This file. |

## Required settings

Replace each placeholder in `local.settings.json` (under `Values`) before you run the function:

| Setting | Description |
| --- | --- |
{SettingsTable}

## 1. Install dependencies

```bash
npm install "@azure/functions@^4.17.1" "@azure/functions-extensions-connectors@0.2.0-preview" "@azure/connectors@0.4.0-preview"
```
{TypeScriptNote}
## 2. Create the connector resources in Azure

1. Create a **connector namespace**, then create a **{ConnectorDisplayName}** connection in it and sign in.
2. In the connection, create a **trigger config** for the trigger operation, with `deliveryMode` set to `Poll`.
3. Give the identity that runs the function (your developer identity when you run locally) access to the connector namespace through its access policies.
4. Copy the connector namespace endpoint into `ConnectorNamespace__pollingEndpoint`, and the trigger config name into the trigger config setting.

## 3. Run locally

Before starting the host, configure a preview extension bundle in `host.json`
that includes Connector Poll support. Installing the npm packages does not
install the host listener; projects initialized with `--no-bundle` also need
this configuration. Follow the
[host extension configuration guide](https://github.com/Azure/azure-functions-connector-extension/blob/main/docs/connector-trigger-configuration.md#non-net-workers)
and verify that the selected published bundle contains Poll support.

```bash
func start
```

Sign in with `az login` first so that the local run can authenticate to the connector namespace.

## 4. Deploy

1. Deploy the app with `func azure functionapp publish <APP_NAME>`.
2. Add the same settings from the **Required settings** table as app settings on the function app.
3. Enable a managed identity on the function app and grant it access to the connector namespace.
