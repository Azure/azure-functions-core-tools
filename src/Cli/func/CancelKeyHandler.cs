// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli;

internal static class CancelKeyHandler
{
    private static readonly TimeSpan _gracefulShutdownPeriod = TimeSpan.FromSeconds(2);
    private static readonly ConsoleCancelEventHandler _handlerDelegate = HandleCancelKeyPress;
    private static Action _onShuttingDown;
    private static Action _onGracePeriodTimeout;
    private static bool _registered = false;
    private static bool _shutdownStarted = false;

    public static void Register(Action onShuttingDown = null, Action onGracePeriodTimeout = null)
    {
        if (_registered)
        {
            return;
        }

        _onShuttingDown = onShuttingDown ?? (() => { });
        _onGracePeriodTimeout = onGracePeriodTimeout ?? (() => { });

        Console.CancelKeyPress += _handlerDelegate;
        _registered = true;
    }

    internal static void HandleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _onShuttingDown?.Invoke();

        _ = Task.Run(async () =>
        {
            await Task.Delay(_gracefulShutdownPeriod);
            _onGracePeriodTimeout?.Invoke();
        });
    }

    // For testing purposes, we need to ensure that the handler can be disposed of properly.
    internal static void Dispose()
    {
        if (_registered)
        {
            Console.CancelKeyPress -= _handlerDelegate;
            _registered = false;
        }

        _shutdownStarted = false;
        _onShuttingDown = null;
        _onGracePeriodTimeout = null;
    }
}
