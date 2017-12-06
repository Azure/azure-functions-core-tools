![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-cli/master/src/Azure.Functions.Cli/npm/assets/azure-functions-logo-color-raster.png)

|Branch|Status|
|---|---|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/master)|

# Azure Functions Core Tools

The Azure Functions Core Tools provide a local development experience for creating, developing, testing, running, and debugging Azure Functions.

## Installing

**NOTE**: This package only currently works on Windows, since the underlying Functions Host is not yet cross-platform. You can upvote this GitHub issue if you're interested in running on other platforms: [make the Azure Functions Core Tools cross platform](https://github.com/Azure/azure-functions-cli/issues/13).

To install globally for Windows:

```
npm i -g azure-functions-core-tools
```

To install globally for non-Windows platforms:

```
npm i -g azure-functions-core-tools@core
```
This installs a higher beta version of the cli, so it is highly recommended that while using the core tag, you [enable the `beta` runtime](https://docs.microsoft.com/en-us/azure/azure-functions/functions-versions#target-the-version-20-runtime) in function app settings, otherwise you may not see the same results as running locally.


### Dependencies

There is a dependency on the .NET Core tools for the cross platform support. You can [install these here](https://www.microsoft.com/net/core).

### Aliases

The package sets up the following global aliases:

```
func
azfun
azure-functions
```

## Commands

Commands have the following basic structure:

```
func [context] [context] <action> [-/--options]
```

Output can be found at *%temp%\LogFiles*.

### Contexts

```
azure        For Azure login and working with Function Apps on Azure
function     For local function settings and actions
functionapp  For local function app settings and actions
host         For local Functions host settings and actions
settings     For local settings for your Functions host
```

### Top-level actions

```
func init    Create a new Function App in the current folder. Initializes git repo.
func run     Run a function directly
```

### Azure actions

Actions in the "azure" context require logging in to Azure.

```
func azure

Usage: func azure [context] <action> [-/--options]

Contexts:
account        For Azure account and subscriptions settings and actions
functionapp    For Azure Function App settings and actions
storage        For Azure Storage settings and actions
subscriptions  For Azure account and subscriptions settings and actions

Actions:
get-publish-username  Get the source control publishing username for a Function App in Azure
set-publish-password  Set the source control publishing password for a Function App in Azure
login                 Log in to an Azure account. Can also do "func azure login"
logout                Log out of Azure account. Can also do "func azure logout"
portal                Launch default browser with link to the current app in https://portal.azure.com
```

```
func azure account
Usage: func azure account <action> [-/--options]

Actions:
set <subscriptionId> Set the active subscription
list  List subscriptions for the logged in user
```

```
func azure functionapp
Usage: func azure functionapp <action> [-/--options]

Actions:
enable-git-repo     Enable git repository on your Azure-hosted Function App
fetch-app-settings  Retrieve App Settings from your Azure-hosted Function App and store locally. Alias: fetch
list                List all Function Apps in the selected Azure subscription
```

The `func azure storage list` command will show storage accounts in the selected subscription. You can then set up a connection string locally with this storage account name using `func settings add-storage-account`.

```
func azure storage
Usage: func Azure Storage <action> [-/--options]

Actions:
list  List all Storage Accounts in the selected Azure subscription
```

### Local actions

Actions that are not in the "azure" context operate on the local environment. For instance, `func settings list` will show the app settings for the current function app.

```
func settings
Usage: func settings [context] <action> [-/--options]

Actions:
add                  Add new local app setting to appsettings.json
add-storage-account  Add a local app setting using the value from an Azure Storage account. Requires Azure login.
decrypt              Decrypt the local settings file
delete               Remove a local setting
encrypt              Encrypt the local settings file
list                 List local settings
```

```
func function
Usage: func function [context] <action> [-/--options]

Actions:
create  Create a new Function from a template, using the Yeoman generator
run     Run a function directly
```

For consistency, the `func init` command can also be invoked via `func functionapp init`.

```
func functionapp init
```

## Default Directories

* `CurrentDirectory`: is the default directory the functions runtime looks for functions in.
* `%TMP%\LogFiles\Application\Functions`: is the default directory for logs. It mirrors the logs directory on Azure as well.

## License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](LICENSE.txt)

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Contact Us

For questions on Azure Functions or the tools, you can ask questions here:

- [Azure Functions MSDN Forum](https://social.msdn.microsoft.com/Forums/azure/en-US/home?forum=AzureFunctions)
- [Azure-Functions tag on StackOverflow](http://stackoverflow.com/questions/tagged/azure-functions)

File bugs at [Azure Functions Core Tools repo on GitHub](https://github.com/Azure/azure-functions-cli/issues).
