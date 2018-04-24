using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new function from a template.")]
    [Action(Name = "new", HelpText = "Create a new function from a template.")]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new function from a template.")]
    internal class CreateFunctionAction : BaseAction
    {
        private readonly ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;

        public string Language { get; set; }
        public string TemplateName { get; set; }
        public string FunctionName { get; set; }

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
            templates = templates.Concat(_templatesManager.PythonTemplates);

            var workerRuntime = _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrWhiteSpace(workerRuntime) && !string.IsNullOrWhiteSpace(Language))
            {
                // validate
                var worker = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);
                var language = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);
                if (worker != language)
                {
                    throw new CliException("Selected language doesn't match worker set in local.settings.json." +
                        $"Selected worker is: {worker} and selected language is: {language}");
                }
            }
            else if (string.IsNullOrWhiteSpace(Language))
            {
                if (string.IsNullOrWhiteSpace(workerRuntime))
                {
                    ColoredConsole.Write("Select a language: ");
                    Language = SelectionMenuHelper.DisplaySelectionWizard(templates.Select(t => t.Metadata.Language).Distinct());
                    var worker = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);

                    _secretsManager.SetSecret(Constants.FunctionsWorkerRuntime, worker.ToString());
                    ColoredConsole
                        .WriteLine(WarningColor("Starting from 2.0.1-beta.26 it's required to set a language for your project in your settings"))
                        .WriteLine(WarningColor($"'{worker}' has been set in your local.settings.json"));
                }
                else
                {
                    var worker = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);
                    var languages = WorkerRuntimeLanguageHelper.LanguagesForWorker(worker);
                    ColoredConsole.Write("Select a language: ");
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
                        Language = SelectionMenuHelper.DisplaySelectionWizard(displayList);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(Language))
            {
                var worker = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(Language);
                _secretsManager.SetSecret(Constants.FunctionsWorkerRuntime, worker.ToString());
                ColoredConsole
                    .WriteLine(WarningColor("Starting from 2.0.1-beta.26 it's required to set a language for your project in your settings"))
                    .WriteLine(WarningColor($"'{worker}' has been set in your local.settings.json"));
            }

            ColoredConsole.Write("Select a template: ");
            var name = TemplateName ?? SelectionMenuHelper.DisplaySelectionWizard(templates.Where(t => t.Metadata.Language.Equals(Language, StringComparison.OrdinalIgnoreCase)).Select(t => t.Metadata.Name).Distinct());
            ColoredConsole.WriteLine(TitleColor(name));

            var template = templates.FirstOrDefault(t => t.Metadata.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Metadata.Language.Equals(Language, StringComparison.OrdinalIgnoreCase));

            if (template == null)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find template \"{name}\" in \"{Language}\""));
            }
            else
            {
                ColoredConsole.Write($"Function name: [{template.Metadata.DefaultFunctionName}] ");
                var functionName = FunctionName ?? Console.ReadLine();
                functionName = string.IsNullOrEmpty(functionName) ? template.Metadata.DefaultFunctionName : functionName;
                await _templatesManager.Deploy(functionName, template);
            }
        }
    }
}
