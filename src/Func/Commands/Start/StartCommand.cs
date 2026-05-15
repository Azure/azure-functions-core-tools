// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;

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

    public Option<string?> LogFileOption { get; } = new("--log-file")
    {
        Description = "Mirror all host events to the specified log file."
    };

    // Prototype-only knob: scales the number of functions DemoEventSource
    // generates so layout variants (full-table ≤8 vs. status-strip >8) can
    // be demoed without code changes. Hidden from --help; intended for
    // demos and screenshots until real host integration replaces the
    // synthetic event source. Also overridable via FUNC_DEMO_FUNCTIONS.
    public Option<int?> DemoFunctionsOption { get; } = new("--demo-functions")
    {
        Description = "Demo: number of functions to load (clamped to a minimum of 5).",
        Hidden = true,
    };

    private readonly IInteractionService _interaction;
    private readonly FunctionPalette _palette;
    private readonly ICliVersionProvider _versionProvider;
    private readonly IStartInitializationRunner _initializationRunner;

    public StartCommand(
        IInteractionService interaction,
        FunctionPalette palette,
        ICliVersionProvider versionProvider,
        IStartInitializationRunner initializationRunner)
        : base("start", "Launch the Azure Functions host runtime.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(initializationRunner);

        _interaction = interaction;
        _palette = palette;
        _versionProvider = versionProvider;
        _initializationRunner = initializationRunner;

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
        Options.Add(LogFileOption);
        Options.Add(DemoFunctionsOption);
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

        string? logFilePath = parseResult.GetValue(LogFileOption);
        IDashboardEventSink? eventSink = CreateLogFileSink(logFilePath);

        var initializationContext = new StartInitializationContext(
            workingDirectory,
            _versionProvider.Version,
            ProfileName: "none",
            RequestedHostVersion: parseResult.GetValue(HostVersionOption),
            DemoFunctionCount: ParseFunctionCount(
                parseResult.GetValue(DemoFunctionsOption),
                Environment.GetEnvironmentVariable("FUNC_DEMO_FUNCTIONS")),
            DemoSpeedMultiplier: ParseSpeedMultiplier(Environment.GetEnvironmentVariable("FUNC_DEMO_SPEED")),
            DemoAutoExit: ParseAutoExit(Environment.GetEnvironmentVariable("FUNC_DEMO_AUTOEXIT")));

        StartInitializationResult initializationResult;
        await using (IStartInitializationRenderer initializationRenderer = CreateInitializationRenderer(mode))
        {
            initializationResult = await _initializationRunner.RunAsync(initializationContext, initializationRenderer, cancellationToken);
        }

        var state = new DashboardState();

        IDashboardRenderer renderer = CreateRenderer(mode, initializationResult.RunInfo);

        var pipeline = new DashboardPipeline(state, initializationResult.EventStream, renderer, eventSink);
        return await pipeline.RunAsync(cancellationToken);
    }

    private static IDashboardEventSink? CreateLogFileSink(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return DashboardLogFileSink.Create(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new GracefulException(
                $"Could not open log file '{path}': {ex.Message}",
                isUserError: true,
                verboseMessage: ex.ToString());
        }
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

    private static int ParseFunctionCount(int? cliValue, string? envRaw)
    {
        // CLI option takes precedence over the env-var fallback. Both are
        // clamped to a minimum of 5 because the scripted opener and
        // expansion together always discover 5 functions; lower values
        // would be silently overridden by the source anyway.
        const int Default = 5;

        if (cliValue is { } cli)
        {
            return Math.Max(Default, cli);
        }

        if (!string.IsNullOrWhiteSpace(envRaw)
            && int.TryParse(envRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            return Math.Max(Default, parsed);
        }

        return Default;
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

    private IDashboardRenderer CreateRenderer(OutputMode mode, DashboardRunInfo runInfo) => mode switch
    {
        OutputMode.Json => new JsonRenderer(),
        OutputMode.Plain => new PlainRenderer(_interaction),
        OutputMode.Compact => new CompactRenderer(_interaction, _palette, runInfo: runInfo),
        _ => throw new InvalidOperationException($"Unsupported output mode: {mode}"),
    };

    private IStartInitializationRenderer CreateInitializationRenderer(OutputMode mode) => mode switch
    {
        OutputMode.Json => new JsonStartInitializationRenderer(),
        OutputMode.Plain => new PlainStartInitializationRenderer(_interaction),
        OutputMode.Compact => new CompactStartInitializationRenderer(_interaction),
        _ => throw new InvalidOperationException($"Unsupported output mode: {mode}"),
    };
}
