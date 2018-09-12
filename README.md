![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-cli/master/src/Azure.Functions.Cli/npm/assets/azure-functions-logo-color-raster.png)

|Branch|Status|
|---|---|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/master)|
|v1.x|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/v1.x?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/v1.x)|

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

1. Set up package feed
##### Ubuntu 18.04

```bash
wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
```

##### Ubuntu 17.10

```bash
wget -q https://packages.microsoft.com/config/ubuntu/17.10/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
```

##### Ubuntu 16.04 / Linux Mint 18

```bash
wget -q https://packages.microsoft.com/config/ubuntu/16.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
```

2. Install

```bash
sudo apt-get update
sudo apt-get install azure-functions-core-tools
```

[Code and test Azure Functions locally](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)

**NOTE**: npm can be used on all platforms. On unix platforms, you may need to specify `--unsafe-perm` if you are running npm with sudo. That's due to npm behavior of post install script.


**NOTE**: If you're running the v2 on Windows, Linux, or Mac, make sure to [enable the `beta` runtime](https://docs.microsoft.com/en-us/azure/azure-functions/functions-versions#target-the-version-20-runtime) in function app settings, otherwise you may not see the same results as running locally.

## Getting Started on Kubernetes

Using the Core Tools, you can easily run Azure Functions on 1.7+ Kubernetes clusters.
The Core Tools will build and push a Docker image of the function to a given registry and create corresponding Kubernetes objects including a Deployment, Service and Horizontal Pod Autoscaler.

First, make sure you init a Docker file.

```bash
func init --docker
```
### Prerequisites

* [Docker](https://docs.docker.com/install/)
* [Kubectl](https://kubernetes.io/docs/tasks/tools/install-kubectl/)

### Deploy a function to Kubernetes

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server>
```

### Deploy a function with a minimum of 3 instances and a maximum of 10

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --min 3 --max 10
```

### Get function logs

```
func logs --name myfunction --platform kubernetes
```

### Provide a kubeconfig file

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --config /mypath/config
```

### Deploying Azure Functions with Virtual-Kubelet

Azure Functions running on Kubernetes can take advantage of true serverless containers model by getting deployed to different providers of [Virtual Kubelet](https://github.com/virtual-kubelet/virtual-kubelet), such as Azure Container Instances.<br>

Functions deployed to Kubernetes already contain all the tolerations needed to be schedulable to Virtual Kubelet nodes.
All you need to do is to set up VKubelet on your Kubernetes cluster:

* [Install VKubelet with ACI](https://github.com/virtual-kubelet/virtual-kubelet/tree/master/providers/azure)

* [Install VKubelet with ACI on AKS](https://docs.microsoft.com/en-us/cli/azure/aks?view=azure-cli-latest#az-aks-install-connector)

*Important note:*
Virtual Kubelet does not currently allow for Kubernetes Services to route external traffic to pods.
This means that HTTP triggered functions will not receive traffic running on a VKubelet provider (including ACI).

A good usage scenario for using functions with VKubelet would be with event triggered / time triggered functions that do not rely on external HTTP traffic.

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
