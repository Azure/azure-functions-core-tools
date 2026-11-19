// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Shares graceful shutdown and escalation between raw terminal keys and external signals.
/// </summary>
internal sealed class ConsoleInterruptCoordinator(Action cancel, Action<int> forceExit)
{
    private readonly Action _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
    private readonly Action<int> _forceExit = forceExit ?? throw new ArgumentNullException(nameof(forceExit));
    private Action? _restore;
    private int _stopping;

    public bool IsStopping => Volatile.Read(ref _stopping) != 0;

    public void MarkStopping() => Interlocked.Exchange(ref _stopping, 1);

    public void RequestStop(int forcedExitCode = 130)
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _cancel();
            return;
        }

        try
        {
            RestoreTerminal();
        }
        finally
        {
            _forceExit(forcedExitCode);
        }
    }

    public void RestoreTerminal() => Volatile.Read(ref _restore)?.Invoke();

    public IDisposable RegisterTerminal(Action restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        if (Interlocked.CompareExchange(ref _restore, restore, null) is not null)
        {
            throw new InvalidOperationException("A terminal session already owns interrupt restoration.");
        }

        return new Registration(this, restore);
    }

    private sealed class Registration(ConsoleInterruptCoordinator owner, Action restore) : IDisposable
    {
        public void Dispose() => Interlocked.CompareExchange(ref owner._restore, null, restore);
    }
}
