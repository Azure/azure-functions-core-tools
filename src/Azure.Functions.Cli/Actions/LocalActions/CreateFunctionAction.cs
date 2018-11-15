using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Azure.Functions.Cli.Helpers.TelemetryHelpers;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new function from a template.")]
    [Action(Name = "new", HelpText = "Create a new function from a template.")]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new function from a template.")]
    internal class CreateFunctionAction : BaseAction, ITelemetryEventAction
    {
        private readonly ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;

        public string Language { get; set; }
        public string TemplateName { get; set; }
        public string FunctionName { get; set; }
        public bool Csx { get; set; }

        public CreateFunctionAction(ITemplatesManager templatesManager, ISecretsManager secretsManager)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
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
            return base.ParseArgs(args);
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
            ColoredConsole.WriteLine($"The function \"{FunctionName}\" was created successfully from the \"{TemplateName}\" template.");
        }

        public void UpdateTelemetryLogEvent(ConsoleAppLogEvent consoleEvent)
        {
            consoleEvent.CommandEvents = new Dictionary<string, string>
            {
                { "language", Language },
                { "trigger", TemplateName }
            };
        }
    }
}
