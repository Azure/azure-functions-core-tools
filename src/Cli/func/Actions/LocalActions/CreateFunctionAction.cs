// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.Constants;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", HelpText = "Create a new Function from a template.", HelpOrder = 2)]
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new Function from a template.", HelpOrder = 1)]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new Function from a template.", HelpOrder = 2)]
    internal class CreateFunctionAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;
        private readonly IContextHelpManager _contextHelpManager;
        private readonly IUserInputHandler _userInputHandler;
        private readonly InitAction _initAction;
        private readonly ITemplatesManager _templatesManager;
        private IEnumerable<Template> _templates;
        private IEnumerable<NewTemplate> _newTemplates;
        private IEnumerable<UserPrompt> _userPrompts;
        private WorkerRuntime _workerRuntime;

        public CreateFunctionAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, IContextHelpManager contextHelpManager, IEnumerable<IConfigurationProfile> configurationProfiles)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
            _contextHelpManager = contextHelpManager;

            // Construct InitAction with the provided providers so it can validate and apply the profile
            _initAction = new InitAction(_templatesManager, _secretsManager, configurationProfiles);
            _userInputHandler = new UserInputHandler(_templatesManager);
        }

        public string Language { get; set; }

        public string TemplateName { get; set; }

        public string FunctionName { get; set; }

        public bool Csx { get; set; }

        private string TriggerNameForHelp { get; set; }

        private string FileName { get; set; }

        public AuthorizationLevel? AuthorizationLevel { get; set; }

        public WorkerRuntime WorkerRuntime => _workerRuntime;

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

        public override async Task RunAsync()
        {
            // Check if the command only ran for help.
            if (!string.IsNullOrEmpty(TriggerNameForHelp))
            {
                await ProcessHelpRequest(TriggerNameForHelp, true);
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            // Resolve worker runtime early to check for Java before any prompts
            _workerRuntime = await ResolveWorkerRuntimeAsync();

            // Check if Java runtime is being used
            if (_workerRuntime == WorkerRuntime.Java)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("The 'func new' command is not supported for Java runtime."))
                    .WriteLine(ErrorColor("Please use Maven to create Java Azure Functions."))
                    .WriteLine(ErrorColor("For more information, visit: https://learn.microsoft.com/azure/azure-functions/functions-develop-local?pivots=programming-language-java"));
                return;
            }

            // Load templates and resolve language
            if (NeedsToLoadExtensionTemplates(_workerRuntime, Csx))
            {
                _templates = await _templatesManager.Templates;
            }

            ResolveLanguageAsync(_workerRuntime);

            if (IsNewPythonProgrammingModel())
            {
                _newTemplates = await _templatesManager.NewTemplates;
                _userPrompts = await _templatesManager.UserPrompts;
            }

            if (WorkerRuntimeLanguageHelper.IsDotnet(_workerRuntime) && !Csx)
            {
                if (string.IsNullOrWhiteSpace(TemplateName))
                {
                    SelectionMenuHelper.DisplaySelectionWizardPrompt("template");
                    TemplateName ??= SelectionMenuHelper.DisplaySelectionWizard(DotnetHelpers.GetTemplates(_workerRuntime, Language));
                }
                else
                {
                    ColoredConsole.WriteLine($"Template: {TemplateName}");
                }

                ColoredConsole.Write($"Function name: [{TemplateName}] ");
                FunctionName = FunctionName ?? Console.ReadLine();
                FunctionName = string.IsNullOrEmpty(FunctionName) ? TemplateName : FunctionName;
                ColoredConsole.WriteLine(FunctionName);
                var namespaceStr = Path.GetFileName(Environment.CurrentDirectory);
                await DotnetHelpers.DeployDotnetFunction(TemplateName.Replace(" ", string.Empty), Utilities.SanitizeClassName(FunctionName), Utilities.SanitizeNameSpace(namespaceStr), Language.Replace("-isolated", string.Empty), _workerRuntime, AuthorizationLevel);
            }
            else if (IsNewPythonProgrammingModel())
            {
                if (string.IsNullOrEmpty(TemplateName))
                {
                    SelectionMenuHelper.DisplaySelectionWizardPrompt("template");
                    TemplateName = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(GetTriggerNamesFromNewTemplates(Language));
                }

                // Defaulting the filename to "function_app.py" if the file name is not provided.
                if (string.IsNullOrWhiteSpace(FileName))
                {
                    FileName = "function_app.py";
                }

                var userPrompt = _userPrompts.First(x => string.Equals(x.Id, "app-selectedFileName", StringComparison.OrdinalIgnoreCase));
                while (!_userInputHandler.ValidateResponse(userPrompt, FileName))
                {
                    _userInputHandler.PrintInputLabel(userPrompt, PySteinFunctionAppPy);
                    FileName = Console.ReadLine();
                    if (string.IsNullOrEmpty(FileName))
                    {
                        FileName = PySteinFunctionAppPy;
                    }
                }

                var providedInputs = new Dictionary<string, string>()
                {
                    { GetFunctionNameParamId, FunctionName },
                    { HttpTriggerAuthLevelParamId, AuthorizationLevel?.ToString().ToUpperInvariant() }
                };

                var jobName = "appendToFile";
                if (FileName != PySteinFunctionAppPy)
                {
                    var filePath = Path.Combine(Environment.CurrentDirectory, FileName);
                    if (FileUtility.FileExists(filePath))
                    {
                        jobName = "AppendToBlueprint";
                        providedInputs[GetBluePrintExistingFileNameParamId] = FileName;
                    }
                    else
                    {
                        jobName = "CreateNewBlueprint";
                        providedInputs[GetBluePrintFileNameParamId] = FileName;
                    }
                }
                else
                {
                    providedInputs[GetFileNameParamId] = FileName;
                }

                var template = _newTemplates.FirstOrDefault(t => string.Equals(t.Name, TemplateName, StringComparison.CurrentCultureIgnoreCase) && string.Equals(t.Language, Language, StringComparison.CurrentCultureIgnoreCase));

                if (template is null)
                {
                    throw new CliException($"Can't find template \"{TemplateName}\" in \"{Language}\"");
                }

                var templateJob = template.Jobs.Single(x => x.Type.Equals(jobName, StringComparison.OrdinalIgnoreCase));

                var variables = new Dictionary<string, string>();
                _userInputHandler.RunUserInputActions(providedInputs, templateJob.Inputs, variables);

                if (string.IsNullOrEmpty(FunctionName))
                {
                    FunctionName = providedInputs[GetFunctionNameParamId];
                }

                await _templatesManager.Deploy(templateJob, template, variables);
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
                    templateLanguage = WorkerRuntimeLanguageHelper.GetDefaultTemplateLanguageFromWorker(_workerRuntime);
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

                    if (!IsNewNodeJsProgrammingModel(_workerRuntime) && AuthorizationLevel.HasValue)
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

            var isNewNodeJsModel = IsNewNodeJsProgrammingModel(_workerRuntime);
            if (_workerRuntime == WorkerRuntime.Node && !isNewNodeJsModel)
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
            _workerRuntime = await ResolveWorkerRuntimeAsync();

            // Only load templates when we need them and before we try to resolve language
            if (NeedsToLoadExtensionTemplates(_workerRuntime, Csx))
            {
                _templates = await _templatesManager.Templates;
            }

            ResolveLanguageAsync(_workerRuntime);
        }

        private static bool NeedsToLoadExtensionTemplates(WorkerRuntime worker, bool csx)
        {
            // Templates are needed if:
            //  - Worker is unknown and we need the list to ask user (non-dotnet path)
            //  - Worker is non-dotnet (Node, Python, PowerShell, Java, etc.)
            //  - Worker is dotnet **but** we're in CSX mode
            //  - Language is unknown and worker is non-dotnet (requires menu from templates)
            if (worker == WorkerRuntime.None)
            {
                return true;
            }

            if (WorkerRuntimeLanguageHelper.IsDotnet(worker))
            {
                return csx;
            }

            return true;
        }

        private async Task<WorkerRuntime> ResolveWorkerRuntimeAsync()
        {
            if (!CurrentPathHasLocalSettings())
            {
                // we're assuming "func init" has not been run
                await _initAction.RunAsync();
                Language = _initAction.ResolvedLanguage;
                return _initAction.ResolvedWorkerRuntime;
            }

            return GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
        }

        private void ResolveLanguageAsync(WorkerRuntime runtime)
        {
            // If Language was provided, align runtime
            if (!string.IsNullOrWhiteSpace(Language))
            {
                if (runtime == WorkerRuntime.None)
                {
                    _workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
                    return;
                }

                var selected = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);
                if (runtime != selected)
                {
                    throw new CliException(
                        "Selected language doesn't match worker set in local.settings.json." +
                        $" Selected worker is: {runtime} and selected language is: {selected}");
                }

                return;
            }

            // .NET SDK-style: infer from project (C#, F#, VB) -> no templates required
            if (WorkerRuntimeLanguageHelper.IsDotnet(runtime) && !Csx)
            {
                InferAndUpdateLanguage(runtime);
                return;
            }

            if (WorkerRuntimeLanguageHelper.IsDotnet(runtime) && Csx)
            {
                // Filter display list to CSX-capable languages (likely just "C#")
                var display = _templates.Select(t => t.Metadata.Language)
                                        .Where(l => WorkerRuntimeLanguageHelper
                                            .LanguagesForWorker(runtime)
                                            .Contains(l, StringComparer.OrdinalIgnoreCase))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToArray();

                Language = display?.Length == 1 ? display[0]
                         : InferAndUpdateLanguage(runtime) ? Language
                         : SelectionMenuHelper.DisplaySelectionWizard(display);
                return;
            }

            // Non-.NET
            var languages = WorkerRuntimeLanguageHelper.LanguagesForWorker(runtime);
            var displayList = _templates.Select(t => t.Metadata.Language)
                                        .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToArray();

            if (displayList?.Length == 1)
            {
                Language = displayList.First();
            }
            else if (!InferAndUpdateLanguage(_workerRuntime))
            {
                SelectionMenuHelper.DisplaySelectionWizardPrompt("language");
                Language = SelectionMenuHelper.DisplaySelectionWizard(displayList);
            }
        }

        private IEnumerable<string> GetTriggerNames(string templateLanguage, bool forNewModelHelp = false)
        {
            return GetLanguageTemplates(templateLanguage, forNewModelHelp).Select(t => t.Metadata.Name).Distinct();
        }

        private IEnumerable<Template> GetLanguageTemplates(string templateLanguage, bool forNewModelHelp = false)
        {
            if (IsNewNodeJsProgrammingModel(_workerRuntime) ||
                (forNewModelHelp && (Languages.TypeScript.EqualsIgnoreCase(templateLanguage) || Languages.JavaScript.EqualsIgnoreCase(templateLanguage))))
            {
                return _templates.Where(t => t.Id.EndsWith("-4.x") && t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
            }
            else if (_workerRuntime == WorkerRuntime.Node)
            {
                // Ensuring that we only show v3 templates for node when the user has not opted into the new model
                return _templates.Where(t => !t.Id.EndsWith("-4.x") && t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
            }

            return _templates.Where(t => t.Metadata.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<string> GetTriggerNamesFromNewTemplates(string templateLanguage, bool forNewModelHelp = false)
        {
            return GetNewTemplates(templateLanguage, forNewModelHelp).Select(t => t.Name).Distinct();
        }

        private IEnumerable<NewTemplate> GetNewTemplates(string templateLanguage, bool forNewModelHelp = false)
        {
            if (IsNewPythonProgrammingModel() || (Languages.Python.EqualsIgnoreCase(templateLanguage) && forNewModelHelp))
            {
                return _newTemplates.Where(t => t.Language.Equals(templateLanguage, StringComparison.OrdinalIgnoreCase));
            }

            throw new CliException("The new version of templates are only supported for Python.");
        }

        private void ConfigureAuthorizationLevel(Template template)
        {
            var bindings = template.Function["bindings"];
            bool isHttpTriggerTemplate = bindings.Any(b => b["type"].ToString() == "httpTrigger");

            if (!isHttpTriggerTemplate)
            {
                throw new CliException(AuthLevelErrorMessage);
            }
            else
            {
                var binding = bindings.Where(b => b["type"].ToString().Equals(HttpTriggerTemplateName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                binding["authLevel"] = AuthorizationLevel.ToString().ToLowerInvariant();
            }
        }

        private bool InferAndUpdateLanguage(WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.Dotnet:
                    // use fsproj as an indication that we have a F# project
                    Language = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").Any() ? Constants.Languages.FSharp : Constants.Languages.CSharp;
                    return true;
                case WorkerRuntime.DotnetIsolated:
                    // use fsproj as an indication that we have a F# project
                    Language = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").Any() ? Constants.Languages.FSharp : Constants.Languages.CSharp;
                    return true;
                case WorkerRuntime.Node:
                    // use tsconfig.json as an indicator that we have a TypeScript project
                    Language = FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, "tsconfig.json")) ? Constants.Languages.TypeScript : Constants.Languages.JavaScript;
                    return true;
                case WorkerRuntime.None:
                case WorkerRuntime.Python:
                case WorkerRuntime.Java:
                case WorkerRuntime.Powershell:
                case WorkerRuntime.Custom:
                default:
                    return false;
            }
        }

        private void PerformPostDeployTasks(string functionName, string language)
        {
            if (language == Languages.TypeScript && !IsNewNodeJsProgrammingModel(_workerRuntime))
            {
                // Update typescript function.json
                var funcJsonFile = Path.Combine(Environment.CurrentDirectory, functionName, FunctionJsonFileName);
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

        public async Task<bool> ProcessHelpRequest(string triggerName, bool promptQuestions = false)
        {
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return false;
            }

            var supportedLanguages = new List<string>() { Languages.JavaScript, Languages.TypeScript, Languages.Python };
            if (string.IsNullOrEmpty(Language))
            {
                if (CurrentPathHasLocalSettings())
                {
                    await UpdateLanguageAndRuntime();
                }

                if (string.IsNullOrEmpty(Language) || !supportedLanguages.Contains(Language, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!promptQuestions)
                    {
                        return false;
                    }

                    SelectionMenuHelper.DisplaySelectionWizardPrompt("language");
                    Language = SelectionMenuHelper.DisplaySelectionWizard(supportedLanguages);
                }
            }

            IEnumerable<string> triggerNames;
            if (Languages.Python.EqualsIgnoreCase(Language))
            {
                triggerNames = GetTriggerNamesFromNewTemplates(Language, forNewModelHelp: true);
            }
            else
            {
                triggerNames = GetTriggerNames(Language, forNewModelHelp: true);
            }

            await _contextHelpManager.LoadTriggerHelp(Language, triggerNames.ToList());

            if (_contextHelpManager.IsValidTriggerNameForHelp(triggerName))
            {
                triggerName = _contextHelpManager.GetTriggerTypeFromTriggerNameForHelp(triggerName);
            }

            if (promptQuestions && !_contextHelpManager.IsValidTriggerTypeForHelp(triggerName))
            {
                ColoredConsole.WriteLine(ErrorColor($"The trigger name '{TriggerNameForHelp}' is not valid for {Language} language. "));
                SelectionMenuHelper.DisplaySelectionWizardPrompt("valid trigger");
                triggerName = SelectionMenuHelper.DisplaySelectionWizard(triggerNames);
                triggerName = _contextHelpManager.GetTriggerTypeFromTriggerNameForHelp(triggerName);
            }

            if (_contextHelpManager.IsValidTriggerTypeForHelp(triggerName))
            {
                ColoredConsole.Write(AdditionalInfoColor($"{Environment.NewLine}{_contextHelpManager.GetTriggerHelp(triggerName, Language)}"));
                return true;
            }

            return false;
        }

        private bool IsNewPythonProgrammingModel()
        {
            return PythonHelpers.IsNewPythonProgrammingModel(Language);
        }

        private bool IsNewNodeJsProgrammingModel(WorkerRuntime workerRuntime)
        {
            try
            {
                if (workerRuntime == WorkerRuntime.Node)
                {
                    if (FileSystemHelpers.FileExists(PackageJsonFileName))
                    {
                        var packageJsonData = FileSystemHelpers.ReadAllTextFromFile(PackageJsonFileName);
                        var packageJson = JsonConvert.DeserializeObject<JToken>(packageJsonData);
                        var funcPackageVersion = packageJson["dependencies"]["@azure/functions"];
                        if (funcPackageVersion != null && new Regex("^[^0-9]*4").IsMatch(funcPackageVersion.ToString()))
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

        private bool CurrentPathHasLocalSettings()
        {
            return FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, "local.settings.json"));
        }
    }
}
