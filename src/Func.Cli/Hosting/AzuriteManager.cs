// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Net.Sockets;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Checks whether Azurite (local Azure Storage emulator) is running and helps
/// users get it started when UseDevelopmentStorage=true is configured.
/// </summary>
public class AzuriteManager
{
    private const int AzuriteBlobPort = 10000;
    private const int AzuriteQueuePort = 10001;
    private const int AzuriteTablePort = 10002;

    private readonly IInteractionService _interaction;

    public AzuriteManager(IInteractionService interaction)
    {
        _interaction = interaction;
    }

    /// <summary>
    /// Returns true if storage is configured to use Azurite (UseDevelopmentStorage=true).
    /// </summary>
    public static bool RequiresAzurite(Dictionary<string, string> env)
    {
        if (!env.TryGetValue("AzureWebJobsStorage", out var storage))
        {
            return false;
        }

        return storage.Equals("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if Azurite is reachable on its default ports.
    /// </summary>
    public static bool IsRunning()
    {
        return IsPortOpen(AzuriteBlobPort);
    }

    /// <summary>
    /// Ensures Azurite is running. If not, prompts the user to start it.
    /// Returns true if Azurite is running (or was successfully started), false to skip.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (IsRunning())
        {
            return true;
        }

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine(
            "[yellow]⚠ AzureWebJobsStorage is set to UseDevelopmentStorage=true but Azurite is not running.[/]");
        _interaction.WriteBlankLine();

        // Check if Azurite is available
        var azuriteAvailable = IsAzuriteInstalled();
        var dockerAvailable = IsDockerAvailable();

        if (!azuriteAvailable && !dockerAvailable)
        {
            _interaction.WriteMarkupLine("[grey]Azurite is not installed. Install it with one of:[/]");
            _interaction.WriteMarkupLine("  [white]npm install -g azurite[/]");
            _interaction.WriteMarkupLine("  [white]docker pull mcr.microsoft.com/azure-storage/azurite[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Then start it and re-run func start.[/]");
            _interaction.WriteMarkupLine("[grey]Continuing without Azurite — timer triggers, durable functions, and other storage-dependent features may not work.[/]");
            _interaction.WriteBlankLine();
            return false;
        }

        var choices = new List<string>();
        if (azuriteAvailable)
        {
            choices.Add("npx azurite (lightweight)");
        }
        if (dockerAvailable)
        {
            choices.Add("Docker container (isolated)");
        }
        choices.Add("No thanks, continue without Azurite");

        var selection = _interaction.PromptForSelectionAsync(
            "Azurite is needed for storage triggers. Start it?",
            choices,
            cancellationToken)
            .GetAwaiter().GetResult();

        if (selection.Contains("No thanks"))
        {
            _interaction.WriteMarkupLine("[grey]Continuing without Azurite — timer triggers, durable functions, and other storage-dependent features may not work.[/]");
            _interaction.WriteBlankLine();
            return false;
        }

        var choice = selection.Contains("npx") ? "npx" : "docker";

        return choice == "npx"
            ? await StartWithNpxAsync(cancellationToken)
            : await StartWithDockerAsync(cancellationToken);
    }

    private async Task<bool> StartWithNpxAsync(CancellationToken cancellationToken)
    {
        _interaction.WriteMarkupLine("[grey]Starting Azurite...[/]");

        var dataDir = Path.Combine(HostResolver.GetDataDirectory(), "azurite");
        Directory.CreateDirectory(dataDir);

        var psi = new ProcessStartInfo
        {
            FileName = "npx",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("azurite");
        psi.ArgumentList.Add("--location");
        psi.ArgumentList.Add(dataDir);
        psi.ArgumentList.Add("--silent");

        try
        {
            var process = Process.Start(psi);
            if (process is null)
            {
                _interaction.WriteError("Failed to start Azurite.");
                return false;
            }

            // Wait briefly for it to start listening
            await Task.Delay(2000, cancellationToken);

            if (process.HasExited)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                _interaction.WriteError($"Azurite exited unexpectedly: {stderr}");
                return false;
            }

            if (IsRunning())
            {
                _interaction.WriteMarkupLine($"[green]✓[/] Azurite started (blob:10000, queue:10001, table:10002). PID: {process.Id}");
                _interaction.WriteBlankLine();
                return true;
            }

            _interaction.WriteWarning($"Azurite process started (PID: {process.Id}) but ports are not yet available. Continuing...");
            return true;
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Failed to start Azurite: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> StartWithDockerAsync(CancellationToken cancellationToken)
    {
        _interaction.WriteMarkupLine("[grey]Starting Azurite via Docker...[/]");

        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add("func-azurite");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add($"{AzuriteBlobPort}:{AzuriteBlobPort}");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add($"{AzuriteQueuePort}:{AzuriteQueuePort}");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add($"{AzuriteTablePort}:{AzuriteTablePort}");
        psi.ArgumentList.Add("mcr.microsoft.com/azure-storage/azurite");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                _interaction.WriteError("Failed to start Docker container.");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                // Container might already exist — try starting it
                if (stderr.Contains("already in use"))
                {
                    return await StartExistingDockerContainerAsync(cancellationToken);
                }
                _interaction.WriteError($"Docker failed: {stderr}");
                return false;
            }

            // Wait for ports
            await Task.Delay(2000, cancellationToken);

            if (IsRunning())
            {
                _interaction.WriteMarkupLine("[green]✓[/] Azurite started via Docker container [white]func-azurite[/] (blob:10000, queue:10001, table:10002).");
                _interaction.WriteMarkupLine("[grey]  Stop with: docker stop func-azurite[/]");
                _interaction.WriteBlankLine();
                return true;
            }

            _interaction.WriteWarning("Docker container 'func-azurite' started but Azurite is not responding yet. Continuing...");
            return true;
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Failed to start Azurite via Docker: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> StartExistingDockerContainerAsync(CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("start");
        psi.ArgumentList.Add("func-azurite");

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync(cancellationToken);
            await Task.Delay(1500, cancellationToken);

            if (IsRunning())
            {
                _interaction.WriteMarkupLine("[green]✓[/] Azurite Docker container restarted.");
                _interaction.WriteBlankLine();
                return true;
            }
        }
        catch { /* best effort */ }

        return false;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            if (connected)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAzuriteInstalled()
    {
        return HostConfiguration.FindExecutableOnPath("azurite") is not null ||
               HostConfiguration.FindExecutableOnPath("npx") is not null;
    }

    private static bool IsDockerAvailable()
    {
        return HostConfiguration.FindExecutableOnPath("docker") is not null;
    }
}
