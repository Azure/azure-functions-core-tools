// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateStoreTransactionState
{
    Ready,
    Committed,
    Restored,
    RecoveryRequired,
}

/// <summary>
/// Runs one validated mutation under exclusive ownership, invalidating native capabilities before restoration.
/// </summary>
internal sealed class TemplateStoreWriteSession(TemplateStore store, TemplateEngineContext context, IDisposable lease) : IDisposable
{
    private readonly TemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TemplateEngineContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IDisposable _lease = lease ?? throw new ArgumentNullException(nameof(lease));
    private readonly object _lifetimeGate = new();
    private LifetimeState _lifetime;

    public TemplateStoreTransactionState State { get; private set; }

    public bool RecoverySessionRecreated { get; private set; }

    /// <summary>
    /// Executes a mutation and its actual-content validation before rebuilding the persistent cache and committing.
    /// Failures propagate after restoration; inspect <see cref="State"/> for the recovery outcome.
    /// </summary>
    public async Task ExecuteAsync(Func<TemplateEngineSession, CancellationToken, Task> mutate,
        Func<TemplateEngineSession, CancellationToken, Task> validate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(validate);
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_lifetime == LifetimeState.Disposed, this);
            if (_lifetime != LifetimeState.Ready)
            {
                throw new InvalidOperationException("A template-store write session permits only one transaction. Open a fresh session to retry.");
            }

            _lifetime = LifetimeState.Running;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.SnapshotAsync(cancellationToken);
            State = TemplateStoreTransactionState.RecoveryRequired;
            TemplateEngineSession? engine = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                engine = _store.CreateWriteEngine(_context);
                cancellationToken.ThrowIfCancellationRequested();
                await mutate(engine, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await validate(engine, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await engine.RebuildCacheAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                engine.Dispose();
                engine = null;
                _store.Commit();
                State = TemplateStoreTransactionState.Committed;
            }
            finally
            {
                try
                {
                    engine?.Dispose();
                }
                finally
                {
                    if (State == TemplateStoreTransactionState.RecoveryRequired)
                    {
                        await _store.RecoverAsync(CancellationToken.None);
                        State = TemplateStoreTransactionState.Restored;
                        using TemplateEngineSession recovered = await _store.CreateRecoveryEngineAsync(_context);
                        RecoverySessionRecreated = true;
                    }
                }
            }
        }
        finally
        {
            lock (_lifetimeGate)
            {
                _lifetime = LifetimeState.Finished;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_lifetime == LifetimeState.Running)
            {
                throw new InvalidOperationException("Await the template-store transaction and recovery before disposing its exclusive lease.");
            }

            if (_lifetime == LifetimeState.Disposed)
            {
                return;
            }

            _lifetime = LifetimeState.Disposed;
        }

        _lease.Dispose();
    }

    private enum LifetimeState
    {
        Ready,
        Running,
        Finished,
        Disposed,
    }
}
