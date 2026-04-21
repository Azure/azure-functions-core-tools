// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a new function from a template. Detects the project's runtime,
/// then routes templates.list / templates.create to the matching workload.
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

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "Programming language",
    };

    public static readonly Option<string?> AuthLevelOption = new("--auth-level", "-a")
    {
        Description = "Authorization level (HTTP triggers)",
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Overwrite existing files"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHost _workloadHost;

    public NewCommand(IInteractionService interaction, IWorkloadHost workloadHost)
        : base("new", "Create a new function from a template.")
    {
        _interaction = interaction;
        _workloadHost = workloadHost;

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(LanguageOption);
        Options.Add(AuthLevelOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var detection = await _workloadHost.DetectProjectAsync(Directory.GetCurrentDirectory(), cancellationToken);
        if (detection is null)
        {
            _interaction.WriteError("No installed workload could detect a project here.");
            _interaction.WriteMarkupLine("[grey]Run[/] [white]func init[/] [grey]first, or install the matching workload.[/]");
            return 1;
        }

        var (workload, _) = detection.Value;
        await using var client = await _workloadHost.StartByIdAsync(workload.Manifest.Id, cancellationToken);

        var template = parseResult.GetValue(TemplateOption);
        var language = parseResult.GetValue(LanguageOption);
        if (string.IsNullOrEmpty(template))
        {
            var listed = await client.InvokeAsync(
                WorkloadProtocol.Methods.TemplatesList,
                new TemplatesListParams(language),
                WorkloadJsonContext.Default.TemplatesListParams,
                WorkloadJsonContext.Default.TemplatesListResult,
                cancellationToken);

            if (listed.Templates.Count == 0)
            {
                _interaction.WriteError($"Workload '{workload.Manifest.Id}' has no templates available.");
                return 1;
            }

            template = await _interaction.PromptForSelectionAsync(
                "Select a template",
                listed.Templates.Select(t => t.Name),
                cancellationToken);
        }

        var name = parseResult.GetValue(NameOption)
            ?? await _interaction.PromptForInputAsync("Function name", "MyFunction", cancellationToken);

        var result = await client.InvokeAsync(
            WorkloadProtocol.Methods.TemplatesCreate,
            new TemplatesCreateParams(
                TemplateName: template!,
                FunctionName: name,
                OutputPath: Directory.GetCurrentDirectory(),
                Language: language,
                AuthLevel: parseResult.GetValue(AuthLevelOption),
                Force: parseResult.GetValue(ForceOption)),
            WorkloadJsonContext.Default.TemplatesCreateParams,
            WorkloadJsonContext.Default.TemplatesCreateResult,
            cancellationToken);

        _interaction.WriteSuccess($"Function '{name}' created ({result.FilesCreated.Count} files).");
        foreach (var file in result.FilesCreated)
        {
            _interaction.WriteMarkupLine($"  [grey]+[/] {file}");
        }
        return 0;
    }
}
