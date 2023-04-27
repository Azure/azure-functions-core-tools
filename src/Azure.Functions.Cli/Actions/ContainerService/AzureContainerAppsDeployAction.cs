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
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.ContainerServiceActions
{
    [Action(Name = "deploy", Context = Context.AzureContainerApps, HelpText = "Deploy function app to Azure Container Apps")]
    class AzureContainerAppsDeployAction : BaseAzureAction
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
        public string Location { get; set; }
        public bool NoDocker { get; set; }
        public string RegistryUserName { get; set; }
        public string RegistryPassword { get; set; }
        public string WorkerRuntime { get; set; }

        public AzureContainerAppsDeployAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, IContextHelpManager contextHelpManager)
        {
            _createFunctionAction = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("name", "The name used for the deployment and other artifacts in kubernetes", s => Name = s, isRequired: true);
            SetFlag<string>("image-name", "Image to use for the pod deployment and to read functions from", n => ImageName = n);
            SetFlag<string>("registry", "When set, a docker build is run and the image is pushed to that registry/name. This is mutually exclusive with --image-name. For docker hub, use username.", r => Registry = r);
            SetFlag<string>("registry-username", "The registry username to pull the image from private registry. ", r => RegistryUserName = r);
            SetFlag<string>("registry-password", "The registry password/token to pull the image from private registry. ", r => RegistryPassword = r);
            SetFlag<string>("pull-secret", "The secret holding a private registry credentials", s => PullSecret = s);
            SetFlag<bool>("image-build", "If true, skip the docker build", f => BuildImage = f);
            SetFlag<string>("resource-group", "Resource Group", r => ResourceGroup = r, isRequired: true);
            SetFlag<string>("environment", "Managed Environment Name", e => ManagedEnvironmentName = e, isRequired: true);
            SetFlag<string>("storage-account", "Storage Account Connection String", e => StorageAccountConnectionString = e, isRequired: true);
            SetFlag<string>("location", "Location of the deployment.", e => Location = e);
            SetFlag<string>("worker-runtime", $"Runtime framework for the functions. The parameter is only accepted with --image-name or --image-build. Options are: {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}", w => WorkerRuntime = w);
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            ValidateParameters();
            (var resolvedImageName, var shouldBuild) = ResolveImageName();
            WorkerRuntime workerRuntime;
            if (BuildImage && shouldBuild)
            {
                if (CurrentPathHasLocalSettings())
                {
                    await _createFunctionAction.UpdateLanguageAndRuntime();
                    workerRuntime = _createFunctionAction.workerRuntime;
                }
                else
                {
                    throw new CliException("The image cannot be built. Please run the command from the project folder. ");
                }

                await DockerHelpers.DockerBuild(resolvedImageName, Environment.CurrentDirectory);
                await DockerHelpers.DockerPush(resolvedImageName, false);
            }
            else
            {
                if (!string.IsNullOrEmpty(WorkerRuntime) && Enum.TryParse<WorkerRuntime>(WorkerRuntime, true, out workerRuntime) && WorkerRuntimeLanguageHelper.AvailableWorkersList.ToList().Contains(workerRuntime))
                {
                    workerRuntime = Enum.Parse<WorkerRuntime>(WorkerRuntime);
                }
                else
                {
                    if (!string.IsNullOrEmpty(WorkerRuntime))
                    {
                        ColoredConsole.WriteLine(WarningColor($"WorkerRuntime '{WorkerRuntime}' is not valid."));
                    }

                    SelectionMenuHelper.DisplaySelectionWizardPrompt("worker runtime");
                    IDictionary<WorkerRuntime, string> workerRuntimeToDisplayString = WorkerRuntimeLanguageHelper.GetWorkerToDisplayStrings();
                    string workerRuntimedisplay = SelectionMenuHelper.DisplaySelectionWizard(workerRuntimeToDisplayString.Values);
                    workerRuntime = workerRuntimeToDisplayString.FirstOrDefault(wr => wr.Value.Equals(workerRuntimedisplay)).Key;
                    ColoredConsole.Write(Environment.NewLine);
                }
            }

            var workerRuntimeName = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime);

            (var managedEnvironmentId, var environmentLocation) = await AzureHelper.GetManagedEnvironmentInfo(AccessToken, ManagementURL, Subscription, ResourceGroup, ManagedEnvironmentName);

            if (string.IsNullOrEmpty(managedEnvironmentId))
            {
                throw new CliException($"The environment \"{ManagedEnvironmentName}\" couldn't be found.");
            }

            if (string.IsNullOrEmpty(Location) || Location == environmentLocation)
            {
                Location = environmentLocation;
            }
            else if (environmentLocation != "eastasia" || !Location.Contains("eastasia"))
            {
                throw new CliException($"Location \"{Location}\" should match with that of environment's \"{environmentLocation}\".");
            }

            var registryHost = GetHostNameFromRegistry();
            var payload = ContainerAppsFunctionPayload.CreateInstance(Name, Location,
                managedEnvironmentId, $"DOCKER|{resolvedImageName}", StorageAccountConnectionString, workerRuntimeName, registryHost, RegistryUserName, RegistryPassword);

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
            if (!string.IsNullOrEmpty(ImageName))
            {
                return ($"{ImageName}", false);
            }
            else if (!string.IsNullOrEmpty(Registry))
            {
                var version = "latest";
                return ($"{Registry}/{Name}:{version}", true);
            }

            throw new CliArgumentsException("either --image-name or --registry is required.");
        }

        private bool CurrentPathHasLocalSettings()
        {
            return FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, Constants.LocalSettingsJsonFileName));
        }

        private string GetHostNameFromRegistry()
        {
            // This condition will also cover the scenario of docker username.
            if (string.IsNullOrEmpty(Registry) || !Registry.Contains('/'))
                return Registry;

            return Registry[..Registry.IndexOf("/")];
        }

        private void ValidateParameters()
        {
            if (!string.IsNullOrEmpty(WorkerRuntime))
            {
                if (string.IsNullOrEmpty(ImageName) && BuildImage == true)
                {
                    throw new CliException("The --worker-runtime can only be passed with --image-name or when --image-build is passed as 'false'.");
                }
            }
        }
    }
}