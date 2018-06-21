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
                else
                {
                    var languages = WorkerRuntimeLanguageHelper.LanguagesForWorker(workerRuntime);
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
                workerRuntime = WorkerRuntimeLanguageHelper.SetWorkerRuntime(_secretsManager, Language);
            }

            if (workerRuntime == WorkerRuntime.dotnet)
            {
                if (!string.IsNullOrWhiteSpace(TemplateName))
                {
                    ColoredConsole.Write("Function name: ");
                    var functionName = FunctionName ?? Console.ReadLine();
                    ColoredConsole.WriteLine(functionName);
                    var namespaceStr = Path.GetFileName(Environment.CurrentDirectory);
                    await DotnetHelpers.DeployDotnetFunction(TemplateName, functionName, namespaceStr);
                }
                else
                {
                    ColoredConsole.WriteLine("Please select a template");
                    await DotnetHelpers.ListTemplates();
                }
            }
            else
            {
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
}
