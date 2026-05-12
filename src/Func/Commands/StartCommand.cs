// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Launches the Azure Functions host runtime via 'func start'.
/// </summary>
/// <remarks>
/// v1 prototype: real host integration is not yet implemented, so the
/// command runs against <see cref="DemoEventSource"/> by default. This
/// produces a runnable demonstration of all three output modes (compact,
/// plain, JSON) against the canonical scenario from the design plan.
/// </remarks>
internal sealed class StartCommand : FuncCliCommand, IBuiltInCommand
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

    public Option<string?> HostVersionOption { get; } = new("--host-version", "-v")
    {
        Description = "The host runtime version to use (e.g., 4.1049.0)"
    };

    public Option<string?> OutputOption { get; } = new("--output")
    {
        Description = "Output mode: compact (interactive TUI), plain (CI / non-TTY), or json (NDJSON for AI agents). Defaults to auto-detect."
    };

    public Option<bool> NoTuiOption { get; } = new("--no-tui")
    {
        Description = "Alias for --output=plain. Disables the interactive TUI."
    };

    private readonly IInteractionService _interaction;
    private readonly FunctionPalette _palette;

    public StartCommand(IInteractionService interaction, FunctionPalette palette)
        : base("start", "Launch the Azure Functions host runtime.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(palette);
        _interaction = interaction;
        _palette = palette;

        AddPathArgument();
        Options.Add(PortOption);
        Options.Add(CorsOption);
        Options.Add(CorsCredentialsOption);
        Options.Add(FunctionsOption);
        Options.Add(NoBuildOption);
        Options.Add(EnableAuthOption);
        Options.Add(HostVersionOption);
        Options.Add(OutputOption);
        Options.Add(NoTuiOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        if (!workingDirectory.Exists)
        {
            string displayPath = workingDirectory.OriginalPath ?? workingDirectory.Info.FullName;
            throw new GracefulException(
                $"The specified path does not exist: '{displayPath}'",
                isUserError: true);
        }

        OutputMode mode = ResolveOutputMode(parseResult);

        mode = OutputModeResolver.ApplyTerminalSafetyFallback(mode, _interaction, out bool downgraded);
        if (downgraded)
        {
            System.Console.Error.WriteLine("notice: stdout is not an interactive terminal; falling back to --output=plain.");
        }

        IHostEventStream source = new DemoEventSource
        {
            SpeedMultiplier = ParseSpeedMultiplier(Environment.GetEnvironmentVariable("FUNC_DEMO_SPEED")),
            AutoExit = ParseAutoExit(Environment.GetEnvironmentVariable("FUNC_DEMO_AUTOEXIT")),
        };

        var state = new DashboardState();
        IDashboardRenderer renderer = CreateRenderer(mode);

        var pipeline = new DashboardPipeline(state, source, renderer);
        return await pipeline.RunAsync(cancellationToken);
    }

    private static double ParseSpeedMultiplier(string? raw)
    {
        const double @default = 0.25;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return @default;
        }

        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : @default;
    }

    private static bool ParseAutoExit(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim() is "1" or "true" or "TRUE" or "True" or "yes" or "YES" or "on" or "ON";
    }

    private OutputMode ResolveOutputMode(ParseResult parseResult)
    {
        string? raw = parseResult.GetValue(OutputOption);
        OutputMode? explicitMode = null;
        if (!string.IsNullOrEmpty(raw))
        {
            if (!OutputModeResolver.TryParse(raw, out OutputMode parsed))
            {
                throw new GracefulException(
                    $"--output must be one of: compact, plain, json. Got '{raw}'.",
                    isUserError: true);
            }

            explicitMode = parsed;
        }

        bool noTui = parseResult.GetValue(NoTuiOption);
        return OutputModeResolver.Resolve(explicitMode, noTui, _interaction);
    }

    private IDashboardRenderer CreateRenderer(OutputMode mode) => mode switch
    {
        OutputMode.Json => new JsonRenderer(),
        OutputMode.Plain => new PlainRenderer(_interaction),
        OutputMode.Compact => new CompactRenderer(_interaction, _palette),
        _ => throw new InvalidOperationException($"Unsupported output mode: {mode}"),
    };
}
