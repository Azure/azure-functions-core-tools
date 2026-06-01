// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Globalization;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Launches the Azure Functions host runtime via 'func run' (also aliased
/// as 'func start' for backward compatibility).
/// </summary>
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

    public Option<string?> ProfileOption { get; } = new("--profile")
    {
        Description = "The Azure Functions profile to apply while resolving host, worker, and bundle versions"
    };

    public Option<bool> OfflineOption { get; } = new("--offline")
    {
        Description = "Use only locally installed workloads and skip network installs"
    };

    public Option<string?> OutputOption { get; } = new("--output")
    {
        Description = "Output mode: compact (interactive TUI), plain (CI / non-TTY), "
            + "or json (NDJSON for AI agents). Defaults to auto-detect."
    };

    public Option<bool> NoTuiOption { get; } = new("--no-tui")
    {
        Description = "Alias for --output=plain. Disables the interactive TUI."
    };

    public Option<string?> LogFileOption { get; } = new("--log-file")
    {
        Description = "Mirror all host events to the specified log file."
    };

    public Option<bool> DemoOption { get; } = new("--demo")
    {
        Description = "Demo: use the synthetic host event stream instead of launching the host workload.",
        Hidden = true,
    };

    public Option<bool> NoAzuriteOption { get; } = new("--no-azurite")
    {
        Description = "Disable managed Azurite. The host will start without probing or starting a local emulator."
    };

    // Demo-only knob: scales the number of functions DemoEventSource
    // generates so layout variants (full-table ≤8 vs. status-strip >8) can
    // be demoed without code changes. Hidden from --help; intended for
    // demos and screenshots. Also overridable via FUNC_DEMO_FUNCTIONS.
    public Option<int?> DemoFunctionsOption { get; } = new("--demo-functions")
    {
        Description = "Demo: number of functions to load (clamped to a minimum of 5).",
        Hidden = true,
    };

    private readonly IInteractionService _interaction;
    private readonly FunctionPalette _palette;
    private readonly ICliVersionProvider _versionProvider;
    private readonly IStartInitializationRunner _initializationRunner;
    private readonly CompactDashboardShortcutLabels _shortcutLabels;
    private readonly IPlatform _platform;
    private readonly StartDashboardEventStreamFactory _eventStreamFactory;
    private readonly IOptionsMonitor<HostStartupOptions> _hostStartupOptions;

    public StartCommand(
        IInteractionService interaction,
        FunctionPalette palette,
        ICliVersionProvider versionProvider,
        IStartInitializationRunner initializationRunner,
        IOptionsMonitor<HostStartupOptions> hostStartupOptions,
        CompactDashboardShortcutLabels shortcutLabels,
        IPlatform platform,
        StartDashboardEventStreamFactory? eventStreamFactory = null)
        : base("run", "Launch the Azure Functions host runtime.")
    {
        ArgumentNullException.ThrowIfNull(interaction);

        // 'start' is the legacy name from Core Tools v4 and earlier. Kept as
        // an alias so existing scripts, package.json entries, and muscle
        // memory continue to work after the canonical rename to 'run'.
        Aliases.Add("start");

        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(initializationRunner);
        ArgumentNullException.ThrowIfNull(hostStartupOptions);
        ArgumentNullException.ThrowIfNull(shortcutLabels);
        ArgumentNullException.ThrowIfNull(platform);

        _interaction = interaction;
        _palette = palette;
        _versionProvider = versionProvider;
        _initializationRunner = initializationRunner;
        _shortcutLabels = shortcutLabels;
        _platform = platform;
        _eventStreamFactory = eventStreamFactory ?? new StartDashboardEventStreamFactory();
        _hostStartupOptions = hostStartupOptions;

        AddPathArgument();
        Options.Add(PortOption);
        Options.Add(CorsOption);
        Options.Add(CorsCredentialsOption);
        Options.Add(FunctionsOption);
        Options.Add(NoBuildOption);
        Options.Add(EnableAuthOption);
        Options.Add(ProfileOption);
        Options.Add(HostVersionOption);
        Options.Add(OfflineOption);
        Options.Add(OutputOption);
        Options.Add(NoTuiOption);
        Options.Add(LogFileOption);
        Options.Add(DemoOption);
        Options.Add(DemoFunctionsOption);
        Options.Add(NoAzuriteOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        if (!workingDirectory.Exists)
        {
            string displayPath = workingDirectory.OriginalPath ?? workingDirectory.Info.FullName;
            throw new GracefulException($"The specified path does not exist: '{displayPath}'", isUserError: true);
        }

        OutputMode mode = ResolveOutputMode(parseResult);

        mode = OutputModeResolver.ApplyTerminalSafetyFallback(mode, _interaction, out bool downgraded);
        if (downgraded)
        {
            _interaction.WriteWarning("stdout is not an interactive terminal; falling back to --output=plain.");
        }

        HostStartupOptions hostStartupOptions = GetHostStartupOptions(workingDirectory.Info);
        StartCommandOptions options = CreateStartOptions(parseResult, workingDirectory, mode, hostStartupOptions);
        IDashboardEventSink? eventSink = CreateLogFileSink(options.LogFilePath);

        var initializationContext = new StartInitializationContext(
            options,
            _versionProvider.Version,
            _interaction.IsInteractive,
            CanPrompt: _interaction.IsInteractive && mode != OutputMode.Json);

        StartInitializationResult initializationResult;
        IReadOnlyList<StartInitializationEvent> initializationEvents;
        IStartInitializationRenderer startInitializationRenderer = CreateInitializationRenderer(mode, initializationContext.CliVersion);
        await using (var initializationRenderer = new RecordingStartInitializationRenderer(startInitializationRenderer))
        {
            initializationResult = await _initializationRunner.RunAsync(initializationContext, initializationRenderer, cancellationToken);
            if (!initializationRenderer.HasCompleted)
            {
                var completedEvent = new StartInitializationCompletedEvent(DateTimeOffset.UtcNow, initializationResult);
                await initializationRenderer.OnEventAsync(completedEvent, cancellationToken);
            }

            initializationEvents = [.. initializationRenderer.Events];
        }

        // Take ownership of the host stream now so any failure before pipeline completion still tears the process down.
        await using IHostEventStream hostEventStream = initializationResult.EventStream;

        var state = new DashboardState();

        IDashboardRenderer renderer = CreateRenderer(mode, initializationResult.RunInfo);

        IHostEventStream dashboardEventStream = _eventStreamFactory.Create(mode, initializationEvents, hostEventStream);
        var pipeline = new DashboardPipeline(state, dashboardEventStream, renderer, eventSink);
        FunctionsProjectHostRunOutcome? outcome = null;
        // Stop any managed Azurite instance the orchestrator launched when
        // the host run completes (success, failure, or Ctrl+C). Wrapping the
        // host run in `await using` guarantees the process never outlives
        // `func start`.
        await using ManagedAzuriteHandle? azurite = initializationResult.ManagedAzurite;
        try
        {
            int exitCode = await pipeline.RunAsync(cancellationToken);
            outcome = FunctionsProjectHostRunOutcomes.Completed(exitCode);
            return exitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = FunctionsProjectHostRunOutcomes.Canceled();
            throw;
        }
        catch (Exception ex)
        {
            outcome = FunctionsProjectHostRunOutcomes.Failed(ex);
            throw;
        }
        finally
        {
            if (outcome is not null)
            {
                await CompleteProjectHostRunAsync(initializationResult, outcome);
            }
        }
    }

    private async Task CompleteProjectHostRunAsync(StartInitializationResult initializationResult, FunctionsProjectHostRunOutcome outcome)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completionContext = new FunctionsProjectHostRunCompletionContext(initializationResult.HostRunContext, outcome);

        try
        {
            await initializationResult.Project.CompleteHostRunAsync(completionContext, cleanupCts.Token);
        }
        catch (OperationCanceledException) when (cleanupCts.IsCancellationRequested)
        {
            _interaction.WriteWarning("Project cleanup did not complete within 5 seconds.");
        }
        catch (Exception ex)
        {
            // Project cleanup runs after the host outcome is known, so keep the
            // original host result primary and surface cleanup as a warning.
            _interaction.WriteWarning($"Project cleanup failed: {ex.Message}");
        }
    }

    private HostStartupOptions GetHostStartupOptions(DirectoryInfo projectDirectory)
        => ProjectDirectoryResolver.IsProjectDirectory(projectDirectory)
            ? _hostStartupOptions.CurrentValue
            : _hostStartupOptions.Get(Path.GetFullPath(projectDirectory.FullName));

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
            string message = $"Could not open log file '{path}': {ex.Message}";
            throw new GracefulException(message, isUserError: true, verboseMessage: ex.ToString());
        }
    }

    private StartCommandOptions CreateStartOptions(
        ParseResult parseResult,
        WorkingDirectory workingDirectory,
        OutputMode mode,
        HostStartupOptions hostStartupOptions)
        => new(
            workingDirectory,
            parseResult.GetValue(PortOption) ?? hostStartupOptions.Port,
            ParseCors(parseResult.GetValue(CorsOption) ?? hostStartupOptions.Cors),
            parseResult.GetValue(CorsCredentialsOption) || hostStartupOptions.CorsCredentials is true,
            parseResult.GetValue(FunctionsOption) ?? [],
            parseResult.GetValue(NoBuildOption),
            parseResult.GetValue(EnableAuthOption),
            parseResult.GetValue(ProfileOption),
            parseResult.GetValue(HostVersionOption),
            parseResult.GetValue(OfflineOption),
            mode,
            parseResult.GetValue(NoTuiOption),
            parseResult.GetValue(LogFileOption),
            parseResult.GetValue(DemoOption) || ParseBooleanEnvironmentVariable("FUNC_START_DEMO"),
            ParseFunctionCount(
                parseResult.GetValue(DemoFunctionsOption),
                Environment.GetEnvironmentVariable("FUNC_DEMO_FUNCTIONS")),
            ParseSpeedMultiplier(Environment.GetEnvironmentVariable("FUNC_DEMO_SPEED")),
            ParseAutoExit(Environment.GetEnvironmentVariable("FUNC_DEMO_AUTOEXIT")),
            parseResult.GetValue(NoAzuriteOption));

    private static string[] ParseCors(string? cors)
        => string.IsNullOrWhiteSpace(cors)
            ? []
            : [.. cors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static double ParseSpeedMultiplier(string? raw)
    {
        const double @default = 0.25;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return @default;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : @default;
    }

    private static bool ParseBooleanEnvironmentVariable(string name)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
            && int.TryParse(envRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
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
                throw new GracefulException($"--output must be one of: compact, plain, json. Got '{raw}'.", isUserError: true);
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
        OutputMode.Compact => new CompactRenderer(_interaction, _palette, _shortcutLabels, _platform, runInfo: runInfo),
        _ => throw new InvalidOperationException($"Unsupported output mode: {mode}"),
    };

    private IStartInitializationRenderer CreateInitializationRenderer(OutputMode mode, string cliVersion) => mode switch
    {
        OutputMode.Json => new JsonStartInitializationRenderer(),
        OutputMode.Plain => new PlainStartInitializationRenderer(_interaction),
        OutputMode.Compact => new CompactStartInitializationRenderer(_interaction, cliVersion),
        _ => throw new InvalidOperationException($"Unsupported output mode: {mode}"),
    };
}
