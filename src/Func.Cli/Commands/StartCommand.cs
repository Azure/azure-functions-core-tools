// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Launches the Azure Functions host runtime via 'func start'.
/// </summary>
public class StartCommand : BaseCommand
{
    public static readonly Option<int?> PortOption = new("--port", "-p")
    {
        Description = "The port to listen on (default: 7071)"
    };

    public static readonly Option<string?> CorsOption = new("--cors")
    {
        Description = "A comma-separated list of CORS origins"
    };

    public static readonly Option<bool> CorsCredentialsOption = new("--cors-credentials")
    {
        Description = "Allow cross-origin authenticated requests"
    };

    public static readonly Option<string[]?> FunctionsOption = new("--functions")
    {
        Description = "A space-separated list of functions to load",
        Arity = ArgumentArity.ZeroOrMore
    };

    public static readonly Option<bool> NoBuildOption = new("--no-build")
    {
        Description = "Do not build the project before running"
    };

    public static readonly Option<bool> EnableAuthOption = new("--enable-auth")
    {
        Description = "Enable full authentication handling"
    };

    public static readonly Option<string?> HostVersionOption = new("--host-version")
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
        ApplyPath(parseResult);
        _interaction.WriteWarning("The 'start' command is not yet implemented in this version.");
        _interaction.WriteHint("This is a preview build of Azure Functions CLI vNext.");
        return Task.FromResult(1);
    }
}
