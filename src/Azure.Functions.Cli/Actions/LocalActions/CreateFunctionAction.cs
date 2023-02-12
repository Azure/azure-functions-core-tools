using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Storage.Blobs.Models;
using Colors.Net;
using DurableTask.Core;
using Fclp;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.Constants;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new function from a template.")]
    [Action(Name = "new", HelpText = "Create a new function from a template.")]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new function from a template.")]
    internal class CreateFunctionAction : BaseAction
    {
        private ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IContextHelpManager _contextHelpManager;

        private readonly InitAction _initAction;
        public WorkerRuntime workerRuntime;

        public string Language { get; set; }
        public string TemplateName { get; set; }
        public string FunctionName { get; set; }
        public bool Csx { get; set; }
        private string TriggerNameForHelp { get; set; }
        private string FileName { get; set; }
        public AuthorizationLevel? AuthorizationLevel { get; set; }

        Lazy<IEnumerable<Template>> _templates;


        public CreateFunctionAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, IContextHelpManager contextHelpManager)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
            _contextHelpManager = contextHelpManager;
            _initAction = new InitAction(_templatesManager, _secretsManager);
            _templates = new Lazy<IEnumerable<Template>>(() => { return _templatesManager.Templates.Result; });
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
                .Setup<string>('f', "file")
                .WithDescription("File Name")
                .Callback(f => FileName = f);

            Parser
                .Setup<AuthorizationLevel?>('a', "authlevel")
                .WithDescription("Authorization level is applicable to templates that use Http trigger, Allowed values: [function, anonymous, admin]. Authorization level is not enforced when running functions from core tools")
                .Callback(a => AuthorizationLevel = a);

            Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => Csx = csx);

            _initAction.ParseArgs(args);

            ParseTriggerForHelpRequest(args);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            if (!ValidateInputs())
            {
                return;
            }

            await UpdateLanguageAndRuntime();

            // Check if the new command only ran for help. 
            if (await ProcessHelpRequest(TriggerNameForHelp, true))
            {
                return;
            }

            if (WorkerRuntimeLanguageHelper.IsDotnet(workerRuntime) && !Csx)
            {
                SelectionMenuHelper.DisplaySelectionWizardPrompt("template");
                TemplateName = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(DotnetHelpers.GetTemplates(workerRuntime));
                ColoredConsole.Write("Function name: ");
                FunctionName = FunctionName ?? Console.ReadLine();
                ColoredConsole.WriteLine(FunctionName);
                var namespaceStr = Path.GetFileName(Environment.CurrentDirectory);
                await DotnetHelpers.DeployDotnetFunction(TemplateName.Replace(" ", string.Empty), Utilities.SanitizeClassName(FunctionName), Utilities.SanitizeNameSpace(namespaceStr), Language.Replace("-isolated", ""), workerRuntime, AuthorizationLevel);
            }
            else
            {
                SelectionMenuHelper.DisplaySelectionWizardPrompt("template");
                string templateLanguage;
                try
                {
                    templateLanguage = WorkerRuntimeLanguageHelper.NormalizeLanguage(Language);
                }
                catch (Exception)
                {
                    // Ideally this should never happen.
                    templateLanguage = WorkerRuntimeLanguageHelper.GetDefaultTemplateLanguageFromWorker(workerRuntime);
                }

                TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "language", templateLanguage);
                TemplateName = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(GetTriggerNames(templateLanguage));
                ColoredConsole.WriteLine(TitleColor(TemplateName));

                Template template = GetLanguageTemplates(templateLanguage).FirstOrDefault(t => Utilities.EqualsIgnoreCaseAndSpace(t.Metadata.Name, TemplateName));

                if (template == null)
                {
                    TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "template", "N/A");
                    throw new CliException($"Can't find template \"{TemplateName}\" in \"{Language}\"");
                }
                else
                {
                    TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "template", TemplateName);

                    var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
                    if (template.Metadata.Extensions != null && !extensionBundleManager.IsExtensionBundleConfigured() && !CommandChecker.CommandExists("dotnet"))
                    {
                        throw new CliException($"The {template.Metadata.Name} template has extensions. {Constants.Errors.ExtensionsNeedDotnet}");
                    }

                    if (AuthorizationLevel.HasValue)
                    {
                        ConfigureAuthorizationLevel(template);
                    }

                    ColoredConsole.Write($"Function name: [{template.Metadata.DefaultFunctionName}] ");
                    FunctionName = FunctionName ?? Console.ReadLine();
                    FunctionName = string.IsNullOrEmpty(FunctionName) ? template.Metadata.DefaultFunctionName : FunctionName;
                    await _templatesManager.Deploy(FunctionName, FileName, template);
                    PerformPostDeployTasks(FunctionName, Language);
                }
            }
            ColoredConsole.WriteLine($"The function \"{FunctionName}\" was created successfully from the \"{TemplateName}\" template.");
            if (string.Equals(Language, Languages.Python, StringComparison.CurrentCultureIgnoreCase) && !IsNewPythonProgrammingModel())
            {
                PythonHelpers.PrintPySteinAwarenessMessage();
            }

            var isNewNodeJsModel = IsNewNodeJsProgrammingModel(workerRuntime);
            if (workerRuntime == WorkerRuntime.node && !isNewNodeJsModel)
            {
                NodeJSHelpers.PrintV4AwarenessMessage();
            }
        }

        public bool ValidateInputs()
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
                    return false;
                }
            }

            return true;
        }

        public async Task UpdateLanguageAndRuntime()
        {
            workerRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            if (!FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, "local.settings.json")))
            {
                // we're assuming "func init" has not been run
                await _initAction.RunAsync();
                workerRuntime = _initAction.ResolvedWorkerRuntime;
                Language = _initAction.ResolvedLanguage;
            }


            if (workerRuntime != WorkerRuntime.None && !string.IsNullOrWhiteSpace(Language))
            {
                // validate
                var workerRuntimeSelected = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);
                if (workerRuntime != workerRuntimeSelected)
                {
                    throw new CliException("Selected language doesn't match worker set in local.settings.json." +
                        $"Selected worker is: {workerRuntime} and selected language is: {workerRuntimeSelected}");
                }
            }
            else if (string.IsNullOrWhiteSpace(Language))
            {
                if (workerRuntime == WorkerRuntime.None)
                {
                    SelectionMenuHelper.DisplaySelectionWizardPrompt("language");
                    Language = SelectionMenuHelper.DisplaySelectionWizard(_templates.Value.Select(t => t.Metadata.Language).Where(l => !l.Equals("python", StringComparison.OrdinalIgnoreCase)).Distinct());
                    workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
                }
                else if (!WorkerRuntimeLanguageHelper.IsDotnet(workerRuntime) || Csx)
                {
                    var languages = WorkerRuntimeLanguageHelper.LanguagesForWorker(workerRuntime);
                    var displayList = _templates.Value
                            .Select(t => t.Metadata.Language)
                            .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    if (displayList.Length == 1)
                    {
                        Language = displayList.First();
                    }
                    else if (!InferAndUpdateLanguage(workerRuntime))
                    {
                        SelectionMenuHelper.DisplaySelectionWizardPrompt("language");
                        Language = SelectionMenuHelper.DisplaySelectionWizard(displayList);
                    }
                }
                else if (WorkerRuntimeLanguageHelper.IsDotnet(workerRuntime))
                {
                    InferAndUpdateLanguage(workerRuntime);
                }
            }
            else if (!string.IsNullOrWhiteSpace(Language))
            {
                workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
            }
        }
        private IEnumerable<string> GetTriggerNames(string templateLanguage)
        {
            return GetLanguageTemplates(templateLanguage).Select(t => t.Metadata.Name).Distinct();
        }

        private IEnumerable<Template> GetLanguageTemplates(string templateLanguage)
        {
            if (IsNewNodeJsProgrammingModel(workerRuntime))
            {
                return _templates.Value.Where(t => t.Id.EndsWith("-4.x") && t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
            }
            
            if (IsNewPythonProgrammingModel()) 
            { 
                return _templates.Value.Where(t => t.Metadata.ProgrammingModel && t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
            }

            return _templates.Value.Where(t => t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
        }

        private void ConfigureAuthorizationLevel(Template template)
        {
            var bindings = template.Function["bindings"];
            bool IsHttpTriggerTemplate = bindings.Any(b => b["type"].ToString() == "httpTrigger");

            if (!IsHttpTriggerTemplate)
            {
                throw new CliException(AuthLevelErrorMessage);
            }
            else
            {
                var binding = bindings.Where(b => b["type"].ToString().Equals(HttpTriggerTemplateName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                binding["authLevel"] = AuthorizationLevel.ToString();
            }
        }

        private bool InferAndUpdateLanguage(WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.dotnet:
                    // use fsproj as an indication that we have a F# project
                    Language = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").Any() ? Constants.Languages.FSharp : Constants.Languages.CSharp;
                    return true;
                case WorkerRuntime.dotnetIsolated:
                    // use fsproj as an indication that we have a F# project
                    Language = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").Any() ? Constants.Languages.FSharpIsolated : Constants.Languages.CSharpIsolated;
                    return true;
                case WorkerRuntime.node:
                    // use tsconfig.json as an indicator that we have a TypeScript project
                    Language = FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, "tsconfig.json")) ? Constants.Languages.TypeScript : Constants.Languages.JavaScript;
                    return true;
                case WorkerRuntime.None:
                case WorkerRuntime.python:
                case WorkerRuntime.java:
                case WorkerRuntime.powershell:
                case WorkerRuntime.custom:
                default:
                    return false;
            }
        }

        private void PerformPostDeployTasks(string functionName, string language)
        {
            if (language == Constants.Languages.TypeScript)
            {
                // Update typescript function.json
                var funcJsonFile = Path.Combine(Environment.CurrentDirectory, functionName, Constants.FunctionJsonFileName);
                var jsonStr = FileSystemHelpers.ReadAllTextFromFile(funcJsonFile);
                var funcObj = JsonConvert.DeserializeObject<JObject>(jsonStr);
                funcObj.Add("scriptFile", $"../dist/{functionName}/index.js");
                FileSystemHelpers.WriteAllTextToFile(funcJsonFile, JsonConvert.SerializeObject(funcObj, Formatting.Indented));
            }
        }

        private void ParseTriggerForHelpRequest(string[] args)
        {
            if (args.Length != 2)
            {
                return;
            }

            var inputTriggerName = args[0];
            var inputHelp = args[1];
            if (HelpCommand.Equals(inputHelp, StringComparison.OrdinalIgnoreCase))
            {
                TriggerNameForHelp = inputTriggerName;
            }
        }

        public async Task<bool> ProcessHelpRequest(string triggerName, bool suggestCorrectTriggerNames = false)
        {
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return false;
            }

            var language = Language;
            if (string.IsNullOrEmpty(language))
            {
                await UpdateLanguageAndRuntime();
            }

            if (suggestCorrectTriggerNames)
            {
                if (!IsValidTriggerName(language, triggerName))
                {
                    ColoredConsole.WriteLine(ErrorColor($"The trigger name '{TriggerNameForHelp}' is not valid for {language} language. "));
                    SelectionMenuHelper.DisplaySelectionWizardPrompt("valid trigger");
                    triggerName = SelectionMenuHelper.DisplaySelectionWizard(GetTriggerNames(language));
                }
            }

            if ((IsNewPythonProgrammingModel() || IsNewNodeJsProgrammingModel(workerRuntime)) && IsValidTriggerName(language, triggerName))
            {
                ColoredConsole.Write(AdditionalInfoColor(_contextHelpManager.GetTriggerHelp(triggerName, language).Result));
                return true;
            }

            return false;
        }

        private bool IsValidTriggerName(string language, string triggerName)
        {
            return GetTriggerNames(language).Any(x => x.Equals(triggerName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsNewPythonProgrammingModel()
        {
            return PythonHelpers.IsNewPythonProgrammingModel(Language);
        }

        private bool IsNewNodeJsProgrammingModel(WorkerRuntime workerRuntime)
        {
            try
            {
                if (workerRuntime == WorkerRuntime.node)
                {
                    if (FileSystemHelpers.FileExists(Constants.PackageJsonFileName))
                    {
                        var packageJsonData = FileSystemHelpers.ReadAllTextFromFile(Constants.PackageJsonFileName);
                        var packageJson = JsonConvert.DeserializeObject<JToken>(packageJsonData);
                        var funcPackageVersion = packageJson["dependencies"]["@azure/functions"];
                        if (new Regex("^[^0-9]*4").IsMatch(funcPackageVersion.ToString()))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore and assume "false"
            }

            return false;
        }
    }
}
