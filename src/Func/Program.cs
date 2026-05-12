// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Telemetry;
using Microsoft.Extensions.Hosting;

DefaultTheme theme = new();
SpectreInteractionService interaction = new(theme);

// Wire cancellation to Ctrl+C / SIGTERM
// First Ctrl+C: graceful shutdown. Second Ctrl+C: force exit.
using var cts = new CancellationTokenSource();
int ctrlCCount = 0;
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
Task<string?> versionCheckTask = VersionChecker.CheckForUpdateAsync(cts.Token);

// Parse and invoke asynchronously.
string commandName = ResolveCommandName(args);
var stopwatch = Stopwatch.StartNew();
int exitCode = 0;

using (Activity? activity = CliTelemetry.Trace.StartCommandActivity(commandName))
{
    try
    {
        HostApplicationBuilder builder = CliHost.CreateBuilder(interaction);
        await builder.RegisterWorkloadsAsync(cts.Token);
        using IHost host = builder.Build();
        await host.StartAsync(cts.Token);

        FuncRootCommand rootCommand = Parser.CreateCommand(host.Services);

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
} // activity disposed (and stopped) here, before host shutdown flushes

// Print version update notice if available (bounded wait). Skip on
// user requested cancellation.
if (exitCode != 130)
{
    await PrintVersionNotice(interaction, versionCheckTask, cts.Token);
}

// Container disposal (triggered by `using var host` going out of scope)
// disposes the OTel providers, which flushes pending telemetry. No explicit
// flush needed here.
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
    foreach (string arg in args)
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
/// Waits at most 1 second for the background check to complete, or
/// returns immediately if <paramref name="cancellationToken"/> fires.
/// </summary>
static async Task PrintVersionNotice(IInteractionService interaction, Task<string?> versionCheckTask, CancellationToken cancellationToken)
{
    try
    {
        // Wait at most 1 second for the background check, but bail
        // immediately if cancellation has been requested.
        Task completed = await Task.WhenAny(versionCheckTask, Task.Delay(1000, cancellationToken));
        if (completed != versionCheckTask)
        {
            return;
        }

        string? latestVersion = await versionCheckTask;
        if (latestVersion is not null)
        {
            interaction.WriteBlankLine();
            interaction.WriteLine(l => l
                .Warning($"A newer version of Azure Functions CLI is available ({latestVersion})."));
        }
    }
    catch (Exception)
    {
        // Best-effort
    }
}
