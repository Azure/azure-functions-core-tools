// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Displays Azure Functions CLI version information.
/// </summary>
internal class VersionCommand : FuncCliCommand, IBuiltInCommand
{
    private readonly IInteractionService _interaction;
    private readonly ICliVersionProvider _versionProvider;

    public VersionCommand(IInteractionService interaction, ICliVersionProvider versionProvider)
        : base("version", "Display the current Azure Functions CLI version.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(versionProvider);
        Hidden = true;
        _interaction = interaction;
        _versionProvider = versionProvider;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _interaction.WriteLine(_versionProvider.Version);
        return Task.FromResult(0);
    }

    /// <summary>
    /// Prints detailed version info.
    /// </summary>
    internal int ExecuteDetailed()
    {
        _interaction.WriteTitle("Azure Functions CLI");
        _interaction.WriteBlankLine();
        _interaction.WriteTable(
            ["Property", "Value"],
            [
                ["Version", _versionProvider.Version],
                ["Build", _versionProvider.InformationalVersion],
                ["Runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription],
                ["OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription],
                ["Architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()],
            ]
        );

        return 0;
    }
}
