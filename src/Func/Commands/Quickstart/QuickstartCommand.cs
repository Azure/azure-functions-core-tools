// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// <c>func quickstart [&lt;path&gt;] [--language] [--template] [--resource] [--iac] [--search] [--fetch]</c>.
/// Top-level command that browses the CDN manifest and scaffolds a complete
/// Azure Functions app template. Run without a subcommand for interactive mode.
/// </summary>
internal sealed class QuickstartCommand : FuncCliCommand, IBuiltInCommand
{
    // Next-steps banners per runtime.
    private static readonly Dictionary<string, string[]> _nextSteps =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Python"]     = ["python -m venv .venv", "pip install -r requirements.txt", "func start"],
            ["TypeScript"] = ["npm install", "npm run build", "func start"],
            ["JavaScript"] = ["npm install", "func start"],
            ["CSharp"]     = ["dotnet restore", "func start"],
            ["FSharp"]     = ["dotnet restore", "func start"],
            ["Java"]       = ["mvn clean package", "func start"],
            ["PowerShell"] = ["func start"],
        };

    private readonly IInteractionService _interaction;
    private readonly IQuickstartManifestClient _manifestClient;
    private readonly IQuickstartScaffolder _scaffolder;

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "Programming language (e.g. CSharp, Python, TypeScript, node, dotnet).",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID to scaffold directly (skips interactive selection).",
    };

    public Option<string?> ResourceOption { get; } = new("--resource", "-r")
    {
        Description = "Azure resource type filter (e.g. 'HTTP Trigger').",
    };

    public Option<string?> IacOption { get; } = new("--iac")
    {
        Description = "Filter by infrastructure-as-code type (e.g. Bicep, Terraform).",
    };

    public Option<string?> SearchOption { get; } = new("--search", "-s")
    {
        Description = "Case-insensitive substring filter applied to IDs, display names, resources, tags, and descriptions before prompting.",
    };

    public Option<FetchMode> FetchOption { get; } = new("--fetch")
    {
        Description = "How to fetch the template payload: 'auto' (default — use git when 2.25+ is available, else http), 'git', or 'http'.",
        DefaultValueFactory = _ => FetchMode.Auto,
    };

    public QuickstartCommand(
        QuickstartListCommand listCommand,
        QuickstartInfoCommand infoCommand,
        IInteractionService interaction,
        IQuickstartManifestClient manifestClient,
        IQuickstartScaffolder scaffolder)
        : base("quickstart", "Browse and scaffold complete Azure Functions app templates.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        ArgumentNullException.ThrowIfNull(infoCommand);
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
        _scaffolder = scaffolder ?? throw new ArgumentNullException(nameof(scaffolder));

        Subcommands.Add(listCommand);
        Subcommands.Add(infoCommand);

        AddPathArgument();
        Options.Add(LanguageOption);
        Options.Add(TemplateOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
        Options.Add(FetchOption);
    }

    protected override async Task<int> ExecuteAsync(
        ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? languageFlag = parseResult.GetValue(LanguageOption);
        string? templateId   = parseResult.GetValue(TemplateOption);
        string? resource     = parseResult.GetValue(ResourceOption);
        string? iac          = parseResult.GetValue(IacOption);
        string? search       = parseResult.GetValue(SearchOption);
        FetchMode fetchMode  = parseResult.GetValue(FetchOption);
        WorkingDirectory workingDir = parseResult.GetValue(PathArgument!)!;
        string targetPath = workingDir.Info.FullName;

        QuickstartManifest manifest;
        try
        {
            manifest = await _interaction.ShowStatusAsync(
                "Fetching quickstart manifest…",
                _manifestClient.GetManifestAsync,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }

        // Resolve the manifest-language string (may require sub-prompt for dotnet/node).
        string? language = await ResolveLanguageAsync(languageFlag, cancellationToken);

        // Scaffold directly when --template is specified.
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            QuickstartEntry? byId = manifest.Entries.FirstOrDefault(
                t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));

            if (byId is null)
            {
                throw new GracefulException(
                    $"Template '{templateId}' was not found. Run 'func quickstart list' to see available templates.",
                    isUserError: true);
            }

            return await RunScaffoldAsync(byId, targetPath, fetchMode, cancellationToken);
        }

        // Interactive: filter candidates then let user pick.
        IReadOnlyList<QuickstartEntry> candidates = manifest.Filter(language, resource, iac, search);
        if (candidates.Count == 0)
        {
            string message = !string.IsNullOrWhiteSpace(search)
                ? $"No templates found matching '{search}'. Run 'func quickstart list' to browse all available templates."
                : "No templates matched the specified filters. Run 'func quickstart list' to browse all available templates.";
            _interaction.WriteHint(message);
            return 1;
        }

        var choices = candidates.Select(t => $"{t.Id} — {t.DisplayName}").ToList();
        string chosen = await _interaction.PromptForSelectionAsync(
            "Select a template:", choices, cancellationToken);

        // The choice format is "{id} — {displayName}", extract the ID.
        string chosenId = chosen.Split(" — ", 2)[0].Trim();
        QuickstartEntry selected = candidates.First(
            t => string.Equals(t.Id, chosenId, StringComparison.OrdinalIgnoreCase));

        return await RunScaffoldAsync(selected, targetPath, fetchMode, cancellationToken);
    }

    private async Task<int> RunScaffoldAsync(
        QuickstartEntry template, string targetPath, FetchMode fetchMode, CancellationToken cancellationToken)
    {
        _interaction.WriteHint(
            $"Scaffolding template '{template.DisplayName}' into '{targetPath}'…");

        try
        {
            await _interaction.StatusAsync(
                "Downloading quickstart…",
                ct => _scaffolder.ScaffoldAsync(template, targetPath, fetchMode, ct),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }

        _interaction.WriteSuccess($"Template '{template.DisplayName}' scaffolded successfully.");
        WritNextStepsBanner(template.Language);
        return 0;
    }

    private async Task<string?> ResolveLanguageAsync(
        string? languageFlag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(languageFlag))
        {
            return null;
        }

        string? mapped = LanguageMapper.ToManifestLanguage(languageFlag);
        if (mapped is not null)
        {
            return mapped;
        }

        // The flag value itself might already be a manifest language string
        // (e.g. user passed "TypeScript" not "typescript").
        if (LanguageMapper.AllManifestLanguages.Contains(languageFlag, StringComparer.OrdinalIgnoreCase))
        {
            return languageFlag;
        }

        // Needs a sub-prompt for runtimes that map to multiple languages.
        IReadOnlyList<string> runtimeLanguages = LanguageMapper.GetLanguagesForRuntime(languageFlag);
        if (runtimeLanguages.Count == 0)
        {
            throw new GracefulException(
                $"Unknown language '{languageFlag}'. " +
                "Supported values: csharp, fsharp, javascript, typescript, python, java, powershell, dotnet, node.",
                isUserError: true);
        }

        return await _interaction.PromptForSelectionAsync(
            $"Select the programming language for the '{languageFlag}' runtime:",
            runtimeLanguages,
            cancellationToken);
    }

    private void WritNextStepsBanner(string language)
    {
        if (!_nextSteps.TryGetValue(language, out string[]? steps))
        {
            return;
        }

        _interaction.WriteBlankLine();
        _interaction.WriteSectionHeader("Next steps");
        foreach (string step in steps)
        {
            _interaction.WriteLine($"  {step}");
        }
    }
}
