// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Commands.Start;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Telemetry;
using Microsoft.Extensions.DependencyInjection;
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
UnexpectedErrorReporter unexpectedErrors = new(interaction);

// Wire cancellation to Ctrl+C / SIGTERM
// First Ctrl+C: graceful shutdown. Second Ctrl+C: force exit.
using var cts = new CancellationTokenSource();
ConsoleInterruptCoordinator interrupts = new(cts.Cancel, Environment.Exit);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    interrupts.RequestStop();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => interrupts.RestoreTerminal();

// Leave advisories disabled until the parsed command establishes its output contract.
Task<string?> versionCheckTask = Task.FromResult<string?>(null);
bool allowAdvisories = false;

var stopwatch = Stopwatch.StartNew();
int exitCode = 0;

// Best-effort fallback for the metric tag when we fail before the parse
// resolves a command path (e.g. host startup blows up). The activity gets
// no `cli.command.name` tag at all in that window, but the metric needs a
// non-null value.
string commandName = "unknown";

FuncAliasNudge? aliasNudge = null;
using (Activity? activity = CliTelemetry.Trace.StartCommandActivity())
{
    FuncRootCommand? rootCommand = null;
    ParseResult? commandParseResult = null;
    try
    {
        using IHost host = await CliHostFactory.CreateHostAsync(interaction, cts.Token, interrupts);
        await host.StartAsync(cts.Token);

        // Capture nudge before host disposal so it can run after the
        // version notice. Its dependencies (interaction, version
        // provider) are stateless singletons that outlive the host.
        aliasNudge = host.Services.GetRequiredService<FuncAliasNudge>();

        rootCommand = Parser.CreateCommand(host.Services);
        string[] normalizedArgs = LegacyStartArgumentNormalizer.Normalize(args);

        // Disable POSIX bundling so single-dash typos like `-name` surface as unrecognized options.
        var parserConfiguration = new ParserConfiguration { EnablePosixBundling = false };
        commandParseResult = rootCommand.Parse(normalizedArgs, parserConfiguration);
        commandName = CommandNameResolver.ResolveCommandName(commandParseResult, rootCommand);
        allowAdvisories = !string.Equals(commandName, "update", StringComparison.OrdinalIgnoreCase)
            && !SetupCommand.ShouldSuppressAdvisories(commandParseResult);
        if (allowAdvisories)
        {
            versionCheckTask = VersionChecker.CheckForUpdateAsync(cts.Token);
        }

        activity?.SetCommandName(commandName);

        IFirstRunCoordinator firstRunCoordinator = host.Services.GetRequiredService<IFirstRunCoordinator>();
        int? firstRunExitCode = await firstRunCoordinator.EnsureFirstRunPromptedAsync(commandName, commandParseResult, cts.Token);
        if (firstRunExitCode is int firstRunResult)
        {
            exitCode = firstRunResult;
        }
        else
        {
            var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
            bool ownsTerminal = commandParseResult.CommandResult.Command is StartCommand startCommand
                && startCommand.UsesFullScreen(commandParseResult);
            if (ownsTerminal)
            {
                config.ProcessTerminationTimeout = null;
            }

            using PosixSignalRegistration? termination = ownsTerminal
                ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    interrupts.RequestStop(143);
                })
                : null;
            exitCode = await commandParseResult.InvokeAsync(config, cts.Token);
        }
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

        if (ex.VerboseMessage is not null
            && rootCommand is not null
            && commandParseResult?.GetValue(rootCommand.VerboseOption) is true)
        {
            interaction.WriteHint(ex.VerboseMessage);
        }

        exitCode = 1;
    }
    catch (Exception ex)
    {
        activity?.Fail(ex);
        unexpectedErrors.Report(ex, rootCommand is not null && commandParseResult?.GetValue(rootCommand.VerboseOption) is true);
        exitCode = 1;
    }
    finally
    {
        stopwatch.Stop();
        CliTelemetry.Metric.RecordCommand(commandName, exitCode, stopwatch.ElapsedMilliseconds);
    }
} // activity disposed (and stopped) here, before host shutdown flushes

// Skip trailing advisories for check/JSON setup, including after failures.
// Other commands retain the bounded version notice unless cancelled.
if (allowAdvisories && exitCode != 130)
{
    await PrintVersionNotice(interaction, versionCheckTask, cts.Token);
    aliasNudge?.TryPrint(exitCode, commandName);
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
