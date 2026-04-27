// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Displays Azure Functions CLI version information.
/// </summary>
internal class VersionCommand : BaseCommand
{
    private readonly IInteractionService _interaction;

    public VersionCommand(IInteractionService interaction)
        : base("version", "Display the current Azure Functions CLI version.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        Hidden = true;
        _interaction = interaction;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _interaction.WriteLine(GetVersion());
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
                ["Version", GetVersion()],
                ["Build", GetInformationalVersion()],
                ["Runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription],
                ["OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription],
                ["Architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()],
            ]
        );

        return 0;
    }

    public static string GetVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string GetInformationalVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "unknown";
    }
}
