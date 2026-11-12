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
/// Creates an item from an installed .NET C# HTTP template.
/// </summary>
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand
{
    private const string HttpTemplateShortName = "http";

    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Function name.",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template identity or short name.",
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
        Description = "List installed .NET C# HTTP templates.",
    };

    public Option<string?> FormatOption { get; } = new("--format")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
    };

    private readonly IInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly InstalledProjectStackCatalog _stackCatalog;
    private readonly ITemplaterFactory _templaterFactory;

    public NewCommand(
        IInteractionService interaction,
        IFunctionsProjectResolver projectResolver,
        InstalledProjectStackCatalog stackCatalog,
        ITemplaterFactory templaterFactory)
        : base("new", "Create a new function from an installed template.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _stackCatalog = stackCatalog ?? throw new ArgumentNullException(nameof(stackCatalog));
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
        WorkingDirectory targetDirectory = parseResult.GetValue(PathArgument!)!;
        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(targetDirectory),
            cancellationToken);
        if (resolution is not ProjectResolutionResult.Resolved resolved)
        {
            return Fail("No initialized Functions project was found. Run `func init` before adding a function.");
        }

        FunctionsProject project = resolved.Project;
        if (!File.Exists(CliConfigurationPathsOptions.GetProjectConfigPath(project.WorkingDirectory.Info)))
        {
            return Fail("This Functions project has not been initialized by CLI v5. Run `func init` before adding a function.");
        }

        IProjectStack? stack = _stackCatalog.FindStack(project.StackName);
        if (stack is null)
        {
            return Fail($"The '{project.StackName}' Stack is not installed. Run `func setup --features {project.StackName}` and try again.");
        }

        if (!string.Equals(stack.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(project.Language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("`func new` currently supports HTTP templates only for initialized .NET C# source projects.");
        }

        if (parseResult.GetValue(ForceOption))
        {
            return Fail("`func new --force` is not supported because overwrite behavior for item templates has not been approved.");
        }

        using Templater templater = _templaterFactory.Create(new TemplateEngineContext(
            targetDirectory,
            new TemplateEngineProjectContext(project.WorkingDirectory, project.StackName, project.Language)));
        IReadOnlyList<ITemplateInfo> inventory = await templater.ListAsync(cancellationToken);
        ITemplateInfo[] httpTemplates = [.. inventory.Where(IsDotNetHttpTemplate)];

        if (parseResult.GetValue(ListOption))
        {
            return ListTemplates(httpTemplates, parseResult.GetValue(FormatOption));
        }

        string? reference = parseResult.GetValue(TemplateOption);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Fail("Specify an HTTP template with `--template <identity-or-short-name>`.");
        }

        ITemplateInfo[] exactMatches = [.. inventory.Where(template =>
            string.Equals(template.Identity, reference, StringComparison.Ordinal))];
        ITemplateInfo[] candidates = exactMatches.Length > 0
            ? [.. exactMatches.Where(IsDotNetHttpTemplate)]
            : [.. httpTemplates.Where(template =>
                template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase))];

        if (candidates.Length != 1)
        {
            string message = candidates.Length > 1
                ? $"Template reference '{reference}' is ambiguous: {string.Join(", ", candidates.Select(template => template.Identity))}."
                : exactMatches.Length > 0
                    ? $"Template identity '{reference}' is not a .NET C# HTTP item template."
                    : $"No installed .NET C# HTTP item template matches '{reference}'. Run `func new --list` to see available templates.";
            return Fail(message);
        }

        ITemplateInfo selectedTemplate = candidates[0];
        string? name = parseResult.GetValue(NameOption);
        ITemplateCreationResult creation;
        try
        {
            creation = await templater.CreateAsync(
                selectedTemplate,
                name,
                null,
                project.WorkingDirectory.Info.FullName,
                new Dictionary<string, string?>(StringComparer.Ordinal),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Fail($"Failed to create function from template '{selectedTemplate.Identity}': {ex.Message}");
        }
        if (creation.Status != CreationResultStatus.Success || creation.CreationResult is null)
        {
            return Fail(
                $"Failed to create function from template '{selectedTemplate.Identity}': "
                + $"{creation.ErrorMessage ?? creation.Status.ToString()}. Template files may have been written.");
        }

        foreach (IPostAction postAction in creation.CreationResult.PostActions)
        {
            try
            {
                await stack.RunTemplatePostActionAsync(postAction.ActionId.ToString(), postAction.Args, project.WorkingDirectory, cancellationToken);
            }
            catch (NotSupportedException ex)
            {
                return Fail($"Function files were created, but template post-action '{postAction.ActionId}' is unsupported: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                return Fail($"Function files were created, but template post-action '{postAction.ActionId}' failed: {ex.Message}");
            }
        }

        _interaction.WriteSuccess($"Created function from template '{selectedTemplate.Identity}'.");
        return 0;
    }

    private int ListTemplates(IReadOnlyList<ITemplateInfo> templates, string? format)
    {
        string outputFormat = format ?? "plain";
        if (!outputFormat.Equals("plain", StringComparison.OrdinalIgnoreCase)
            && !outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"Unsupported format '{outputFormat}'. Use 'plain' or 'json'.");
        }

        if (templates.Count == 0)
        {
            return Fail("No installed .NET C# HTTP item templates were found.");
        }

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            _interaction.WriteJson(templates.Select(template => new
            {
                template.Identity,
                template.Name,
                ShortNames = template.ShortNameList,
            }).ToArray());
            return 0;
        }

        _interaction.WriteTable(
            ["Name", "Short name", "Identity"],
            [.. templates.Select(template => new[]
            {
                template.Name,
                string.Join(", ", template.ShortNameList),
                template.Identity,
            })]);
        return 0;
    }

    private static bool IsDotNetHttpTemplate(ITemplateInfo template)
        => TemplateCatalogProjector.GetTemplateType(template) == TemplateType.Item
            && template.TagsCollection.TryGetValue("language", out string? language)
            && string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase)
            && template.ShortNameList.Contains(HttpTemplateShortName, StringComparer.OrdinalIgnoreCase);

    private int Fail(string message)
    {
        _interaction.WriteError(message);
        return 1;
    }
}
