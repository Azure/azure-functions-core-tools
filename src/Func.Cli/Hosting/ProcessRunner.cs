// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Manages the lifecycle of a child process with proper signal forwarding
/// and cleanup. Adapted from the ProcessReaper pattern in dotnet/sdk.
/// </summary>
public sealed class ProcessRunner : IDisposable
{
    private readonly Process _process;
    private readonly IInteractionService _interaction;
    private readonly bool _verbose;
    private readonly Func<string, bool>? _outputFilter;
    private bool _disposed;

    /// <param name="outputFilter">
    /// Optional filter for stdout lines. If provided, stdout is redirected and each line
    /// is passed to the filter. Return true to echo the line to the terminal, false to suppress it.
    /// If null, stdout flows directly to the terminal without redirection.
    /// </param>
    public ProcessRunner(ProcessStartInfo startInfo, IInteractionService interaction, bool verbose = false, Func<string, bool>? outputFilter = null)
    {
        _interaction = interaction;
        _verbose = verbose;
        _outputFilter = outputFilter;

        if (_outputFilter is not null)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // Register signal handlers before starting the process to avoid races
        System.Console.CancelKeyPress += HandleCancelKeyPress;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AppDomain.CurrentDomain.ProcessExit += HandleProcessExit;
        }
    }

    /// <summary>
    /// Starts the child process and blocks until it exits.
    /// Returns the process exit code.
    /// </summary>
    public int Run()
    {
        if (_verbose)
        {
            _interaction.WriteMarkupLine($"[grey]> {EscapeMarkup(_process.StartInfo.FileName)} {EscapeMarkup(string.Join(' ', _process.StartInfo.ArgumentList))}[/]");
        }

        _process.Start();

        _interaction.WriteMarkupLine($"[grey]Host process started (PID: {_process.Id})[/]");
        _interaction.WriteMarkupLine("[grey]Press Ctrl+C to stop the host.[/]");
        _interaction.WriteBlankLine();

        if (_outputFilter is not null)
        {
            ReadFilteredOutput();
        }

        _process.WaitForExit();

        LogVerbose($"Host process exited with code {_process.ExitCode}");

        return _process.ExitCode;
    }

    private void ReadFilteredOutput()
    {
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && _outputFilter!(e.Data))
            {
                System.Console.Error.WriteLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();

        var reader = _process.StandardOutput;
        while (reader.ReadLine() is { } line)
        {
            if (_outputFilter!(line))
            {
                System.Console.WriteLine(line);
            }
        }
    }

    private void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
    }

    private void HandleProcessExit(object? sender, EventArgs e)
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: false);
                _process.WaitForExit(TimeSpan.FromSeconds(10));

                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }

            Environment.ExitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // Process already exited or never started
        }
    }

    private void LogVerbose(string message)
    {
        if (_verbose)
        {
            _interaction.WriteMarkupLine($"[grey]{EscapeMarkup(message)}[/]");
        }
    }

    private static string EscapeMarkup(string text) => Spectre.Console.Markup.Escape(text);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        System.Console.CancelKeyPress -= HandleCancelKeyPress;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AppDomain.CurrentDomain.ProcessExit -= HandleProcessExit;
        }

        _process.Dispose();
    }
}
