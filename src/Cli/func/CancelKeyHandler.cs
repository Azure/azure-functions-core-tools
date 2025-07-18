// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;

namespace Azure.Functions.Cli;

internal static class CancelKeyHandler
{
    private static readonly object _lock = new();
    private static IProcessManager _processManager;
    private static IConsoleReader _consoleReader;
    private static Action _onFirstCancel;
    private static Action _onSecondCancel;
    private static int _cancelKeyPressCount = 0;

    internal static void Register(IProcessManager processManager, IConsoleReader consoleReader, Action onFirstCancel = null, Action onSecondCancel = null)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _consoleReader = consoleReader ?? throw new ArgumentNullException(nameof(consoleReader));

        _onFirstCancel = onFirstCancel ?? (() => { });
        _onSecondCancel = onSecondCancel ?? (() => { });

        Console.CancelKeyPress += HandleCancelKeyPress;
    }

    internal static void HandleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        lock (_lock)
        {
            _cancelKeyPressCount++;

            if (_cancelKeyPressCount == 1)
            {
                var message = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? "Press 'q' to force exit."
                            : "Press Ctrl+C again to force exit.";

                ColoredConsole.WriteLine(message);

                _onFirstCancel?.Invoke();

                // Cancel first Ctrl+C to allow graceful cleanup
                e.Cancel = true;

                _processManager.KillChildProcesses();

                // Windows locks up the main thread processing the first Ctrl+C.
                // Start force-kill fallback that listens for `q` key press
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Task.Run(() =>
                    {
                        while (true)
                        {
                            var key = _consoleReader.ReadKey(true);
                            if (key.Key == ConsoleKey.Q)
                            {
                                ForceQuit();
                            }
                        }
                    });
                }
            }
            else if (_cancelKeyPressCount >= 2)
            {
                ForceQuit();
            }
        }
    }

    private static void ForceQuit()
    {
        ColoredConsole.WriteLine("Forcing exit...");

        _onSecondCancel?.Invoke();

        Console.Out.Flush();
        Console.Error.Flush();

        // Hard exit
        _processManager.KillMainProcess();
    }

    // Dispose method to unregister the event handler and reset state (for testing)
    internal static void Dispose()
    {
        Console.CancelKeyPress -= HandleCancelKeyPress;
        _cancelKeyPressCount = 0;
        _processManager = null;
        _onFirstCancel = null;
        _onSecondCancel = null;
    }
}
