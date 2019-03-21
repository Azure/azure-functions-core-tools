![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-cli/master/src/Azure.Functions.Cli/npm/assets/azure-functions-logo-color-raster.png)

|Branch|Status|
|---|---|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/master)|
|dev|[![Build status](https://ci.appveyor.com/api/projects/status/max86pwo54y44j36/branch/dev?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-cli/branch/dev)|
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
npm i -g azure-functions-core-tools@1
```

To install v1 with chocolatey:

```bash
choco install azure-functions-core-tools --version 1.0.15 
```

To install v2 with npm:

```bash
npm i -g azure-functions-core-tools --unsafe-perm true
```

To install v2 with chocolatey:

```bash
choco install azure-functions-core-tools
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

##### Ubuntu 18.10

```bash
wget -q https://packages.microsoft.com/config/ubuntu/18.10/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
```

##### Ubuntu 18.04

```bash
wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
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

#### Other Linux Distributions

1. Download latest release

Download the latest release for your platform from [here](https://github.com/Azure/azure-functions-core-tools/releases).

2. Unzip release zip

Using your preferred tool, unzip the downloaded release. To unzip into an `azure-functions-cli` directory using the `unzip` tool, run this command from the directory containing the downloaded release zip:

```bash
unzip -d azure-functions-cli Azure.Functions.Cli.linux-x64.*.zip
```

3. Make the `func` command executable

Zip files do not maintain the executable bit on binaries. So, you'll need to make the `func` binary executable. Assuming you used the instructions above to unzip:

```bash
cd azure-functions-cli
chmod +x func
./func
```

4. Optionally add `func` to your `$PATH`

To execute the `func` command without specifying the full path to the binary, add its directory to your `$PATH` environment variable. Assuming you're still following along from above:

```bash
export PATH=`pwd`:$PATH
func
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

### Deploy using a private registry

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --pull-secret <registry auth secret>
```

### Deploy a function with a minimum of 3 instances and a maximum of 10

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --min 3 --max 10
```

### Deploy a function to a custom namespace

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --namespace <namespace-name>
```

#### Scaling out Http Trigger
Currently the solution is configured to scale out using the Horizontal Pod Autoscaler when any pod reaches a CPU of 60%.

### Get function logs

```
func logs --name myfunction --platform kubernetes
```

### Provide a kubeconfig file

```bash
func deploy --platform kubernetes --name myfunction --registry <docker-hub-id or registry-server> --config /mypath/config
```
### Deploy a function to Knative

#### Prerequisites

* [Knative](https://github.com/knative/docs/tree/master/install/)

Deploying Azure Functions to knative is supported with the ```--platform knative``` flag.
The Core Tools CLI identifies non HTTP trigger functions and annotates the knative manifest with the the ```minScale``` annotation to opt out of scale-to-zero.

```bash
func deploy --platform knative --name myfunction --registry <docker-hub-id or registry-server>
```

### Deploying a function to AKS using ACR
Using the configuration options an Azure Function app can also be deployed to a [AKS](https://azure.microsoft.com/en-us/services/kubernetes-service/) (Azure Kubernetes Service) Kubernetes cluster and use [ACR](https://azure.microsoft.com/en-us/services/container-registry/) as the registry server. Do all of the following *before* you run the deployment command.

#### Create a AKS cluster
You can create an AKS cluster using the [Azure Portal](https://docs.microsoft.com/en-us/azure/aks/kubernetes-walkthrough-portal) or using [Azure CLI](https://docs.microsoft.com/en-us/azure/aks/kubernetes-walkthrough).

Once your AKS cluster is created make sure that you can access it using kubectl. To make kubectl run in the context of your cluster, configure a connection using the command below.
```azurecli
az aks get-credentials \
    --name FunctionsCluster \
    --resource-group <resource-group-name>
```

To verify the connection to your cluster run the following command
```bash
> kubectl get nodes

NAME                       STATUS    ROLES     AGE       VERSION
aks-agentpool-20257154-0   Ready     agent     1d        v1.11.5
aks-agentpool-20257154-1   Ready     agent     1d        v1.11.5
aks-agentpool-20257154-2   Ready     agent     1d        v1.11.5
```
#### Create a ACR Registry
An ACR instance can be created using the Azure Portal or the [Azure CLI](https://docs.microsoft.com/en-us/azure/container-registry/container-registry-get-started-azure-cli#create-a-container-registry)

#### Login to the ACR Registry
Before pushing and pulling container images, you must log in to the ACR instance. 

```azurecli
az acr login --name <acrName>
```

#### Give the AKS cluster access to the ACR Registry
The AKS cluster needs access to the ACR Registry to pull the container. Azure creates a service principal to support cluster operability with other Azure resources. This can be used for authentication with an ACR registry. See here for how to grant the right access here: [Authenticate with Azure Container Registry from Azure Kubernetes Service](https://docs.microsoft.com/en-us/azure/container-registry/container-registry-auth-aks)  

#### Run the deployment 
The deployment will build the docker container and upload the container image to your referenced ACR instance (Note: Specify the ACR Login Server in the --registry parameter this is usually of the form <container_registry_name>.azurecr.io) and then your AKS cluster will use that as a source to obtain the container and deploy it.

```bash
func deploy --platform kubernetes --name myfunction --registry <acr-registry-loginserver>
```

If the deployment is successful, you should see this:

Function deployed successfully!
Function IP: 40.121.21.192

#### Verifying your deployment 
You can verify your deployment by using the Kubernetes web dashboard. To start the Kubernetes dashboard, use the [az aks browse](https://docs.microsoft.com/en-us/cli/azure/aks?view=azure-cli-latest#az-aks-browse) command.

```azurecli
az aks browse --resource-group myResourceGroup --name myAKSCluster
```
In the Kubernetes dashboard look for the namespace "azure-functions" and make sure that a pod has been deployed sucessfully with your container.

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
