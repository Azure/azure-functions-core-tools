// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Displays detailed information about a specific template by id.
/// Resolves the owning provider from the entry's language for display-name
/// mapping.
/// </summary>
internal sealed class QuickstartInfoCommand : FuncCliCommand
{
    public Argument<string> TemplateIdArgument { get; } = new("id")
    {
        Description = "Template ID from the manifest (e.g. http-trigger-python-azd). Use 'func quickstart list' to see available IDs."
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of formatted output."
    };

    private readonly IInteractionService _interaction;
    private readonly IQuickstartProviderResolver _resolver;
    private readonly IQuickstartManifestService _manifestService;

    public QuickstartInfoCommand(
        IInteractionService interaction,
        IQuickstartProviderResolver resolver,
        IQuickstartManifestService manifestService)
        : base("info", "Show detailed information about a template.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(manifestService);

        _interaction = interaction;
        _resolver = resolver;
        _manifestService = manifestService;

        Arguments.Add(TemplateIdArgument);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string templateId = parseResult.GetValue(TemplateIdArgument)!;
        bool json = parseResult.GetValue(JsonOption);

        QuickstartManifest manifest = await _interaction.ShowStatusAsync(
            QuickstartMessages.FetchingCatalogStatus,
            _manifestService.GetManifestAsync,
            cancellationToken);

        QuickstartEntry? entry = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.Id, templateId, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _interaction.WriteError(
                $"Template '{templateId}' not found. " + QuickstartMessages.TemplateNotFoundHint);
            return 1;
        }

        IQuickstartProvider? provider = _resolver.FindProviderForLanguage(entry.Language);
        string displayLanguage = provider?.GetDisplayLanguage(entry.Language) ?? entry.Language;

        if (json)
        {
            _interaction.WriteJson(new InfoRow(
                entry.Id,
                entry.DisplayName,
                displayLanguage,
                entry.Resource,
                entry.Iac ?? "none",
                entry.GitRef,
                entry.RepositoryUrl,
                entry.FolderPath,
                entry.ShortDescription,
                entry.LongDescription,
                entry.WhatsIncluded));
            return 0;
        }

        _interaction.WriteLine(l => l.Heading($"{entry.DisplayName}"));
        _interaction.WriteLine(l => l.Muted($"  ID:        {entry.Id}"));
        _interaction.WriteLine(l => l.Muted($"  Language:  {displayLanguage}"));
        _interaction.WriteLine(l => l.Muted($"  Resource:  {entry.Resource}"));
        _interaction.WriteLine(l => l.Muted($"  IaC:       {entry.Iac ?? "none"}"));
        _interaction.WriteLine(l => l.Muted($"  Git Ref:   {entry.GitRef ?? "-"}"));
        _interaction.WriteLine(l => l.Muted($"  Repo:      {entry.RepositoryUrl}"));
        _interaction.WriteLine(l => l.Muted($"  Path:      {entry.FolderPath}"));

        if (entry.ShortDescription is not null)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteLine(l => l.Plain(entry.ShortDescription));
        }

        if (entry.LongDescription is not null)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteLine(l => l.Plain(entry.LongDescription));
        }

        if (entry.WhatsIncluded is { Count: > 0 })
        {
            _interaction.WriteBlankLine();
            _interaction.WriteLine(l => l.Heading(QuickstartMessages.WhatsIncludedHeading));
            foreach (string item in entry.WhatsIncluded)
            {
                _interaction.WriteLine(l => l.Muted($"{QuickstartMessages.StepBullet}{item}"));
            }
        }

        _interaction.WriteBlankLine();
        string stack = provider?.Stack ?? entry.Language.ToLowerInvariant();
        _interaction.WriteLine(l => l.Muted($"  Run: func quickstart <path> --stack {stack} --language {displayLanguage.ToLowerInvariant()} --template {entry.Id}"));

        return 0;
    }

    internal sealed record InfoRow(
        string Id,
        string Name,
        string Language,
        string Resource,
        string Iac,
        string? GitRef,
        string RepositoryUrl,
        string FolderPath,
        string? ShortDescription,
        string? LongDescription,
        IReadOnlyList<string>? WhatsIncluded);
}
