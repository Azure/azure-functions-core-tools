// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

ITheme theme = new DefaultTheme();
IInteractionService interaction = new SpectreInteractionService(theme);

// Wire up the OpenTelemetry SDK only when telemetry is configured (build has
// a real instrumentation key and the user has not opted out). When it isn't,
// no listener is subscribed and ActivitySource/Meter calls become no-ops.
TracerProvider? tracerProvider = null;
MeterProvider? meterProvider = null;
if (CliTelemetry.TryGetConnectionString(out var connectionString))
{
    var resourceBuilder = CliTelemetry.CreateResourceBuilder();

    tracerProvider = Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(resourceBuilder)
        .AddSource(CliTelemetry.SourceName)
        .AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString)
        .Build();

    meterProvider = Sdk.CreateMeterProviderBuilder()
        .SetResourceBuilder(resourceBuilder)
        .AddMeter(CliTelemetry.SourceName)
        .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString)
        .Build();
}

// Build the host: register workloads, then build the DI container the
// command tree is composed from.
var hostBuilder = new FuncCliHostBuilder(interaction);
WorkloadRegistration.RegisterKnownWorkloads(hostBuilder);
await using var services = hostBuilder.Build();

// Create the command tree
var rootCommand = Parser.CreateCommand(services);

// Wire cancellation to Ctrl+C / SIGTERM
// First Ctrl+C: graceful shutdown. Second Ctrl+C: force exit.
using var cts = new CancellationTokenSource();
var ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    ctrlCCount++;
    if (ctrlCCount == 1)
    {
        e.Cancel = true;
        cts.Cancel();
    }
    else
    {
        // Second Ctrl+C — force exit immediately
        Environment.Exit(130);
    }
};

// Fire background version check (non-blocking, best-effort)
var versionCheckTask = VersionChecker.CheckForUpdateAsync(cts.Token);

// Parse and invoke asynchronously.
var commandName = ResolveCommandName(args);
var stopwatch = Stopwatch.StartNew();
int exitCode = 0;

using (Activity? activity = CliTelemetry.Trace.StartCommandActivity(commandName))
{
    try
    {
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        exitCode = await rootCommand.Parse(args).InvokeAsync(config, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // SIGINT — not a failure to record on the activity.
        exitCode = 130;
    }
    catch (GracefulException ex)
    {
        activity?.Fail(ex);
        interaction.WriteError(ex.Message);

        if (ex.VerboseMessage is not null)
        {
            interaction.WriteHint(ex.VerboseMessage);
        }

        exitCode = 1;
    }
    catch (Exception ex)
    {
        activity?.Fail(ex);
        interaction.WriteError($"An unexpected error occurred: {ex.Message}");
        exitCode = 1;
    }
    finally
    {
        stopwatch.Stop();
        CliTelemetry.Metric.RecordCommand(commandName, exitCode, stopwatch.ElapsedMilliseconds);
    }
} // activity disposed (and stopped) here, before flush

try
{
    // Print version update notice if available (bounded wait)
    await PrintVersionNotice(interaction, versionCheckTask);
}
finally
{
    // Flush + dispose the OTel providers explicitly. The bare ServiceProvider
    // doesn't own them (they were built outside DI), so we manage them here.
    tracerProvider?.ForceFlush(3000);
    meterProvider?.ForceFlush(3000);
    tracerProvider?.Dispose();
    meterProvider?.Dispose();
}

return exitCode;

/// <summary>
/// Resolves the command name from args for telemetry.
/// </summary>
static string ResolveCommandName(string[] args)
{
    if (args.Length == 0)
    {
        return "help";
    }

    // Take command args (skip options starting with -)
    var parts = new List<string>();
    foreach (var arg in args)
    {
        if (arg.StartsWith('-'))
        {
            break;
        }

        parts.Add(arg);

        // At most 2 levels (e.g., "workload install", "host start")
        if (parts.Count >= 2)
        {
            break;
        }
    }

    return parts.Count > 0 ? string.Join(" ", parts) : "unknown";
}

/// <summary>
/// Prints a version update notice if a newer version is available.
/// Waits at most 1 second for the background check to complete.
/// </summary>
static async Task PrintVersionNotice(IInteractionService interaction, Task<string?> versionCheckTask)
{
    try
    {
        // Wait at most 1 second for the background check
        var completed = await Task.WhenAny(versionCheckTask, Task.Delay(1000));
        if (completed != versionCheckTask)
        {
            return;
        }

        var latestVersion = await versionCheckTask;
        if (latestVersion is not null)
        {
            interaction.WriteBlankLine();
            interaction.WriteLine(l => l
                .Warning($"A newer version of Azure Functions Core Tools is available ({latestVersion})."));
            interaction.WriteLine(l => l
                .Muted("Update with: ")
                .Code("npm i -g azure-functions-core-tools@5 --unsafe-perm true"));
        }
    }
    catch
    {
        // Best-effort
    }
}
