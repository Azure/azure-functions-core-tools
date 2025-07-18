// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Colors.Net;

namespace Azure.Functions.Cli;

internal static class CancelKeyHandler
{
    private static readonly object _lock = new();
    private static IProcessManager _processManager;
    private static int _cancelKeyPressCount = 0;
    private static Action _onFirstCancel;
    private static Action _onSecondCancel;

    internal static void Register(IProcessManager processManager, Action onFirstCancel = null, Action onSecondCancel = null)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

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
                ColoredConsole.WriteLine("Press Ctrl+C again to force exit.");

                _onFirstCancel?.Invoke();

                // Cancel first Ctrl+C to allow graceful cleanup
                e.Cancel = true;

                _processManager.KillChildProcesses();
            }
            else if (_cancelKeyPressCount >= 2)
            {
                ColoredConsole.WriteLine("Forcing exit...");

                _onSecondCancel?.Invoke();

                Console.Out.Flush();
                Console.Error.Flush();

                // Hard exit
                _processManager.KillMainProcess();
            }
        }
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
