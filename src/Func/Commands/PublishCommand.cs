// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Stub for the legacy v4 <c>func publish</c> command. Deployment is not part
/// of the v5 CLI; we redirect users to the Azure CLI's
/// <c>az functionapp deployment</c> family, falling back to v4 Core Tools for
/// the legacy publish flow.
/// </summary>
internal sealed class PublishCommand : FuncCliCommand, IBuiltInCommand
{
    private static readonly HashSet<string> _settingsOnlyFlags = ["-o", "--publish-settings-only"];

    private static readonly HashSet<string> _settingsFlags =
        [.. _settingsOnlyFlags, "-i", "--publish-local-settings", "-y", "--overwrite-settings"];

    private readonly IInteractionService _interaction;

    public PublishCommand(IInteractionService interaction)
        : base("publish", "Publish a function app to Azure (not supported in v5).")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Hidden = true;
        TreatUnmatchedTokensAsErrors = false;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _interaction.WriteWarning("'func publish' is not supported in v5 of the Azure Functions CLI.");
        _interaction.WriteBlankLine();

        if (!parseResult.UnmatchedTokens.Any(_settingsOnlyFlags.Contains))
        {
            _interaction.WriteLine("Use the Azure CLI to deploy your function app, for example:");
            _interaction.WriteLine(l => l.Muted("  ").Code("az functionapp deployment source config-zip -g <resource-group> -n <app-name> --src <zip-path>"));
            _interaction.WriteBlankLine();
        }

        if (parseResult.UnmatchedTokens.Any(_settingsFlags.Contains))
        {
            _interaction.WriteLine("To publish app settings, use the Azure CLI, for example:");
            _interaction.WriteLine(l => l.Muted("  ").Code("az functionapp config appsettings set -g <resource-group> -n <app-name> --settings <name>=<value>"));
            _interaction.WriteBlankLine();
        }

        _interaction.WriteLine("Or use v4 of Core Tools for the legacy 'func azure functionapp publish' flow:");
        _interaction.WriteLine(l => l.Muted("  ").Plain("https://learn.microsoft.com/azure/azure-functions/functions-run-local"));
        return Task.FromResult(1);
    }
}
