# Getting Started on Kubernetes using Core Tools

Using the Core Tools, you can easily configure a Kubernetes cluster and run Azure Functions on it.

## Prerequisites

* [Docker](https://docs.docker.com/install/)
* [Kubectl](https://kubernetes.io/docs/tasks/tools/install-kubectl/)

## Installing Kubernetes Scalers

This deploys [KEDA](https://github.com/kedacore/keda) to your cluster which allows you to deploy your functions in a scale-to-zero by default for non-http scenarios only.

```bash
func kubernetes install --namespace {namespace}
```

**KEDA:** Handles monitoring polling event sources currently QueueTrigger and ServiceBusTrigger.

## Deploy to Kubernetes

**First make sure you have Dockerfile for your project.** You can generate one using:

```bash
func init --docker # or --docker-only (for existing projects)
```

Then to deploy to kubernetes:

```bash
func kubernetes deploy \
    --name myfunction \
    --namespace functions-ns \
    --registry <docker-hub-id or registry-server>
```

This will build the current `Dockerfile` and push the image to the registry specified, then deploys a `Secret`, `Deployment`, and `ScaledObject`. If your functions have httpTrigger, you'll get an additional `Deployment` and `Service`.

### Deploy using a private registry

```bash
func kubernetes deploy --name myfunction --registry <docker-hub-id or registry-server> --pull-secret <registry auth secret>
```

### Deploy a function to Knative

* [Knative](https://github.com/knative/docs/tree/master/docs/install)

Deploying Azure Functions to knative is supported with the ```--platform knative``` flag.
The Core Tools CLI identifies non HTTP trigger functions and annotates the knative manifest with the the ```minScale``` annotation to opt out of scale-to-zero.

```bash
func deploy --platform knative --name myfunction --registry <docker-hub-id or registry-server>
```

### Deploying a function to AKS using ACR

Using the configuration options an Azure Function app can also be deployed to a [AKS](https://azure.microsoft.com/services/kubernetes-service/) (Azure Kubernetes Service) Kubernetes cluster and use [ACR](https://azure.microsoft.com/services/container-registry/) as the registry server. Do all of the following *before* you run the deployment command.

#### Create a AKS cluster

You can create an AKS cluster using the [Azure Portal](https://docs.microsoft.com/azure/aks/kubernetes-walkthrough-portal) or using [Azure CLI](https://docs.microsoft.com/azure/aks/kubernetes-walkthrough).

Once your AKS cluster is created make sure that you can access it using kubectl. To make kubectl run in the context of your cluster, configure a connection using the command below.

```azurecli
az aks get-credentials \
    --name FunctionsCluster \
    --resource-group <resource-group-name>
```

To verify the connection to your cluster run the following command:

```bash
> kubectl get nodes

NAME                       STATUS    ROLES     AGE       VERSION
aks-agentpool-20257154-0   Ready     agent     1d        v1.11.5
aks-agentpool-20257154-1   Ready     agent     1d        v1.11.5
aks-agentpool-20257154-2   Ready     agent     1d        v1.11.5
```

#### Create a ACR Registry

An ACR instance can be created using the Azure Portal or the [Azure CLI](https://docs.microsoft.com/azure/container-registry/container-registry-get-started-azure-cli#create-a-container-registry)

#### Login to the ACR Registry

Before pushing and pulling container images, you must log in to the ACR instance.

```azurecli
az acr login --name <acrName>
```

#### Give the AKS cluster access to the ACR Registry

The AKS cluster needs access to the ACR Registry to pull the container. Azure creates a service principal to support cluster operability with other Azure resources. This can be used for authentication with an ACR registry. See here for how to grant the right access here: [Authenticate with Azure Container Registry from Azure Kubernetes Service](https://docs.microsoft.com/azure/container-registry/container-registry-auth-aks)

#### Run the deployment

The deployment will build the docker container and upload the container image to your referenced ACR instance (Note: Specify the ACR Login Server in the --registry parameter this is usually of the form <container_registry_name>.azurecr.io) and then your AKS cluster will use that as a source to obtain the container and deploy it.

```bash
func kubernetes deploy --name myfunction --registry <acr-registry-loginserver>
```

If the deployment is successful, you should see this:

Function deployed successfully!
Function IP: 40.121.21.192

#### Verifying your deployment

You can verify your deployment by using the Kubernetes web dashboard. To start the Kubernetes dashboard, use the [az aks browse](https://docs.microsoft.com/cli/azure/aks?view=azure-cli-latest#az-aks-browse) command.

```azurecli
az aks browse --resource-group myResourceGroup --name myAKSCluster
```
In the Kubernetes dashboard look for the namespace "azure-functions" and make sure that a pod has been deployed sucessfully with your container.

### Deploying Azure Functions with Virtual-Kubelet

Azure Functions running on Kubernetes can take advantage of true serverless containers model by getting deployed to different providers of [Virtual Kubelet](https://github.com/virtual-kubelet/virtual-kubelet), such as Azure Container Instances.<br>

Functions deployed to Kubernetes already contain all the tolerations needed to be schedulable to Virtual Kubelet nodes.
All you need to do is to set up VKubelet on your Kubernetes cluster:

* [Install VKubelet with ACI](https://github.com/virtual-kubelet/azure-aci)

* [Install VKubelet with ACI on AKS](https://docs.microsoft.com/cli/azure/aks?view=azure-cli-latest#az-aks-install-connector)

*Important note:*
Virtual Kubelet does not currently allow for Kubernetes Services to route external traffic to pods.
This means that HTTP triggered functions will not receive traffic running on a VKubelet provider (including ACI).

A good usage scenario for using functions with VKubelet would be with event triggered / time triggered functions that do not rely on external HTTP traffic.
