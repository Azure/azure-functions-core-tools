// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Displays detailed information about a specific template by id.
/// </summary>
internal sealed class QuickstartInfoCommand : FuncCliCommand
{
    public Argument<string> TemplateIdArgument { get; } = new("id")
    {
        Description = "Template ID from the manifest (e.g. http-trigger-python-azd). Use 'func quickstart list' to see available IDs."
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IQuickstartProvider> _providers;

    public QuickstartInfoCommand(IInteractionService interaction, IEnumerable<IQuickstartProvider> providers)
        : base("info", "Show detailed information about a template.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(providers);

        _interaction = interaction;
        _providers = providers.ToList();

        Arguments.Add(TemplateIdArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            _interaction.WriteWarning(
                "The quickstart command is not yet available. Stack support is coming in a future release.");
            return Task.FromResult(1);
        }

        _interaction.WriteWarning("The quickstart info command is not yet implemented.");
        return Task.FromResult(1);
    }
}
