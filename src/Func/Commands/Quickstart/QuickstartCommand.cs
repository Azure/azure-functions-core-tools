// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
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
    public Option<string?> StackOption { get; } = new("--stack", "-s")
    {
        Description = "The stack to use. Run `func workload list` to see what's installed."
    };

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "The programming language"
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID from the manifest (e.g. http-trigger-python-azd) — skips all prompts"
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
        Description = "Case-insensitive substring filter applied to template names and descriptions"
    };

    public Option<FetchMode> FetchOption { get; } = new("--fetch")
    {
        Description = "Download strategy: auto (default), git, or http",
        DefaultValueFactory = _ => FetchMode.Auto
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IQuickstartProvider> _providers;

    public QuickstartCommand(
        QuickstartListCommand listCommand,
        QuickstartInfoCommand infoCommand,
        IInteractionService interaction,
        IEnumerable<IQuickstartProvider> providers)
        : base("quickstart", "Browse and scaffold complete function apps from the Azure Functions template catalog.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        ArgumentNullException.ThrowIfNull(infoCommand);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(providers);

        _interaction = interaction;
        _providers = providers.ToList();

        LanguageOption.Description = BuildLanguageOptionDescription(_providers);

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(LanguageOption);
        Options.Add(TemplateOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
        Options.Add(FetchOption);

        Subcommands.Add(listCommand);
        Subcommands.Add(infoCommand);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            _interaction.WriteWarning(
                "The quickstart command is not yet available. Stack support is coming in a future release.");
            return Task.FromResult(1);
        }

        // Implementation will be added in a follow-up PR:
        // 1. Fetch manifest via IQuickstartManifestService
        // 2. Resolve stack/language (prompt if interactive, error if non-TTY without --language)
        // 3. Resolve template (--template skips prompts, otherwise filter + prompt)
        // 4. Scaffold via IQuickstartScaffolder
        _interaction.WriteWarning(
            "The quickstart command is not yet implemented. This is a placeholder for the upcoming scaffold flow.");
        return Task.FromResult(1);
    }

    private static string BuildLanguageOptionDescription(IReadOnlyList<IQuickstartProvider> providers)
    {
        var languages = providers
            .SelectMany(p => p.SupportedLanguages)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        return languages.Count == 0
            ? "The programming language. Install a stack workload (`func workload install <id>`) to see supported values."
            : "The programming language. Supported values: " + string.Join(", ", languages) + ".";
    }
}
