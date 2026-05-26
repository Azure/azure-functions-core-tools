// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// <c>func quickstart info &lt;id&gt;</c>.
/// Shows detailed information about a single template.
/// </summary>
internal sealed class QuickstartInfoCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IQuickstartManifestClient _manifestClient;

    public Argument<string> IdArgument { get; } = new("id")
    {
        Description = "The template ID to look up.",
    };

    public QuickstartInfoCommand(
        IInteractionService interaction,
        IQuickstartManifestClient manifestClient)
        : base("info", "Show detailed information about a function app template.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));

        Arguments.Add(IdArgument);
    }

    protected override async Task<int> ExecuteAsync(
        ParseResult parseResult, CancellationToken cancellationToken)
    {
        string id = parseResult.GetValue(IdArgument)!;

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

        QuickstartEntry? template = manifest.Entries.FirstOrDefault(
            t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        if (template is null)
        {
            throw new GracefulException(
                $"Template '{id}' was not found. Run 'func quickstart list' to see available templates.",
                isUserError: true);
        }

        _interaction.WriteSectionHeader(template.DisplayName);

        var definitions = new List<DefinitionItem>
        {
            new("ID",           template.Id),
            new("Language",     template.Language),
            new("Resource",     template.Resource),
            new("Repository",   template.RepositoryUrl),
            new("Folder",       template.FolderPath),
        };

        if (!string.IsNullOrWhiteSpace(template.Iac))
        {
            definitions.Add(new("IaC", template.Iac));
        }

        if (!string.IsNullOrWhiteSpace(template.ShortDescription))
        {
            definitions.Add(new("Description", template.ShortDescription));
        }

        if (template.Tags.Count > 0)
        {
            definitions.Add(new("Tags", string.Join(", ", template.Tags)));
        }

        _interaction.WriteDefinitionList(definitions);

        if (template.WhatIsIncluded.Count > 0)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteSectionHeader("What's Included");
            foreach (string item in template.WhatIsIncluded)
            {
                _interaction.WriteLine($"  • {item}");
            }
        }

        return 0;
    }
}
