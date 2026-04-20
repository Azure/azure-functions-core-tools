// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

// Build the host with DI
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    DisableDefaults = true
});

builder.Services.AddSingleton<IInteractionService, SpectreInteractionService>();
builder.Services.AddSingleton<ITelemetry>(sp =>
{
    var client = new AppInsightsTelemetryClient();
    return client.IsEnabled ? client : new NoOpTelemetryClient();
});
builder.Services.AddSingleton<IWorkloadManager, WorkloadManager>();

var host = builder.Build();
var interaction = host.Services.GetRequiredService<IInteractionService>();
var telemetry = host.Services.GetRequiredService<ITelemetry>();
var workloadManager = host.Services.GetRequiredService<IWorkloadManager>();

// Create the command tree (including workload-contributed commands)
var rootCommand = Parser.CreateCommand(interaction, workloadManager);

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

// Parse and invoke asynchronously
var commandName = ResolveCommandName(args);
Activity? commandActivity = null;
var stopwatch = Stopwatch.StartNew();

try
{
    // Start telemetry span wrapping the entire command execution
    if (telemetry is AppInsightsTelemetryClient aiClient)
    {
        commandActivity = aiClient.StartCommandActivity(commandName);
    }

    var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    var exitCode = await rootCommand.Parse(args).InvokeAsync(config, cts.Token);

    stopwatch.Stop();
    telemetry.TrackCommand(commandName, isSuccess: exitCode == 0, durationMs: stopwatch.ElapsedMilliseconds);
    commandActivity?.Stop();

    // Print any workload update notices (non-blocking, best-effort)
    await workloadManager.PrintUpdateNoticesAsync();

    // Print version update notice if available (bounded wait)
    await PrintVersionNotice(interaction, versionCheckTask);

    return exitCode;
}
catch (OperationCanceledException)
{
    commandActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
    commandActivity?.Stop();
    return 130; // Standard Unix exit code for SIGINT
}
catch (GracefulException ex)
{
    stopwatch.Stop();
    telemetry.TrackCommand(commandName, isSuccess: false, durationMs: stopwatch.ElapsedMilliseconds);
    telemetry.TrackException(ex);
    commandActivity?.Stop();

    interaction.WriteError(ex.Message);

    if (ex.VerboseMessage is not null)
    {
        interaction.WriteMarkupLine($"[grey]{ex.VerboseMessage.EscapeMarkup()}[/]");
    }

    return ex.IsUserError ? 1 : 2;
}
catch (Exception ex)
{
    stopwatch.Stop();
    telemetry.TrackCommand(commandName, isSuccess: false, durationMs: stopwatch.ElapsedMilliseconds);
    telemetry.TrackException(ex);
    commandActivity?.Stop();

    interaction.WriteError($"An unexpected error occurred: {ex.Message}");
    return 2;
}
finally
{
    telemetry.Flush();
    (telemetry as IDisposable)?.Dispose();
    host.Dispose();
}

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
            interaction.WriteMarkupLine(
                $"[yellow]A newer version of Azure Functions Core Tools is available ({latestVersion}).[/]");
            interaction.WriteMarkupLine(
                "[grey]Update with:[/] [white]npm i -g azure-functions-core-tools@5 --unsafe-perm true[/]");
        }
    }
    catch
    {
        // Best-effort
    }
}
