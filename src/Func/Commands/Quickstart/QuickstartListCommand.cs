// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Lists available templates from the CDN manifest, optionally filtered by
/// language, resource, IaC type, or keyword search.
/// </summary>
internal sealed class QuickstartListCommand : FuncCliCommand
{
    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "Filter by worker runtime or language (e.g. python, node, java, dotnet)"
    };

    public Option<string?> ResourceOption { get; } = new("--resource", "-r")
    {
        Description = "Filter by trigger/binding resource (e.g. http, timer, blob, eventhub, servicebus, cosmos, sql, mcp, durable)"
    };

    public Option<string?> IacOption { get; } = new("--iac")
    {
        Description = "Filter by infrastructure-as-code type (e.g. bicep, terraform, none)"
    };

    public Option<string?> SearchOption { get; } = new("--search", "-s")
    {
        Description = "Case-insensitive substring match against template names, IDs, resources, tags, and descriptions"
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IQuickstartProvider> _providers;

    public QuickstartListCommand(
        IInteractionService interaction,
        IEnumerable<IQuickstartProvider> providers)
        : base("list", "List available templates from the catalog.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(providers);

        _interaction = interaction;
        _providers = providers.ToList();

        Options.Add(LanguageOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            _interaction.WriteWarning(
                "The quickstart command is not yet available. Stack support is coming in a future release.");
            return Task.FromResult(1);
        }

        _interaction.WriteWarning(
            "The quickstart list command is not yet implemented.");
        return Task.FromResult(1);
    }
}
