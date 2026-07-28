// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <inheritdoc cref="IListeningProcessInspector" />
internal sealed class ListeningProcessInspector(
    IProcessRunner runner,
    IPortOwnershipStrategy strategy,
    ILogger<ListeningProcessInspector> logger) : IListeningProcessInspector
{
    private static readonly TimeSpan _lookupTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly IPortOwnershipStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly ILogger<ListeningProcessInspector> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<ListeningProcessInfo>> GetListeningProcessesAsync(int port, CancellationToken cancellationToken)
    {
        (string listenerFile, IReadOnlyList<string> listenerArgs) = _strategy.BuildListenerLookup(port);
        ProcessOutcome? listener = await TryRunAsync(listenerFile, listenerArgs, cancellationToken);
        if (listener is null || listener.ExecutableNotFound || listener.TimedOut)
        {
            return [];
        }

        IReadOnlyList<int> pids = _strategy.ParseListenerPids(listener.StandardOutput, port);
        if (pids.Count == 0)
        {
            return [];
        }

        // Resolve command lines concurrently so total latency is bounded by the
        // slowest lookup rather than the sum across listeners.
        return await Task.WhenAll(pids.Select(pid => ResolveCommandLineAsync(pid, cancellationToken)));
    }

    private async Task<ListeningProcessInfo> ResolveCommandLineAsync(int processId, CancellationToken cancellationToken)
    {
        (string commandFile, IReadOnlyList<string> commandArgs) = _strategy.BuildCommandLineLookup(processId);
        ProcessOutcome? outcome = await TryRunAsync(commandFile, commandArgs, cancellationToken);
        string? commandLine = outcome is null || outcome.ExecutableNotFound || outcome.TimedOut
            ? null
            : _strategy.ParseCommandLine(outcome.StandardOutput);

        return new ListeningProcessInfo(processId, commandLine ?? string.Empty);
    }

    private async Task<ProcessOutcome?> TryRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessRunRequest request = new(fileName, arguments, WorkingDirectory: null, Timeout: _lookupTimeout);
        try
        {
            return await _runner.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort diagnostics: probing the OS for the owning process must
            // never break `func start`. Any failure (missing tool, denied query,
            // unexpected output) degrades to "could not identify", which the
            // caller treats as adopt-and-log.
            _logger.LogDebug(ex, "Process lookup '{FileName}' failed while inspecting the listening process.", fileName);
            return null;
        }
    }
}
