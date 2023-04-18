using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ContainerApps.Models;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using Fclp;
using Microsoft.Extensions.FileSystemGlobbing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.ContainerServiceActions
{
    [Action(Name = "deploy", Context = Context.ContainerService, HelpText = "")]
    class ContainerServiceDeployAction : BaseAzureAction
    {
        private readonly CreateFunctionAction _createFunctionAction;
        // greens
        public bool DryRun { get; private set; }
        public string ImageName { get; private set; }
        public string Name { get; set; } = string.Empty;
        public string Registry { get; set; }
        public bool BuildImage { get; set; } = true;
        public string PullSecret { get; set; } = string.Empty;
        public string ResourceGroup { get; set; }
        public string ManagedEnvironmentName { get; set; }
        public string StorageAccountConnectionString { get; set; }

        // More Params
        public string HashFilesPattern { get; set; } = "";
        public bool NoDocker { get; set; }


        public ContainerServiceDeployAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, IContextHelpManager contextHelpManager)
        {
            _createFunctionAction = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("name", "The name used for the deployment and other artifacts in kubernetes", s => Name = s, isRequired: true);
            SetFlag<string>("image-name", "Image to use for the pod deployment and to read functions from", n => ImageName = n);
            SetFlag<string>("registry", "When set, a docker build is run and the image is pushed to that registry/name. This is mutually exclusive with --image-name. For docker hub, use username.", r => Registry = r);
            SetFlag<string>("pull-secret", "The secret holding a private registry credentials", s => PullSecret = s);
            SetFlag<bool>("image-build", "If true, skip the docker build", f => BuildImage = f);
            SetFlag<string>("resource-group", "Resource Group", r => ResourceGroup = r, isRequired: true);
            SetFlag<string>("environment", "Managed Environment Name", e => ManagedEnvironmentName = e, isRequired: true);
            SetFlag<string>("storage-account", "Storage Account Connection String", e => StorageAccountConnectionString = e, isRequired: true);

            // Others
            SetFlag<string>("hash-files", "Files to hash to determine the image version", f => HashFilesPattern = f);
            SetFlag<bool>("no-docker", "With --image-name, the core-tools will inspect the functions inside the image. This will require mounting the image filesystem. Passing --no-docker uses current directory for functions.", nd => NoDocker = nd);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            WorkerRuntime workerRuntime;
            if (string.IsNullOrEmpty(ImageName))
            {
                if (CurrentPathHasLocalSettings())
                {
                    await _createFunctionAction.UpdateLanguageAndRuntime();
                    workerRuntime = _createFunctionAction.workerRuntime;
                }
                else
                {
                    throw new CliException("The image cannot be built. Please run the command from the project folder or run the command --image-name. ");
                }
            }
            else
            {
                IDictionary<WorkerRuntime, string> workerRuntimeToDisplayString = WorkerRuntimeLanguageHelper.GetWorkerToDisplayStrings();
                string workerRuntimedisplay = SelectionMenuHelper.DisplaySelectionWizard(workerRuntimeToDisplayString.Values);
                workerRuntime = workerRuntimeToDisplayString.FirstOrDefault(wr => wr.Value.Equals(workerRuntimedisplay)).Key;
            }

            var workerRuntimeName = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime);

            (var resolvedImageName, var shouldBuild) = ResolveImageName();

            if (BuildImage && shouldBuild)
            {
                await DockerHelpers.DockerBuild(resolvedImageName, Environment.CurrentDirectory);
                await DockerHelpers.DockerPush(resolvedImageName, false);
            }

            var managedEnvironmentId = await AzureHelper.GetManagedEnvironmentID(AccessToken, ManagementURL, Subscription, ResourceGroup, ManagedEnvironmentName);

            if (string.IsNullOrEmpty(managedEnvironmentId))
            {
                throw new CliException("Managed Environment couldn't be found. Please confirm the Managed Environment Names");
            }

            var payload = ContainerAppsFunctionPayload.CreateInstance(Name, "East Asia (Stage)", 
                managedEnvironmentId, $"DOCKER|{resolvedImageName}", StorageAccountConnectionString, workerRuntimeName);

            await AzureHelper.CreateFunctionAppOnContainerService(AccessToken, ManagementURL, Subscription, ResourceGroup, payload);

            ColoredConsole.Write("Getting Function App information..");
            Arm.Models.Site functionApp = null;

            for (int i = 0; functionApp == null && i < 12; i++)
            {
                try
                {
                    Console.Write(".");
                    await Task.Delay(10000);
                    functionApp = await AzureHelper.GetFunctionApp(payload.Name, AccessToken, ManagementURL, defaultSubscription: Subscription, loadFunction: AzureHelper.LoadFunctionAppInContainerApp);
                }
                catch (ArmResourceNotFoundException)
                {
                    continue;
                }
            }

            ColoredConsole.Write(Environment.NewLine);

            if (functionApp != null)
            {
                await AzureHelper.PrintFunctionsInfo(functionApp, AccessToken, ManagementURL, showKeys: true);
            }
        }

        private (string, bool) ResolveImageName()
        {
            var version = "latest";
            if (!string.IsNullOrEmpty(Registry))
            {
                return ($"{Registry}/{Name}:{version}", true && !NoDocker);
            }
            else if (!string.IsNullOrEmpty(ImageName))
            {
                return ($"{ImageName}", false);
            }
            throw new CliArgumentsException("either --image-name or --registry is required.");
        }

        private bool CurrentPathHasLocalSettings()
        {
            return FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, "local.settings.json"));
        }
    }
}