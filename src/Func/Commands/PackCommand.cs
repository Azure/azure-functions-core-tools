// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Stub for the legacy v4 <c>func pack</c> command. Packaging is not supported
/// in v5 yet; this command exists only to redirect users to v4 with a clear
/// message instead of falling through to "command not found".
/// </summary>
internal sealed class PackCommand : FuncCliCommand, IBuiltInCommand
{
    private readonly IInteractionService _interaction;

    public PackCommand(IInteractionService interaction)
        : base("pack", "Package a function app for deployment (not supported yet).")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Hidden = true;
        TreatUnmatchedTokensAsErrors = false;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _interaction.WriteWarning("'func pack' is not supported yet in v5 of the Azure Functions CLI.");
        _interaction.WriteBlankLine();
        _interaction.WriteLine("In the meantime, to package a function app, use v4 of Core Tools:");
        _interaction.WriteLine(l => l.Muted("  ").Plain("https://learn.microsoft.com/azure/azure-functions/functions-run-local"));
        return Task.FromResult(1);
    }
}
