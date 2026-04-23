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
    private readonly IReadOnlyList<IWorkload> _workloads;

    public NewCommand(
        IInteractionService interaction,
        IEnumerable<ITemplateProvider> providers,
        IReadOnlyList<IWorkload> workloads)
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

        var provider = SelectProvider(workerRuntime);
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
            await ListTemplatesAsync(provider, cancellationToken);
            return 0;
        }

        if (string.IsNullOrEmpty(functionName))
        {
            throw new GracefulException(
                "Missing required option '--name <function-name>'.",
                isUserError: true);
        }

        var context = new FunctionScaffoldContext(
            TemplateName: templateName,
            FunctionName: functionName,
            OutputPath: Directory.GetCurrentDirectory(),
            Language: parseResult.GetValue(LanguageOption));

        await provider.ScaffoldAsync(context, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Created function ")
            .Code(functionName)
            .Muted(" from template ")
            .Code(templateName)
            .Muted("."));

        return 0;
    }

    private async Task ListTemplatesAsync(ITemplateProvider provider, CancellationToken ct)
    {
        var templates = await provider.GetTemplatesAsync(ct);
        if (templates.Count == 0)
        {
            _interaction.WriteHint($"Workload '{provider.WorkerRuntime}' exposes no templates.");
            return;
        }

        _interaction.WriteHint($"Templates for {provider.WorkerRuntime}:");
        var items = templates.Select(t => new DefinitionItem(t.Name, t.Description));
        _interaction.WriteDefinitionList(items);
    }

    private ITemplateProvider? SelectProvider(string? workerRuntime)
    {
        if (_providers.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrEmpty(workerRuntime))
        {
            return _providers.Count == 1 ? _providers[0] : null;
        }

        return _providers.FirstOrDefault(p => p.CanHandle(workerRuntime));
    }
}
