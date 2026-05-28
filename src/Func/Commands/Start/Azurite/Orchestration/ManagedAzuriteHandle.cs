// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;

/// <summary>
/// Disposable wrapper around an <see cref="IAzuriteProcess"/> the CLI
/// launched. Disposal stops the process with a short grace timeout so a
/// <c>Ctrl+C</c> on <c>func start</c> never leaves orphaned native processes
/// or Docker containers behind.
/// </summary>
/// <remarks>
/// The handle is safe to construct for the no-process cases too
/// (<see cref="ManagedAzuriteResult.Disabled"/> or
/// <see cref="ManagedAzuriteResult.UserManaged"/>): disposal is a no-op so
/// callers can <c>await using</c> unconditionally.
/// </remarks>
internal sealed class ManagedAzuriteHandle : IAsyncDisposable
{
    private static readonly TimeSpan _defaultStopGrace = TimeSpan.FromSeconds(5);

    private readonly IAzuriteProcess? _process;
    private readonly TimeSpan _stopGrace;
    private bool _disposed;

    private ManagedAzuriteHandle(IAzuriteProcess? process, AzuriteLaunchMode? mode, TimeSpan stopGrace)
    {
        _process = process;
        _stopGrace = stopGrace;
        Mode = mode;
    }

    public AzuriteLaunchMode? Mode { get; }

    /// <summary>
    /// Handle for the no-process cases (user-managed or disabled). Disposal
    /// is a no-op.
    /// </summary>
    public static ManagedAzuriteHandle None() =>
        new(process: null, mode: null, stopGrace: _defaultStopGrace);

    /// <summary>
    /// Handle that owns <paramref name="process"/> and stops it on disposal.
    /// </summary>
    public static ManagedAzuriteHandle Owning(IAzuriteProcess process, AzuriteLaunchMode mode) =>
        new(process ?? throw new ArgumentNullException(nameof(process)), mode, _defaultStopGrace);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_process is null)
        {
            return;
        }

        // Best-effort graceful stop; the launcher already escalates to kill
        // after its internal grace timeout. Swallow exceptions during
        // shutdown so a launcher fault never masks the host's own outcome.
        using CancellationTokenSource cts = new(_stopGrace);
        try
        {
            await _process.StopAsync(cts.Token);
        }
        catch
        {
            // Intentional: disposal must not throw.
        }

        try
        {
            await _process.DisposeAsync();
        }
        catch
        {
            // Intentional: disposal must not throw.
        }
    }
}
