// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Build the host with DI
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    DisableDefaults = true
});

builder.Services.AddSingleton<ITheme, DefaultTheme>();
builder.Services.AddSingleton<IInteractionService, SpectreInteractionService>();

var host = builder.Build();
var interaction = host.Services.GetRequiredService<IInteractionService>();

// Create the command tree
var rootCommand = Parser.CreateCommand(interaction);

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
try
{
    var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    var exitCode = await rootCommand.Parse(args).InvokeAsync(config, cts.Token);

    // Print version update notice if available (bounded wait)
    await PrintVersionNotice(interaction, versionCheckTask);

    return exitCode;
}
catch (OperationCanceledException)
{
    return 130; // Standard Unix exit code for SIGINT
}
catch (GracefulException ex)
{
    interaction.WriteError(ex.Message);

    if (ex.VerboseMessage is not null)
    {
        interaction.WriteHint(ex.VerboseMessage);
    }

    return ex.IsUserError ? 1 : 2;
}
catch (Exception ex)
{
    interaction.WriteError($"An unexpected error occurred: {ex.Message}");
    return 2;
}
finally
{
    host.Dispose();
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
