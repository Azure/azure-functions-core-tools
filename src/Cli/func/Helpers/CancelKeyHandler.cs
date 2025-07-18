// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers;

public static class CancelKeyHandler
{
    private static readonly object _lock = new object();
    private static int _cancelKeyPressCount = 0;
    private static Action _onFirstCancel;

    public static void Register(Action onFirstCancel, int exitCode = 1)
    {
        _onFirstCancel = onFirstCancel ?? (() => { });

        Console.CancelKeyPress += HandleCancelKeyPress;
    }

    private static void HandleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        lock (_lock)
        {
            _cancelKeyPressCount++;

            if (_cancelKeyPressCount == 1)
            {
                ColoredConsole.WriteLine("Press Ctrl+C again to force exit.");

                // Cancel first Ctrl+C to allow graceful cleanup
                e.Cancel = true;
                _onFirstCancel?.Invoke();
            }
            else if (_cancelKeyPressCount >= 2)
            {
                ColoredConsole.WriteLine("Forcing exit...");
                Console.Out.Flush();
                Console.Error.Flush();

                // Hard exit
                Process.GetCurrentProcess().Kill();
            }
        }
    }
}
