// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Handle to a running managed Azurite process. The orchestrator owns the
/// returned handle and is responsible for disposing it when the lifetime of
/// the emulator ends.
/// </summary>
internal interface IAzuriteProcess : IAsyncDisposable
{
    /// <summary>
    /// Operating-system process id of the launched process. For Docker mode
    /// this is the id of the <c>docker run</c> parent process, not the
    /// container.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Completes with the process exit code when the underlying process exits
    /// or when <paramref name="cancellationToken"/> is signaled.
    /// </summary>
    public Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort graceful stop. For Docker the launcher first issues
    /// <c>docker stop</c>; if that does not return in time the parent
    /// <c>docker run</c> process is killed. For native processes the v1
    /// implementation force-kills the process tree. Safe to call multiple
    /// times.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stream of stdout lines emitted by the process. Terminates when the
    /// process exits or when <paramref name="cancellationToken"/> is signaled.
    /// </summary>
    public IAsyncEnumerable<string> ReadStdoutLinesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stream of stderr lines emitted by the process. Terminates when the
    /// process exits or when <paramref name="cancellationToken"/> is signaled.
    /// </summary>
    public IAsyncEnumerable<string> ReadStderrLinesAsync(CancellationToken cancellationToken);
}
