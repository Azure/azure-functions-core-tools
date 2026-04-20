// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Launches the Azure Functions host runtime: func start [path] [options].
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
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = true
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
    private readonly IHostRunner _hostRunner;

    public StartCommand(IInteractionService interaction, IHostRunner hostRunner)
        : base("start", "Launch the Azure Functions host runtime.")
    {
        _interaction = interaction;
        _hostRunner = hostRunner;

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

        var scriptRoot = Directory.GetCurrentDirectory();

        if (!HostConfiguration.ValidateScriptRoot(scriptRoot, _interaction))
        {
            return Task.FromResult(1);
        }

        var port = parseResult.GetValue(PortOption) ?? HostConfiguration.DefaultPort;

        var config = new HostConfiguration(scriptRoot)
        {
            Port = port,
            CorsOrigins = parseResult.GetValue(CorsOption),
            CorsCredentials = parseResult.GetValue(CorsCredentialsOption),
            FunctionsFilter = parseResult.GetValue(FunctionsOption),
            NoBuild = parseResult.GetValue(NoBuildOption),
            EnableAuth = parseResult.GetValue(EnableAuthOption),
            Verbose = parseResult.GetValue(FuncRootCommand.VerboseOption),
            HostVersion = parseResult.GetValue(HostVersionOption),
        };

        return Task.FromResult(_hostRunner.Start(config, cancellationToken));
    }
}
