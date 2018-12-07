using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Extensions;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new function from a template.")]
    [Action(Name = "new", HelpText = "Create a new function from a template.")]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new function from a template.")]
    internal class CreateFunctionAction : BaseAction
    {
        private readonly ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;
        private readonly ISettings _settings;

        public string Language { get; set; }
        public string TemplateName { get; set; }
        public string FunctionName { get; set; }
        public bool Csx { get; set; }

        public CreateFunctionAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, ISettings settings)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
            _settings = settings;
        }

        private async Task<(bool, string)> CallAzCommandAsync(string commandName)
        {
            var windowscmd = new StringBuilder();
            windowscmd.Append("/c az ").Append(commandName);

            var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new Executable("cmd", windowscmd.ToString())
                        : new Executable("az", commandName);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
            if (exitCode == 0)
            {
                return (true, (stdout.ToString().Trim(' ', '\n', '\r', '"')));
            }
            else
            {
                return (false, (stderr.ToString().Trim(' ', '\n', '\r'))); // "Unable to connect to Azure. Make sure you have the `az` CLI installed and logged in and try again";
            }
        }

        private async Task CreateWebJobsStorageKeyAsync()
        {
            ColoredConsole.Write("Please enter the name of your resource group for your storage account: ");
            string resource_group = Console.ReadLine();
            
            ColoredConsole.Write("Please enter the name of your storage account: ");
            string storage_account = Console.ReadLine();

            //verify if the resource group and account exists and get the details
            //az storage account show -g MyResourceGroup -n MyStorageAccount
            var command = new StringBuilder();
            command.Append("storage account keys list -g ").Append(resource_group).Append(" -n ").Append(storage_account).Append(" --output json");
            (bool success, string keys) = await CallAzCommandAsync(command.ToString());

            if (success)
            {
                JArray json_keys = JArray.Parse(keys);
                string account_key = json_keys[0].Value<string>("value");
                //"{ACCOUNT_NAME}_STORAGE": "DefaultEndpointsProtocol=https;AccountName={ACCOUNT_NAME};AccountKey={ACCOUNT_KEY}",
                var storage_value = new StringBuilder();
                storage_value.Append("DefaultEndpointsProtocol=https;AccountName=").Append(storage_account).Append(";AccountKey=").Append(account_key);
                _secretsManager.SetSecret("AzureWebJobsStorage", storage_value.ToString());
            }
            else
            {
                ColoredConsole.WriteLine(VerboseColor($"Unable to fetch key token from az cli. Error: {keys}"));
            }            
        }

        private async Task CheckWebJobsStorageKeyAsync(string chosenTemplateName)
        {
            List<string> templatesNeedWebJobsStorage = new List<string>();
            templatesNeedWebJobsStorage.Add("Azure Blob Storage Trigger");
            //add the other templates that need web jobs storage here

            //only worry about making the user have a webjobsstorage if the trigger requires it
            if (templatesNeedWebJobsStorage.Contains(chosenTemplateName))
            {
                //check in the secretes to determine if they already have a value in the key - the default is {AzureWebJobsStorage}
                if (_secretsManager.GetSecrets()["AzureWebJobsStorage"] == "{AzureWebJobsStorage}")
                {
                    ColoredConsole.WriteLine(ErrorColor("Please make sure that the AzureWebJobsStorage key in your local.settings.json contains a valid value eg. DefaultEndpointsProtocol=https;AccountName=[name];AccountKey=[key]"))
                                   .WriteLine(ErrorColor("Go to https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings to see documentation"));

                    ColoredConsole.Write("Would you like to add your storage account key here now? (y/n): ");
                    string answer = Console.ReadLine();

                    //give the user the option to add a web jobs storage now
                    if (answer == "y")
                    {
                        await CreateWebJobsStorageKeyAsync();
                    }
                }
            }
        }



        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('l', "language")
                .WithDescription($"Template programming language, such as C#, F#, JavaScript, etc.")
                .Callback(l => Language = l);

            Parser
                .Setup<string>('t', "template")
                .WithDescription("Template name")
                .Callback(t => TemplateName = t);

            Parser
                .Setup<string>('n', "name")
                .WithDescription("Function name")
                .Callback(n => FunctionName = n);

            Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => Csx = csx);
            return Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                if (string.IsNullOrEmpty(TemplateName) ||
                    string.IsNullOrEmpty(FunctionName))
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor("Running with stdin\\stdout redirected. Command must specify --template, and --name explicitly."))
                        .WriteLine(ErrorColor("See 'func help function' for more details"));
                    return;
                }
            }

            var templates = await _templatesManager.Templates;
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);

            if (workerRuntime != WorkerRuntime.None && !string.IsNullOrWhiteSpace(Language))
            {
                // validate
                var language = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);
                if (workerRuntime != language)
                {
                    throw new CliException("Selected language doesn't match worker set in local.settings.json." +
                        $"Selected worker is: {workerRuntime} and selected language is: {language}");
                }
            }
            else if (string.IsNullOrWhiteSpace(Language))
            {
                if (workerRuntime == WorkerRuntime.None)
                {
                    ColoredConsole.Write("Select a language: ");
                    Language = SelectionMenuHelper.DisplaySelectionWizard(templates.Select(t => t.Metadata.Language).Where(l => !l.Equals("python", StringComparison.OrdinalIgnoreCase)).Distinct());
                    workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
                }
                else if (workerRuntime != WorkerRuntime.dotnet || Csx)
                {
                    var languages = WorkerRuntimeLanguageHelper.LanguagesForWorker(workerRuntime);
                    var displayList = templates
                            .Select(t => t.Metadata.Language)
                            .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                            .Distinct()
                            .ToArray();
                    if (displayList.Length == 1)
                    {
                        Language = displayList.First();
                    }
                    else
                    {
                        ColoredConsole.Write("Select a language: ");
                        Language = SelectionMenuHelper.DisplaySelectionWizard(displayList);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(Language))
            {
                workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
            }

            if (workerRuntime == WorkerRuntime.dotnet && !Csx)
            {
                ColoredConsole.Write("Select a template: ");
                TemplateName = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(DotnetHelpers.GetTemplates());
                ColoredConsole.Write("Function name: ");
                FunctionName = FunctionName ?? Console.ReadLine();
                ColoredConsole.WriteLine(FunctionName);
                var namespaceStr = Path.GetFileName(Environment.CurrentDirectory);
                await DotnetHelpers.DeployDotnetFunction(TemplateName.Replace(" ", string.Empty), Utilities.SanitizeClassName(FunctionName), Utilities.SanitizeNameSpace(namespaceStr));
            }
            else
            {
                ColoredConsole.Write("Select a template: ");
                var templateLanguage = WorkerRuntimeLanguageHelper.GetTemplateLanguageFromWorker(workerRuntime);
                TemplateName = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(templates.Where(t => t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase)).Select(t => t.Metadata.Name).Distinct());
                ColoredConsole.WriteLine(TitleColor(TemplateName));
                var template = templates.FirstOrDefault(t => Utilities.EqualsIgnoreCaseAndSpace(t.Metadata.Name, TemplateName) && t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));

                if (template == null)
                {
                    throw new CliException($"Can't find template \"{TemplateName}\" in \"{Language}\"");
                }
                else
                {
                    ExtensionsHelper.EnsureDotNetForExtensions(template);
                    ColoredConsole.Write($"Function name: [{template.Metadata.DefaultFunctionName}] ");
                    FunctionName = FunctionName ?? Console.ReadLine();
                    FunctionName = string.IsNullOrEmpty(FunctionName) ? template.Metadata.DefaultFunctionName : FunctionName;
                    await _templatesManager.Deploy(FunctionName, template);
                }
            }
            await CheckWebJobsStorageKeyAsync(TemplateName);
            ColoredConsole.WriteLine($"The function \"{FunctionName}\" was created successfully from the \"{TemplateName}\" template.");
        }
    }
}
