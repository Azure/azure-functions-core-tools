// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli;

internal static class CancelKeyHandler
{
    private static readonly TimeSpan _gracefulShutdownPeriod = TimeSpan.FromSeconds(2);
    private static IProcessManager _processManager = null!;
    private static Action _onCancel;
    private static bool _registered = false;

    public static void Register(
        IProcessManager processManager,
        Action onCancel)
    {
        if (_registered)
        {
            return;
        }

        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _onCancel = onCancel ?? throw new ArgumentNullException(nameof(onCancel));

        Console.CancelKeyPress += HandleCancelKeyPress;
        _registered = true;
    }

    internal static void HandleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        _processManager.KillChildProcesses();
        _onCancel?.Invoke();

        _ = Task.Run(async () =>
        {
            await Task.Delay(_gracefulShutdownPeriod);
            _processManager.KillMainProcess();
        });
    }

    internal static void Dispose()
    {
        if (_registered)
        {
            Console.CancelKeyPress -= HandleCancelKeyPress;
            _registered = false;
        }
    }
}
