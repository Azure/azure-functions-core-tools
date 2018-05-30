![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-cli/master/src/Azure.Functions.Cli/npm/assets/azure-functions-logo-color-raster.png)

|Branch|Windows|Linux
|---|---|---|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/master)| [![Build status](https://ci.appveyor.com/api/projects/status/m950ifgopq49l7gw?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-core-tools)|
|v1.x|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/v1.x?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/v1.x)| `N/A`|

# Azure Functions Core Tools

The Azure Functions Core Tools provide a local development experience for creating, developing, testing, running, and debugging Azure Functions.

## Versions

**v1** (v1.x branch): Requires .NET 4.7.1 Windows Only

**v2** (master branch): Self-contained cross-platform package

## Installing

### Windows

Both v1 and v2 of the runtime can be installed on Windows.

To install v1 with npm:

```bash
npm i -g azure-functions-core-tools
```

To install v1 with chocolatey:

```bash
choco install azure-functions-core-tools
```

To install v2 with npm:

```bash
npm i -g azure-functions-core-tools@core --unsafe-perm true
```

To install v2 with chocolatey:

```bash
choco install azure-functions-core-tools --pre
```

### Mac

**Homebrew**:

```bash
brew tap azure/functions
brew install azure-functions-core-tools
```

### Linux

#### Ubuntu/Debian

1. Register the Microsoft Product key as trusted

```bash
curl https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.gpg
sudo mv microsoft.gpg /etc/apt/trusted.gpg.d/microsoft.gpg
```

2. Set up package feed
##### Ubuntu 17.10

```bash
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-artful-prod artful main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
```

##### Ubuntu 17.04

```bash
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-zesty-prod zesty main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
```

##### Ubuntu 16.04 / Linux Mint 18

```bash
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-xenial-prod xenial main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
```

3. Install

```bash
sudo apt-get install azure-functions-core-tools
```

#### Fedora/CentOS/Red Hat/openSUSE

1. Register the Microsoft signature key

```bash
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
```

2. Set up package feed
##### yum

```bash
sudo sh -c 'echo -e "[packages-microsoft-com-prod]\nname=packages-microsoft-com-prod \nbaseurl=https://packages.microsoft.com/yumrepos/microsoft-rhel7.3-prod\nenabled=1\ngpgcheck=1\ngpgkey=https://packages.microsoft.com/keys/microsoft.asc" > /etc/yum.repos.d/dotnetdev.repo'
```

##### zypper

```bash
sudo sh -c 'echo -e "[packages-microsoft-com-prod]\nname=packages-microsoft-com-prod \nbaseurl=https://packages.microsoft.com/yumrepos/microsoft-rhel7.3-prod\nenabled=1\ngpgcheck=1\ngpgkey=https://packages.microsoft.com/keys/microsoft.asc" > /etc/zypp/repos.d/dotnetdev.repo'
```

3. Install
##### yum

```bash
sudo yum install azure-functions-core-tools
```

##### zypper

```bash
sudo zypper install azure-functions-core-tools
```

[Code and test Azure Functions locally](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)

**NOTE**: npm can be used on all platforms. On unix platforms, you may need to specify `--unsafe-perm` if you are running npm with sudo. That's due to npm behavior of post install script.


**NOTE**: If you're running the v2 on Windows, Linux, or Mac, make sure to [enable the `beta` runtime](https://docs.microsoft.com/en-us/azure/azure-functions/functions-versions#target-the-version-20-runtime) in function app settings, otherwise you may not see the same results as running locally.

## Known Issues:

`func extensions` command require the `dotnet` cli to be installed and on your path. This requirement is tracked [here](https://github.com/Azure/azure-functions-core-tools/issues/367). You can install .NET Core for your platform from https://www.microsoft.com/net/download/

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

File bugs at [Azure Functions Core Tools repo on GitHub](https://github.com/Azure/azure-functions-core-tools/issues).
