// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Launches the Azure Functions host runtime via 'func start'.
/// </summary>
internal class StartCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<int?> PortOption { get; } = new("--port", "-p")
    {
        Description = "The port to listen on (default: 7071)"
    };

    public Option<string?> CorsOption { get; } = new("--cors")
    {
        Description = "A comma-separated list of CORS origins"
    };

    public Option<bool> CorsCredentialsOption { get; } = new("--cors-credentials")
    {
        Description = "Allow cross-origin authenticated requests"
    };

    public Option<string[]?> FunctionsOption { get; } = new("--functions")
    {
        Description = "A space-separated list of functions to load",
        Arity = ArgumentArity.ZeroOrMore
    };

    public Option<bool> NoBuildOption { get; } = new("--no-build")
    {
        Description = "Do not build the project before running"
    };

    public Option<bool> EnableAuthOption { get; } = new("--enable-auth")
    {
        Description = "Enable full authentication handling"
    };

    public Option<string?> HostVersionOption { get; } = new("--host-version")
    {
        Description = "The host runtime version to use (e.g., 4.1049.0)"
    };

    private readonly IInteractionService _interaction;

    public StartCommand(IInteractionService interaction)
        : base("start", "Launch the Azure Functions host runtime.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        _interaction = interaction;

        AddPathArgument();
        Options.Add(PortOption);
        Options.Add(CorsOption);
        Options.Add(CorsCredentialsOption);
        Options.Add(FunctionsOption);
        Options.Add(NoBuildOption);
        Options.Add(EnableAuthOption);
        Options.Add(HostVersionOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var workingDirectory = parseResult.GetValue(PathArgument!)!;
        if (!workingDirectory.Exists)
        {
            // Echo the path as the user typed it (when explicit) so the error
            // matches their input rather than a fully-resolved absolute path.
            var displayPath = workingDirectory.OriginalPath ?? workingDirectory.Info.FullName;
            throw new GracefulException(
                $"The specified path does not exist: '{displayPath}'",
                isUserError: true);
        }

        _interaction.WriteWarning("The 'start' command is not yet implemented in this version.");
        _interaction.WriteHint("This is a preview build of Azure Functions CLI vNext.");
        return Task.FromResult(1);
    }
}
