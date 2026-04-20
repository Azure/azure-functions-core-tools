// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a new function from a template. Delegates to workload-provided
/// template providers via ITemplateProvider.
/// </summary>
public class NewCommand : BaseCommand
{
    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function"
    };

    public static readonly Option<string?> TemplateOption = new("--template", "-t")
    {
        Description = "The function template name"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Overwrite existing files"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager? _workloadManager;

    public NewCommand(IInteractionService interaction, IWorkloadManager? workloadManager = null)
        : base("new", "Create a new function from a template.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        // Check if this is an initialized Functions project
        var cwd = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(cwd, "host.json")))
        {
            if (!_interaction.IsInteractive)
            {
                _interaction.WriteError("No Azure Functions project found. Run func init first.");
                return 1;
            }

            _interaction.WriteWarning("No Azure Functions project found in this directory.");
            var runInit = await _interaction.ConfirmAsync(
                "Would you like to initialize a project first?",
                defaultValue: true,
                cancellationToken);

            if (!runInit)
            {
                return 1;
            }

            var initCommand = new InitCommand(_interaction, _workloadManager);
            var initResult = await initCommand.RunInitAsync(
                workerRuntime: null, language: null, name: null, force: false,
                parseResult: null, cancellationToken);
            if (initResult != 0)
            {
                return initResult;
            }

            _interaction.WriteBlankLine();
        }

        var templateName = parseResult.GetValue(TemplateOption);
        var functionName = parseResult.GetValue(NameOption);
        var force = parseResult.GetValue(ForceOption);

        // Get all available templates from workloads
        var providers = _workloadManager?.GetAllTemplateProviders() ?? [];

        if (providers.Count == 0)
        {
            _interaction.WriteError("No language workloads installed.");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine(
                "[grey]Azure Functions supports many languages through workloads. Install one to get started:[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("  [white]func workload install dotnet[/]       [grey]C#, F#[/]");
            _interaction.WriteMarkupLine("  [white]func workload install node[/]         [grey]JavaScript, TypeScript[/]");
            _interaction.WriteMarkupLine("  [white]func workload install python[/]       [grey]Python[/]");
            _interaction.WriteMarkupLine("  [white]func workload install java[/]         [grey]Java[/]");
            _interaction.WriteMarkupLine("  [white]func workload install powershell[/]   [grey]PowerShell[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Run[/] [white]func workload search[/] [grey]to discover all available workloads.[/]");
            return 1;
        }

        // Try to detect the project's worker runtime and language to filter templates
        var (detectedRuntime, detectedLanguage) = ProjectDetector.DetectRuntimeAndLanguage(cwd);

        // Collect all templates, filtered by runtime if detected
        var allTemplates = new List<(ITemplateProvider Provider, FunctionTemplate Template)>();
        foreach (var provider in providers)
        {
            if (detectedRuntime is not null
                && !provider.WorkerRuntime.Equals(detectedRuntime, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var templates = await provider.GetTemplatesAsync(cancellationToken);
            foreach (var template in templates)
            {
                allTemplates.Add((provider, template));
            }
        }

        if (allTemplates.Count == 0)
        {
            _interaction.WriteError("No templates available from installed workloads.");
            return 1;
        }

        // If no template specified, prompt for one
        if (string.IsNullOrEmpty(templateName))
        {
            templateName = await _interaction.PromptForSelectionAsync(
                "Select a template:",
                allTemplates.Select(t => t.Template.Name).Distinct(),
                cancellationToken);
        }

        // Find matching template and provider
        var match = allTemplates.FirstOrDefault(t =>
            t.Template.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));

        if (match == default)
        {
            _interaction.WriteError($"Template '{templateName}' not found.");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Available templates:[/]");
            foreach (var t in allTemplates.Select(t => t.Template).DistinctBy(t => t.Name))
            {
                _interaction.WriteMarkupLine($"  [white]{t.Name}[/] [grey]— {t.Description}[/]");
            }
            return 1;
        }

        // If no function name specified, prompt for one
        if (string.IsNullOrEmpty(functionName))
        {
            var defaultName = $"{templateName}1";
            functionName = _interaction.IsInteractive
                ? await _interaction.PromptForInputAsync("Function name:", defaultName, cancellationToken)
                : defaultName;
        }

        var context = new FunctionScaffoldContext(
            TemplateName: templateName,
            FunctionName: functionName,
            OutputPath: Directory.GetCurrentDirectory(),
            Language: match.Template.Language ?? detectedLanguage,
            Force: force);

        await _interaction.StatusAsync(
            $"Creating function '{functionName}' from template '{templateName}'...",
            async ct => await match.Provider.ScaffoldAsync(context, ct),
            cancellationToken);

        _interaction.WriteSuccess($"Function '{functionName}' created from template '{templateName}'.");
        return 0;
    }

}
