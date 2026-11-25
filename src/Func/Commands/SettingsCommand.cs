// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Stub for the legacy v4 <c>func settings</c> command group. v5 does not manage
/// <c>local.settings.json</c>; users edit the file directly, and encrypted files
/// must be decrypted with v4 Core Tools.
/// </summary>
internal sealed class SettingsCommand : FuncCliCommand, IBuiltInCommand
{
    private static readonly HashSet<string> _v4Actions =
        [with(StringComparer.OrdinalIgnoreCase), "add", "delete", "remove", "list", "encrypt", "decrypt"];

    private readonly IInteractionService _interaction;

    public SettingsCommand(IInteractionService interaction)
        : base("settings", "Manage local.settings.json (not supported in v5).")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Hidden = true;
        TreatUnmatchedTokensAsErrors = false;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? action = parseResult.UnmatchedTokens.FirstOrDefault(_v4Actions.Contains);
        string commandName = action is null ? "func settings" : $"func settings {action}";

        _interaction.WriteWarning($"'{commandName}' is not supported in v5 of the Azure Functions CLI.");
        _interaction.WriteBlankLine();

        if (string.Equals(action, "encrypt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "decrypt", StringComparison.OrdinalIgnoreCase))
        {
            _interaction.WriteLine("Encrypted local.settings.json files are not supported in v5. To decrypt one, use v4 of Core Tools:");
            _interaction.WriteLine(l => l.Muted("  ").Code("func settings decrypt"));
        }
        else
        {
            _interaction.WriteLine("Edit local.settings.json directly to add, change, or remove settings.");
        }

        return Task.FromResult(1);
    }
}
