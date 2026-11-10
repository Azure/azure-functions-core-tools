# Azure Functions connector polling triggers for Node.js

These JavaScript and TypeScript templates are embedded in Core Tools' [Node.js v4 catalog](../src/Cli/func/StaticResources/node-v4-templates.json) and receive events from a Connector Namespace without a public callback or tunnel.
Each template creates one function. It does not provision Azure resources, authorize
connections, or create trigger configurations.

## Templates and operations

| Template | Connector operation | Default function name | Trigger config app setting |
| --- | --- | --- | --- |
| Office 365 trigger | `OnNewEmailV3` | `office365NewEmailPoll` | `OnNewEmailTriggerConfigName` |
| SharePoint trigger | `GetOnNewFileItems` | `sharepointNewFilePoll` | `OnSharepointNewFileTriggerConfigName` |
| OneDrive trigger | `OnNewFilesV2` | `oneDriveNewFilePoll` | `OnOneDriveNewFileTriggerConfigName` |
| Dataverse trigger | `GetOnNewItems_V2` | `dataverseNewRowPoll` | `OnDataverseRowChangedTriggerConfigName` |
| Teams trigger | `OnNewChannelMessage` | `teamsNewChannelMessagePoll` | `OnNewChannelMessageTriggerConfigName` |

SharePoint returns file properties, not file content. OneDrive returns file
metadata, not the similarly named `OnNewFileV2` content operation.

The Dataverse template mirrors the Python template's legacy **When a row is added
(Admin Only)** operation, which is **deprecated** in the
[Dataverse connector reference](https://learn.microsoft.com/connectors/commondataservice/).
It receives new rows only, not updates or deletions. The existing
`OnDataverseRowChangedTriggerConfigName` setting name is retained for consistency
with Python; it does not change the operation. Use `dataset=<organization HTTPS URL>`
and `table=<plural entity-set name>` when configuring this operation. Dataverse
uses the generic `connectorTrigger<Item>` API because the extension has no named
Dataverse helper.

## Create and configure a function

Use Node.js 24 or later, Azure Functions Core Tools v4, and an existing Node.js
programming-model-v4 project. The TypeScript templates use imports and strict SDK types.

**JavaScript is CommonJS-only in this preview.** The generated `.js` files use `require` and work when `package.json` omits `type` or declares `"type": "commonjs"`. They do not run unchanged in projects declaring `"type": "module"`; `require` is undefined there. The picker is not filtered by package module type. In an ESM project, use the TypeScript template with a matching ESM compiler configuration or convert the generated JavaScript imports to ESM before loading it. Do not change an existing application's package module type just to accommodate this template.

Use a Core Tools build containing these templates, not an older globally installed CLI. No extension-bundle template catalog replacement or private bundle probing directory is needed. See [local generation tests](../CONTRIBUTING.md#nodejs-connector-template-tests) to build and test from source.

For a generation-only project:

```sh
func init connector-app --worker-runtime node --language typescript --model V4 --no-bundle --skip-npm-install
cd connector-app
func new --template "Office 365 trigger" --name office365NewEmailPoll
npm install "@azure/functions@^4.17.1" "@azure/functions-extensions-connectors@0.2.0-preview" "@azure/connectors@0.4.0-preview"
```

Use `--language javascript` for a JavaScript project. The generated module is
placed in `src/functions` and registered through the project's `package.json`
`main` glob. Do not mix v3 `function.json` functions with these v4 registrations.
If these package versions are not yet available in your registry, install trusted
tarballs of these exact releases rather than substituting older packages.

Core Tools prompts for the function name and writes the module. Connection settings and trigger config names are not additional CLI prompts. Instead, `func new` also:

- adds any missing required settings (`ConnectorNamespace__pollingEndpoint` and the template's trigger config name setting, for example `OnNewEmailTriggerConfigName`) to `local.settings.json` with placeholder values, without overwriting existing values;
- writes `<functionName>.README.md` to the project root (skipped if it already exists) with local testing and deployment instructions.

Each generated module includes the comment `// Visit aka.ms/connectortrigger to local testing and deployment instructions.` immediately before the trigger registration. VS Code's Node v4 template list is bundled separately by the VS Code extension; publishing Core Tools does not update that list.

For TypeScript, merge these options into the existing `tsconfig.json`
`compilerOptions`, keeping the project's `rootDir`, `outDir`, and other settings:

```json
{
  "module": "Node16",
  "moduleResolution": "Node16",
  "target": "ES2022",
  "strict": true
}
```

The connector SDK uses package `exports` for its generated model types. Older
Core Tools scaffolds default to legacy Node module resolution, which cannot
resolve those subpath imports. The paired Node16 settings support both the
CommonJS project output and the SDK's exported type declarations.

1. In the [Connector Namespaces portal](https://connectors.azure.com/), create a
   namespace and authorize a connection for the selected connector.
2. Create an enabled Trigger Config for the table's operation and its required
   parameters, using service `deliveryMode: "Poll"` and no webhook
   `notificationDetails`. An operation with "Webhook" in its name does not select
   the Functions delivery mode.
3. Grant connection access policies to both the namespace identity that runs the
   connector operation and the Function/developer identity that receives and
   acknowledges events.
4. Merge the following example into `local.settings.json` for Office 365. For the
   other templates, use the app setting name from the table:

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "node",
       "ConnectorNamespace__pollingEndpoint": "https://<connector-namespace-host>",
       "OnNewEmailTriggerConfigName": "<poll-trigger-config-name>"
     }
   }
   ```

`connection` is an app setting prefix, not the connector connection resource name.
Derive `ConnectorNamespace__pollingEndpoint` from the Trigger Config's
`pollingEndpoints.receiveUri`. Remove `/triggerconfigs/<name>/receive` and the query
string, preserving an `/api/connectorGateways/<id>` prefix when present. For stable
namespace DNS, use only `https://<connector-namespace-host>` without `/api`.
`triggerConfigName` accepts either `%AppSettingName%` or a literal config name.
Changing the function name does not rename its trigger config setting.

## Run and verify

The `--no-bundle` example above is for generation and compilation only. Before starting a host, configure a preview host extension bundle that includes Connector Poll support; the built-in template catalog is independent of that runtime requirement.
The npm extension supplies the typed Node.js API, not the host listener. Published
bundles can lag the host extension source; verify feature availability rather than
assuming a version range guarantees Poll support. Follow the
[host extension configuration guide](https://github.com/Azure/azure-functions-connector-extension/blob/main/docs/connector-trigger-configuration.md#non-net-workers).

Sign in locally with `az login`, start Azurite if using development storage, and
run `npm start`. For Azure, configure the same app settings plus
`ConnectorNamespace__credential=managedidentity`, and authorize the selected
managed identity on the connection. Restart the host after configuration changes.
Use separate trigger configs or stop the deployed app when testing locally;
listeners sharing a config compete for events.

Generate a source event and look for the received-item count. An enabled config
alone does not prove source registration or event delivery succeeded. No live
connector events are generated by installing or creating the template.

The templates select `DeliveryMode.Poll` using the enum exported by
`@azure/functions` starting in `4.17.1`. They omit cardinality because the library
defaults to `Cardinality.One`; omitting delivery mode instead selects
`DeliveryMode.Webhook`.

The templates use single-event invocations. Each event can contain several items,
exposed through the typed context's `emails`, `records`, `files`, `messages`, or
generic `items` array. The original envelope is available through `rawPayload`.
For batching, import `Cardinality` from `@azure/functions`, set
`cardinality: Cardinality.Many`, and set `maxBatchSize` to a supported value;
do not use `maxConcurrentCalls` with batched invocations. See the
[polling and concurrency guide](https://github.com/Azure/azure-functions-connector-extension/blob/main/docs/connector-poll-concurrency-visual-guide.md).

Numeric tuning options remain omitted, matching the Python templates. `Cardinality.One` selects the invocation shape; it is not an explicit service Receive batch-size override. If an application also needs to pin the receive batch size to one rather than inherit host configuration, set `maxBatchSize: 1` deliberately.

Each generated module lists the selected operation, alternative operations, and SDK payload types. Use the matching typed helper or generic model when changing operations, and configure the corresponding trigger parameters separately. The pinned Node Dataverse SDK exports only the two listed legacy row operations; the additional Python deletion/action notification models are not assumed to be available in Node.

Only item counts are logged. Keep email, message, file, and row contents out of
logs, and never commit credentials or private local settings.

## More connectors and events

- [Connector reference](https://aka.ms/connectors)
- [TypeScript connector samples](https://github.com/Azure-Samples/functions-connectors-typescript)
- [Office 365 samples](https://github.com/Azure-Samples/functions-connectors-typescript/tree/main/office365App)
- [SharePoint samples](https://github.com/Azure-Samples/functions-connectors-typescript/tree/main/sharepointApp)
- [OneDrive samples](https://github.com/Azure-Samples/functions-connectors-typescript/tree/main/onedriveApp)
- [Teams samples](https://github.com/Azure-Samples/functions-connectors-typescript/tree/main/teamsApp)
- [Dataverse SDK models and operation metadata](https://github.com/Azure/Connectors-NodeJS-SDK)
- [Node.js programming model v4](https://learn.microsoft.com/azure/azure-functions/functions-node-upgrade-v4)
