![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-core-tools/refs/heads/main/eng/res/functions.png)

|Branch|Status|
|------|------|
|main|[![Build Status](https://dev.azure.com/azfunc/public/_apis/build/status%2Fazure%2Fazure-functions-core-tools%2Fcoretools.public?repoName=Azure%2Fazure-functions-core-tools&branchName=main)](https://dev.azure.com/azfunc/public/_build/latest?definitionId=579&repoName=Azure%2Fazure-functions-core-tools&branchName=main) |
|in-proc|[![Build Status](https://dev.azure.com/azfunc/public/_apis/build/status%2Fazure%2Fazure-functions-core-tools%2Fcoretools.public?repoName=Azure%2Fazure-functions-core-tools&branchName=in-proc)](https://dev.azure.com/azfunc/public/_build/latest?definitionId=579&repoName=Azure%2Fazure-functions-core-tools&branchName=in-proc)|

# Azure Functions Core Tools

The Azure Functions Core Tools provide a local development experience for creating, developing, testing, running, and debugging Azure Functions.

#### Helpful Documentation

- [Code and test Azure Functions locally](https://docs.microsoft.com/azure/azure-functions/functions-run-local)
- [Get started with Kubernetes using Core Tools](docs/get-started-kubernetes.md)

## Usage

```bash
func [--version] [--help] <command> [<args>] [--verbose]
```

## Versions

**v1** (v1.x branch): Requires .NET 4.7.1 Windows Only

**v4**: (main branch): Self-contained cross-platform package **(recommended)**

## Installing the CLI

### Windows

#### msi

| Version | Platform       | Download Link                                               | Notes                             |
| ------- | -------------- | ----------------------------------------------------------- | --------------------------------- |
| v4      | Windows 64-bit | [Download](https://go.microsoft.com/fwlink/?linkid=2174087) | VS Code debugging requires 64-bit |
| v4      | Windows 32-bit | [Download](https://go.microsoft.com/fwlink/?linkid=2174159) |                                   |
| v3      | Windows 64-bit | [Download](https://go.microsoft.com/fwlink/?linkid=2135274) | VS Code debugging requires 64-bit |
| v3      | Windows 32-bit | [Download](https://go.microsoft.com/fwlink/?linkid=2135275) |                                   |

#### npm

| Version | Installation Command                                       |
| ------- | ---------------------------------------------------------- |
| v4      | `npm i -g azure-functions-core-tools@4`                    |
| v3      | `npm i -g azure-functions-core-tools@3 --unsafe-perm true` |
| v2      | `npm i -g azure-functions-core-tools@2 --unsafe-perm true` |


#### chocolatey

| Version  | Installation Command                                           |
| -------- | -------------------------------------------------------------- |
| v4       | `choco install azure-functions-core-tools`                     |
| v3       | `choco install azure-functions-core-tools-3`                   |
| v2       | `choco install azure-functions-core-tools-2`                   |

> [!NOTE]
> To debug Azure Functions in VSCode, the 64-bit version is required. This is now the default installation.
> However, if needed, you can explicitly specify it using the following parameter: `--params "'/x64'"`

#### winget

| Version | Installation Command                                            |
| ------- | --------------------------------------------------------------- |
| v4      | `winget install Microsoft.Azure.FunctionsCoreTools`             |
| v3      | `winget install Microsoft.Azure.FunctionsCoreTools -v 3.0.3904` |

### Mac

#### homebrew

| Version | Installation Commands                                                        |
| ------- | ---------------------------------------------------------------------------- |
| v4      | `brew tap azure/functions`  <br> `brew install azure-functions-core-tools@4` |
| v3      | `brew tap azure/functions`  <br> `brew install azure-functions-core-tools@3` |
| v2      | `brew tap azure/functions`  <br> `brew install azure-functions-core-tools@2` |


> [!NOTE]
> Homebrew allows side-by-side installation of v2 and v3. You can switch versions with:
>
> `brew link --overwrite azure-functions-core-tools@3`


### Linux

Installation for Linux requires two steps:

1. Setting up the package feed
2. Installing the tools

#### 1. Set up package feed

##### Ubuntu

| OS Version             | Installation Commands                                                                                                                    |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Ubuntu 22.04           | `wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |
| Ubuntu 20.04           | `wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |
| Ubuntu 19.04           | `wget -q https://packages.microsoft.com/config/ubuntu/19.04/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |
| Ubuntu 18.10           | `wget -q https://packages.microsoft.com/config/ubuntu/18.10/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |
| Ubuntu 18.04           | `wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |
| Ubuntu 16.04 / Mint 18 | `wget -q https://packages.microsoft.com/config/ubuntu/16.04/packages-microsoft-prod.deb` <br> `sudo dpkg -i packages-microsoft-prod.deb` |

##### Debian 12

```bash
export DEBIAN_VERSION=12

apt-get update && apt-get install gpg wget  -y

wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor | tee /usr/share/keyrings/microsoft-prod.gpg
wget -q https://packages.microsoft.com/config/debian/$DEBIAN_VERSION/prod.list

mv prod.list /etc/apt/sources.list.d/microsoft-prod.list

chown root:root /usr/share/keyrings/microsoft-prod.gpg
chown root:root /etc/apt/sources.list.d/microsoft-prod.list

apt-get update && apt-get install azure-functions-core-tools-4  -y
apt-get update && apt-get install libicu-dev  -y
```

#### 2. Install

| Version | Installation Commands                                                           |
| ------- | ------------------------------------------------------------------------------- |
| v4      | `sudo apt-get update`  <br> `sudo apt-get install azure-functions-core-tools-4` |
| v3      | `sudo apt-get update`  <br> `sudo apt-get install azure-functions-core-tools-3` |
| v2      | `sudo apt-get update`  <br> `sudo apt-get install azure-functions-core-tools-2` |

### Other Distributions

npm can be used on all platforms. On unix platforms, you may need to specify `--unsafe-perm` if you are running npm with sudo. That's due to npm behavior of post install script.

Alternatively, you can install the CLI manually by downloading the latest release from the GitHub repo:

1. Download the latest release for your platform from [here](https://github.com/Azure/azure-functions-core-tools/releases).

2. Unzip the CLI package
   - Using your preferred tool, unzip the downloaded release. To unzip into an `azure-functions-cli` directory using the `unzip` tool, run this command from the directory containing the downloaded release zip:

    `unzip -d azure-functions-cli Azure.Functions.Cli.linux-x64.*.zip`

3. Make the `func` command executable
   - Zip files do not maintain the executable bit on binaries. So, you'll need to make the `func` binary, as well as `gozip` (used by func during packaging) executables. Assuming you used the instructions above to unzip:

    ```bash
    cd azure-functions-cli
    chmod +x func
    chmod +x gozip
    ./func --version # Test the executable
    ```

4. Optionally add `func` to your `$PATH`
   - To execute the `func` command without specifying the full path to the binary, add its directory to your `$PATH` environment variable. Assuming you're still following along from above:

    ```bash
    export PATH=`pwd`:$PATH
    func
    ```

## Default Directories

* `CurrentDirectory`: is the default directory the functions runtime looks for functions in.
* `%TMP%\LogFiles\Application\Functions`: is the default directory for logs. It mirrors the logs directory on Azure as well.

## Telemetry

The Azure Functions Core tools collect usage data in order to help us improve your experience.
The data is anonymous and doesn't include any user specific or personal information. The data is collected by Microsoft.

You can opt-out of telemetry by setting the `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT` environment variable to '1' or 'true' using your favorite shell.

[Microsoft privacy statement](https://privacy.microsoft.com/privacystatement)

## License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](LICENSE)

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Contact Us

For questions on Azure Functions or the tools, you can ask questions here:

- [Azure Functions Q&A Forum](https://docs.microsoft.com/answers/topics/azure-functions.html)
- [Azure-Functions tag on StackOverflow](http://stackoverflow.com/questions/tagged/azure-functions)

File bugs at [Azure Functions Core Tools repo on GitHub](https://github.com/Azure/azure-functions-core-tools/issues).
