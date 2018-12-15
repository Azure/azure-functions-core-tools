using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "up", HelpText = "Create a new Function App in the current folder.")]
    internal class UpAction : BaseAzureAction
    {
        private readonly ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;
        private readonly ISettings _settings;

        public string WorkerRuntime { get; set; }

        public bool Force { get; set; }

        public string Location { get; set; }

        public UpAction(ISettings settings, ITemplatesManager templatesManager, ISecretsManager secretsManager)
        {
            _settings = settings;
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
            .Setup<string>("worker-runtime")
            .SetDefault(null)
            .WithDescription($"Runtime framework for the functions. Options are: {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}")
            .Callback(w => WorkerRuntime = w);

            Parser
            .Setup<bool>("force")
            .WithDescription("Force initializing")
            .Callback(f => Force = f);

            Parser
            .Setup<string>("location")
            .WithDescription("Location or Region of your new functionapp.")
            .Callback(f => Location = f);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (NeedNewLocalProject(out string functionProject))
            {
                functionProject = await CreateNewLocalProject();
                await CreateNewFunction();
            }
            Environment.CurrentDirectory = functionProject;
            var functionAppName = await GetFunctionAppName();
            await PublishLocalProject(functionAppName);
        }

        private bool NeedNewLocalProject(out string functionProject)
        {
            try
            {
                functionProject = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
                return false;
            }
            catch (Exception)
            {
                functionProject = default(string);
                return true;
            }
        }

        private bool TryGetStorageAndFunctionAppNames(out string functionAppName)
        {
            var publishConfigFileName = Path.Combine(Environment.CurrentDirectory, Constants.PublishConfigurationFileName);
            if (FileSystemHelpers.FileExists(publishConfigFileName))
            {
                try
                {
                    var publishConfig = FileSystemHelpers.ReadAllTextFromFile(publishConfigFileName);
                    functionAppName = JsonConvert.DeserializeObject<JObject>(publishConfig)[Constants.FunctionAppKeyPublish].ToString();
                    return true;
                }
                catch (Exception)
                {
                    ColoredConsole.WriteLine(Yellow($"Could not parse {Constants.PublishConfigurationFileName} file. Continuing without it..."));
                }
            }
            functionAppName = default(string);
            return false;
        }

        private async Task<string> GetFunctionAppName()
        {
            if (TryGetStorageAndFunctionAppNames(out string functionAppName))
            {
                ColoredConsole.WriteLine($"Using functionapp {functionAppName} from {Constants.PublishConfigurationFileName}");
                return functionAppName;
            }
            if (string.IsNullOrEmpty(Location))
            {
                throw new CliException("Must specify --location in order to create a new function app");
            }
            var resourceGroup = await CreateAzureResourceGroup();
            var storageAccount = await CreateAzureStorage(resourceGroup);
            var functionApp = await CreateAzureFunctionApp(resourceGroup, storageAccount);
            SavePublishConfiguration(functionApp);
            return functionApp;
        }

        private string GetPossibleResourceName(Random random, string baseName)
        {
            const int extraRandom = 6;
            const string chars = "acdefghijklmnopqrstuvwxyz";

            var randomElement = new string(Enumerable.Repeat(chars, extraRandom)
              .Select(s => s[random.Next(s.Length)]).ToArray());
            return baseName + randomElement;
        }

        private void SavePublishConfiguration(string functionApp)
        {
            IDictionary<string, string> publishConfig = new Dictionary<string, string>
            {
                { Constants.FunctionAppKeyPublish, functionApp }
            };
            FileSystemHelpers.WriteAllTextToFile(Constants.PublishConfigurationFileName, JsonConvert.SerializeObject(publishConfig));
            // TODO: Write it to .funcignore and .gitignore
        }

        private async Task<string> FindValidNameAndCreatAzureResource(Func<string, Task<bool>> isResourceTakenFunc, Func<string, Task> createResourceFunc, string resourceName)
        {
            var localFunctionName = new DirectoryInfo(Environment.CurrentDirectory).Name;
            // Could probably be less restrictive here, but for now only limiting names to characters and numbers
            var validNameRegex = new Regex("[^a-zA-Z0-9]");
            var localFunctionValidName = validNameRegex.Replace(localFunctionName, "");
            Random random = new Random();
            ColoredConsole.WriteLine($"Looking for a valid name to create a new Azure {resourceName}...");
            while (localFunctionValidName.Length < 2 || await isResourceTakenFunc(localFunctionValidName))
            {
                localFunctionValidName = GetPossibleResourceName(random, validNameRegex.Replace(localFunctionName, ""));
            }
            ColoredConsole.WriteLine($"Creating an Azure {resourceName} with name \'{localFunctionValidName}\'...{Environment.NewLine}");
            await createResourceFunc(localFunctionValidName);
            return localFunctionValidName;
        }

        private async Task<string> CreateAzureResourceGroup()
        {
            async Task<bool> isResourceTakenFunc(string name) {
                return await AzureHelper.CheckIfResourceGroupAlreadyExists(name);
            }
            async Task createResourceFunc(string name)
            {
                await AzureHelper.CreateResourceGroup(Location, name);
            }
            return await FindValidNameAndCreatAzureResource(isResourceTakenFunc, createResourceFunc, "Resource Group");
        }

        private async Task<string> CreateAzureStorage(string resourceGroup)
        {
            async Task<bool> isResourceTakenFunc(string name)
            {
                return await AzureHelper.IsStorageAccountNameTaken(name);
            }
            async Task createResourceFunc(string name)
            {
                await AzureHelper.CreateAzureStorage(name, resourceGroup);
            }
            return await FindValidNameAndCreatAzureResource(isResourceTakenFunc, createResourceFunc, "Storage Account");
        }

        private async Task<string> CreateAzureFunctionApp(string resourceGroup, string storageAccount)
        {
            async Task<bool> isResourceTakenFunc(string name)
            {
                return await AzureHelper.CheckIfFunctionAppAlreadyExists(name, AccessToken);
            }
            async Task createResourceFunc(string name)
            {
                var workerRuntime = string.IsNullOrEmpty(WorkerRuntime)
                    ? WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager)
                    : WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(WorkerRuntime);

                var os = workerRuntime == Helpers.WorkerRuntime.python ? "linux" : "windows";
                await AzureHelper.CreateAzureFunction(resourceGroup, storageAccount, name, os, workerRuntime.ToString(), Location);
            }
            return await FindValidNameAndCreatAzureResource(isResourceTakenFunc, createResourceFunc, "Function App");
        }

        private async Task<string> CreateNewLocalProject()
        {
            ColoredConsole.WriteLine("Creating a new local Functions project...");
            InitAction initAction = new InitAction(_templatesManager, SourceControl.Git, WorkerRuntime, Force, initDocker: false, csx: false);
            await initAction.RunAsync();
            ColoredConsole.WriteLine("Project created.");
            return Environment.CurrentDirectory;
        }

        private async Task CreateNewFunction()
        {
            ColoredConsole.WriteLine("Creating a new local Function...");
            CreateFunctionAction createAction = new CreateFunctionAction(_templatesManager, _secretsManager, language: null, templateName: null, functionName: null, csx: false);
            await createAction.RunAsync();
        }

        private async Task PublishLocalProject(string functionApp)
        {
            ColoredConsole.WriteLine("Publishing your local Function...");
            PublishFunctionAppAction publishFunctionAppAction = new PublishFunctionAppAction(_settings, _secretsManager, functionApp, AccessToken);
            await publishFunctionAppAction.RunAsync();
        }
    }
}
