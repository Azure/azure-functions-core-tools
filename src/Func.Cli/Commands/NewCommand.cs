// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a new function from a template. Templates and scaffolding are
/// supplied by <see cref="ITemplateProvider"/>s contributed by workloads;
/// each provider may also contribute additional options to this command.
/// </summary>
public class NewCommand : BaseCommand
{
    public static readonly Option<string?> WorkerRuntimeOption = new("--worker-runtime", "-w")
    {
        Description = "The worker runtime that owns the project"
    };

    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function"
    };

    public static readonly Option<string?> TemplateOption = new("--template", "-t")
    {
        Description = "The function template name"
    };

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "The programming language"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Overwrite existing files"
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<ITemplateProvider> _providers;
    private readonly IReadOnlyList<WorkloadSummary> _workloads;

    public NewCommand(
        IInteractionService interaction,
        IEnumerable<ITemplateProvider> providers,
        IReadOnlyList<WorkloadSummary> workloads)
        : base("new", "Create a new function from a template.")
    {
        _interaction = interaction;
        _providers = providers.ToList();
        _workloads = workloads;

        AddPathArgument();
        Options.Add(WorkerRuntimeOption);
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        foreach (var provider in _providers)
        {
            foreach (var option in provider.GetNewOptions())
            {
                Options.Add(option);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var workerRuntime = parseResult.GetValue(WorkerRuntimeOption);
        var templateName = parseResult.GetValue(TemplateOption);
        var functionName = parseResult.GetValue(NameOption);

        var provider = await SelectProviderAsync(workerRuntime, cancellationToken);
        if (provider is null)
        {
            WorkloadHints.WriteNoMatchingWorkload(
                _interaction,
                _workloads,
                actionDescription: "create functions from templates",
                requestedRuntime: workerRuntime);
            return 1;
        }

        if (string.IsNullOrEmpty(templateName))
        {
            templateName = await SelectTemplateAsync(provider, cancellationToken);
            if (templateName is null)
            {
                return 1;
            }
        }

        if (string.IsNullOrEmpty(functionName))
        {
            if (!_interaction.IsInteractive)
            {
                throw new GracefulException(
                    "Missing required option '--name <function-name>'.",
                    isUserError: true);
            }

            functionName = await _interaction.PromptForInputAsync(
                "Function name:",
                defaultValue: templateName,
                cancellationToken);
        }

        var project = new FunctionProjectContext(
            ProjectPath: Directory.GetCurrentDirectory(),
            WorkerRuntime: provider.WorkerRuntime,
            Language: parseResult.GetValue(LanguageOption));

        await provider.ScaffoldAsync(project, templateName, functionName, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Created function ")
            .Code(functionName)
            .Muted(" from template ")
            .Code(templateName)
            .Muted("."));

        return 0;
    }

    private async Task<string?> SelectTemplateAsync(ITemplateProvider provider, CancellationToken cancellationToken)
    {
        var templates = await provider.GetTemplatesAsync(cancellationToken);
        if (templates.Count == 0)
        {
            _interaction.WriteHint($"Workload '{provider.WorkerRuntime}' exposes no templates.");
            return null;
        }

        // Non-interactive: surface the list so the user can re-run with --template.
        if (!_interaction.IsInteractive)
        {
            _interaction.WriteHint($"Templates for {provider.WorkerRuntime}:");
            var items = templates.Select(t => new DefinitionItem(t.Name, t.Description));
            _interaction.WriteDefinitionList(items);
            _interaction.WriteBlankLine();
            _interaction.WriteHint("Re-run with --template <name> to scaffold one.");
            return null;
        }

        return await _interaction.PromptForSelectionAsync(
            "Select a template:",
            templates.Select(t => t.Name),
            cancellationToken);
    }

    private async Task<ITemplateProvider?> SelectProviderAsync(string? workerRuntime, CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(workerRuntime))
        {
            return _providers.FirstOrDefault(p => p.CanHandle(workerRuntime));
        }

        if (_providers.Count == 1)
        {
            return _providers[0];
        }

        if (!_interaction.IsInteractive)
        {
            return null;
        }

        var picked = await _interaction.PromptForSelectionAsync(
            "Select a worker runtime:",
            _providers.Select(p => p.WorkerRuntime),
            cancellationToken);

        return _providers.FirstOrDefault(p => p.WorkerRuntime == picked);
    }
}
