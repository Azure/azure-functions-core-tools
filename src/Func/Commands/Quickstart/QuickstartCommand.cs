// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Top-level <c>func quickstart</c> command. Fetches a CDN-hosted manifest of
/// complete function-app templates and scaffolds the selected one into a target
/// directory. Delegates language resolution to registered <see cref="IQuickstartProvider"/>
/// implementations contributed by each stack workload.
/// </summary>
internal sealed class QuickstartCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s");

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "The programming language"
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID from the manifest (e.g. http-trigger-python-azd) — skips template selection prompts"
    };

    public Option<string?> ResourceOption { get; } = new("--resource", "-r")
    {
        Description = "Filter by trigger/binding resource (e.g. http, timer, blob, eventhub, servicebus, cosmos, sql, mcp, durable)"
    };

    public Option<string?> IacOption { get; } = new("--iac")
    {
        Description = "Filter by infrastructure-as-code type (e.g. bicep, terraform, none)"
    };

    public Option<string?> SearchOption { get; } = new("--search")
    {
        Description = "Case-insensitive substring match against IDs, template names, resource type, Infrastructure as Code type, and descriptions"
    };

    public Option<FetchMode> FetchOption { get; } = new("--fetch")
    {
        Description = "Download strategy: auto (default), git, or http",
        DefaultValueFactory = _ => FetchMode.Auto
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Scaffolds even when the target folder isn't empty. Clears the folder (except .git) before scaffolding. In non-interactive mode, proceeds without confirmation."
    };

    private readonly IInteractionService _interaction;
    private readonly IQuickstartProviderResolver _resolver;
    private readonly IQuickstartManifestService _manifestService;
    private readonly IQuickstartScaffolder _scaffolder;

    public QuickstartCommand(
        QuickstartListCommand listCommand,
        QuickstartInfoCommand infoCommand,
        IInteractionService interaction,
        IQuickstartProviderResolver resolver,
        IQuickstartManifestService manifestService,
        IQuickstartScaffolder scaffolder,
        IEnumerable<IQuickstartProvider> providers)
        : base("quickstart", "Browse and scaffold complete function apps from the Azure Functions template catalog.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        ArgumentNullException.ThrowIfNull(infoCommand);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(scaffolder);
        ArgumentNullException.ThrowIfNull(providers);

        _interaction = interaction;
        _resolver = resolver;
        _manifestService = manifestService;
        _scaffolder = scaffolder;

        var providersList = providers.ToList();

        LanguageOption.Description = BuildLanguageOptionDescription(providersList);
        StackOption.Description = QuickstartMessages.BuildStackOptionDescription(providersList);

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(LanguageOption);
        Options.Add(TemplateOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
        Options.Add(FetchOption);
        Options.Add(ForceOption);

        Subcommands.Add(listCommand);
        Subcommands.Add(infoCommand);
    }

    protected override string HelpFooterHint => QuickstartMessages.HelpFooterHint;

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? requestedStack = parseResult.GetValue(StackOption);
        string? requestedLanguage = parseResult.GetValue(LanguageOption);
        string? templateId = parseResult.GetValue(TemplateOption);
        string? resource = parseResult.GetValue(ResourceOption);
        string? iac = parseResult.GetValue(IacOption);
        string? search = parseResult.GetValue(SearchOption);
        FetchMode fetchMode = parseResult.GetValue(FetchOption);
        bool force = parseResult.GetValue(ForceOption);

        // 1. Resolve stack → provider
        IQuickstartProvider? provider = await _resolver.SelectProviderAsync(requestedStack, "scaffold a quickstart project", cancellationToken);
        if (provider is null)
        {
            return 1;
        }

        // 2. Fetch manifest
        QuickstartManifest manifest = await _interaction.ShowStatusAsync(
            QuickstartMessages.FetchingCatalogStatus,
            _manifestService.GetManifestAsync,
            cancellationToken);

        // 3. Resolve language
        (string? manifestLanguage, int? langError) = await _resolver.ResolveOrPromptLanguageAsync(
            requestedLanguage, provider, manifest, cancellationToken);
        if (langError is int langCode)
        {
            return langCode;
        }

        // 4. Select template
        QuickstartEntry? entry;
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            entry = FindTemplateById(templateId, manifestLanguage!, provider, manifest);
            if (entry is null)
            {
                return 1;
            }
        }
        else
        {
            (entry, int? errorCode) = await SelectTemplateAsync(manifest, manifestLanguage!, resource, iac, search, cancellationToken);
            if (errorCode is int code)
            {
                return code;
            }
        }

        // 5. Resolve directory + --force
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        workingDirectory.CreateIfNotExists();

        if (!force && DirectoryGuard.HasNonGitContent(workingDirectory.Info))
        {
            _interaction.WriteError(QuickstartMessages.DirectoryNotEmptyError);
            return 1;
        }

        if (force)
        {
            if (!await ConfirmClearDirectoryAsync(workingDirectory.Info, cancellationToken))
            {
                _interaction.WriteHint(QuickstartMessages.CancelledHint);
                return 1;
            }

            DirectoryGuard.ClearExceptGit(workingDirectory.Info);
        }

        // 6. Scaffold
        await _interaction.StatusAsync(
            $"Scaffolding '{entry!.DisplayName}'...",
            ct => _scaffolder.ScaffoldAsync(entry, workingDirectory.Info.FullName, fetchMode, ct),
            cancellationToken);

        // 7. Next steps
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Success(QuickstartMessages.SuccessIcon)
            .Muted("Scaffolded ")
            .Code(entry.DisplayName)
            .Muted(" for ")
            .Code(provider.DisplayName)
            .Muted("."));

        IReadOnlyList<string> steps = provider.GetNextSteps(manifestLanguage!);
        if (steps.Count > 0)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteHint("Next steps:");

            string targetPath = workingDirectory.Info.FullName;
            string currentDir = Directory.GetCurrentDirectory();
            if (!string.Equals(targetPath, currentDir, StringComparison.OrdinalIgnoreCase))
            {
                string displayPath = workingDirectory.OriginalPath ?? targetPath;
                _interaction.WriteLine(l => l.Muted($"{QuickstartMessages.StepBullet}Run `cd \"{displayPath}\"`"));
            }

            foreach (string step in steps)
            {
                _interaction.WriteLine(l => l.Muted($"{QuickstartMessages.StepBullet}{step}"));
            }
        }

        return 0;
    }

    private QuickstartEntry? FindTemplateById(string templateId, string manifestLanguage, IQuickstartProvider provider, QuickstartManifest manifest)
    {
        QuickstartEntry? entry = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.Id, templateId, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _interaction.WriteError(
                $"Template '{templateId}' not found in the catalog. " + QuickstartMessages.TemplateNotFoundHint);
            return null;
        }

        if (!string.Equals(entry.Language, manifestLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _interaction.WriteError(
                $"Template '{templateId}' is for {entry.Language}, " +
                $"but you selected {provider.GetDisplayLanguage(manifestLanguage)}. " +
                $"Pass --language {entry.Language.ToLowerInvariant()} to use this template.");
            return null;
        }

        return entry;
    }

    private async Task<(QuickstartEntry? Entry, int? ErrorCode)> SelectTemplateAsync(
        QuickstartManifest manifest,
        string manifestLanguage,
        string? resource,
        string? iac,
        string? search,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<QuickstartEntry> filtered = manifest.Filter(manifestLanguage, resource, iac, search);

        if (filtered.Count == 0)
        {
            _interaction.WriteError(QuickstartMessages.NoMatchingFiltersError);
            return (null, 1);
        }

        if (filtered.Count == 1)
        {
            return (filtered[0], null);
        }

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteError(QuickstartMessages.MultipleMatchesError);
            return (null, 1);
        }

        var displayToEntry = new Dictionary<string, QuickstartEntry>(StringComparer.Ordinal);
        foreach (QuickstartEntry entry in filtered)
        {
            string label = entry.ShortDescription is not null
                ? $"{entry.DisplayName} — {entry.ShortDescription}"
                : entry.DisplayName;

            // Handle duplicate display names by appending the ID
            if (!displayToEntry.TryAdd(label, entry))
            {
                label = $"{entry.DisplayName} ({entry.Id})";
                displayToEntry[label] = entry;
            }
        }

        string picked = await _interaction.PromptForSelectionAsync(
            "Select a template:",
            displayToEntry.Keys,
            cancellationToken);

        return displayToEntry.TryGetValue(picked, out QuickstartEntry? chosen) ? (chosen, null) : (null, 1);
    }

    private async Task<bool> ConfirmClearDirectoryAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        if (!DirectoryGuard.HasNonGitContent(workingDirectory))
        {
            return true;
        }

        _interaction.WriteWarning(
            $"--force will delete all files in '{workingDirectory.FullName}' (except .git) before scaffolding.");

        if (!_interaction.IsInteractive)
        {
            return true;
        }

        return await _interaction.ConfirmAsync("Continue?", defaultValue: false, cancellationToken);
    }

    private static string BuildLanguageOptionDescription(IReadOnlyList<IQuickstartProvider> providers)
    {
        var languages = providers
            .SelectMany(p => p.ManifestLanguages)
            .Select(l => l.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        return languages.Count == 0
            ? "The programming language. Install a stack workload (`func workload install <id>`) to see supported values."
            : "The programming language. Supported values: " + string.Join(", ", languages) + ".";
    }
}
