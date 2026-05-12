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

// Force UTF-8 stdout so glyphs like →, ●, ✗, ◉ render correctly on Windows
// consoles (which default to OEM/ANSI code pages that map them to '?').
// Safe on non-Windows platforms: the runtime is already UTF-8 there, this is
// effectively a no-op. Some legacy terminals throw on Out/Error encoding
// changes (e.g., redirected to a pipe that doesn't allow it), so guard.
try
{
    System.Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (System.IO.IOException)
{
    // Output isn't a real console (e.g., closed handle) — proceed regardless.
}

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

var stopwatch = Stopwatch.StartNew();
int exitCode = 0;

// Best-effort fallback for the metric tag when we fail before the parse
// resolves a command path (e.g. host startup blows up). The activity gets
// no `cli.command.name` tag at all in that window, but the metric needs a
// non-null value.
string commandName = "unknown";

using (Activity? activity = CliTelemetry.Trace.StartCommandActivity())
{
    try
    {
        using IHost host = await CliHostFactory.CreateHostAsync(interaction, cts.Token);
        await host.StartAsync(cts.Token);

        FuncRootCommand rootCommand = Parser.CreateCommand(host.Services);

        ParseResult commandParseResult = rootCommand.Parse(args);
        commandName = CommandNameResolver.ResolveCommandName(commandParseResult, rootCommand);
        activity?.SetCommandName(commandName);

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        exitCode = await commandParseResult.InvokeAsync(config, cts.Token);
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
