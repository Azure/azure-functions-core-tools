// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Adds a function to an initialized Functions project from an installed item template.
/// </summary>
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Function name.",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID.",
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Overwrite existing files.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive")
    {
        Description = "Refuse to prompt.",
    };

    public Option<bool> ListOption { get; } = new("--list", "-l")
    {
        Description = "List available templates (temporarily unavailable).",
    };

    public Option<string?> FormatOption { get; } = new("--format")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
    };

    private readonly IInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly ITemplaterFactory _templaterFactory;

    public NewCommand(IInteractionService interaction, IFunctionsProjectResolver projectResolver, ITemplaterFactory templaterFactory)
        : base("new", "Create a new function from a template.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _templaterFactory = templaterFactory ?? throw new ArgumentNullException(nameof(templaterFactory));

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(ListOption);
        Options.Add(FormatOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (parseResult.GetValue(ListOption))
        {
            _interaction.WriteError("Template listing is temporarily unavailable while the Azure Functions CLI migrates to the new templating engine.");
            return 1;
        }

        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        FunctionsProject project = await ResolveInitializedProjectAsync(workingDirectory, cancellationToken);

        string? reference = parseResult.GetValue(TemplateOption);
        string? name = parseResult.GetValue(NameOption);
        bool canPrompt = _interaction.IsInteractive && !parseResult.GetValue(NonInteractiveOption);
        if (!canPrompt && (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(name)))
        {
            throw new GracefulException("Specify both --template and --name when running non-interactively.", isUserError: true);
        }

        if (string.IsNullOrWhiteSpace(project.Language))
        {
            throw new GracefulException($"No function templates are available for this {project.StackDisplayName} project.", isUserError: true);
        }

        TemplateEngineProjectContext projectContext = new(project.WorkingDirectory, project.StackName, project.Language);
        using Templater templater = _templaterFactory.Create(new TemplateEngineContext(workingDirectory, projectContext));

        FuncTemplateInfo template = await SelectTemplateAsync(templater, project.Language, reference, cancellationToken);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = await _interaction.PromptForInputAsync("Function name:", template.DefaultName, cancellationToken);
        }

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template,
            name,
            project.WorkingDirectory.Info.FullName,
            new Dictionary<string, string>(),
            parseResult.GetValue(ForceOption),
            cancellationToken);

        if (result.Status != CreationResultStatus.Success)
        {
            throw new GracefulException(DescribeCreationFailure(template, result), isUserError: true);
        }

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Created function ")
            .Code(name)
            .Muted(" from template ")
            .Code(template.Name)
            .Muted("."));

        return 0;
    }

    // Requires `func init` to have run first; unlike v4, `func new` never initializes the project itself.
    private async Task<FunctionsProject> ResolveInitializedProjectAsync(WorkingDirectory workingDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory.Info)))
        {
            throw new GracefulException(
                $"'{workingDirectory.Info.FullName}' is not an initialized Azure Functions project. Run 'func init' first.",
                isUserError: true);
        }

        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(workingDirectory),
            cancellationToken);

        return resolution switch
        {
            ProjectResolutionResult.Resolved resolved => resolved.Project,
            ProjectResolutionResult.NotResolved notResolved => throw new GracefulException(notResolved.Message, isUserError: true),
            _ => throw new InvalidOperationException($"Unexpected project resolution result '{resolution.GetType().Name}'."),
        };
    }

    private async Task<FuncTemplateInfo> SelectTemplateAsync(
        Templater templater, string language, string? reference, CancellationToken cancellationToken)
    {
        IReadOnlyList<FuncTemplateInfo> templates = await templater.FindItemTemplatesAsync(
            language, string.IsNullOrWhiteSpace(reference) ? null : reference, cancellationToken);

        if (templates.Count == 0)
        {
            throw new GracefulException(
                string.IsNullOrWhiteSpace(reference)
                    ? $"No function templates are installed for {language}."
                    : $"No function template '{reference}' is installed for {language}.",
                isUserError: true);
        }

        if (!string.IsNullOrWhiteSpace(reference))
        {
            return templates.Count == 1
                ? templates[0]
                : throw new GracefulException(
                    $"Multiple function templates match '{reference}': {string.Join(", ", templates.Select(t => t.Identity))}. "
                    + "Pass a template identity to --template to choose one.",
                    isUserError: true);
        }

        var choices = templates.ToDictionary(
            t => templates.Count(other => string.Equals(other.Name, t.Name, StringComparison.Ordinal)) > 1 ? $"{t.Name} ({t.Identity})" : t.Name,
            StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync("Select a template:", choices.Keys, cancellationToken);
        return choices[picked];
    }

    private static string DescribeCreationFailure(ITemplateInfo template, ITemplateCreationResult result)
    {
        if (result.Status == CreationResultStatus.DestructiveChangesDetected)
        {
            string files = string.Join(", ", result.CreationEffects?.FileChanges
                .Where(c => c.ChangeKind != ChangeKind.Create)
                .Select(c => c.TargetRelativePath) ?? []);

            return $"Cannot create the function because these files already exist: {files}. Use --force to overwrite them.";
        }

        return $"Failed to create the function from template '{template.Identity}': {result.ErrorMessage ?? result.Status.ToString()}";
    }
}
