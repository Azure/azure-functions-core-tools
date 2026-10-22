// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateStoreTransactionState
{
    Ready,
    Committed,
    Restored,
    RecoveryRequired,
    NoOp,
}

/// <summary>
/// Runs one validated mutation under exclusive ownership, invalidating native capabilities before restoration.
/// </summary>
internal sealed class TemplateStoreWriteSession(TemplateStore store, TemplateEngineContext context, IDisposable lease,
    bool requireCleanStore = false) : IDisposable
{
    private readonly TemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TemplateEngineContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IDisposable _lease = lease ?? throw new ArgumentNullException(nameof(lease));
    private readonly bool _requireCleanStore = requireCleanStore;
    private readonly object _lifetimeGate = new();
    private LifetimeState _lifetime;

    public TemplateStoreTransactionState State { get; private set; }

    public bool RecoverySessionRecreated { get; private set; }

    /// <summary>
    /// Executes a mutation and its actual-content validation before rebuilding the persistent cache and committing.
    /// Failures propagate after restoration; inspect <see cref="State"/> for the recovery outcome.
    /// </summary>
    public async Task ExecuteAsync(Func<TemplateEngineSession, CancellationToken, Task> mutate,
        Func<TemplateEngineSession, CancellationToken, Task> validate, CancellationToken cancellationToken,
        Func<TemplateEngineSession, CancellationToken, Task<bool>>? admitBeforeSnapshot = null)
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
            if (_requireCleanStore)
            {
                _store.ThrowIfRecoveryRequired();
            }
            if (admitBeforeSnapshot is not null)
            {
                using TemplateEngineSession admission = await _store.CreateAdmissionEngineAsync(_context, cancellationToken);
                bool proceed = await admitBeforeSnapshot(admission, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!proceed)
                {
                    State = TemplateStoreTransactionState.NoOp;
                    return;
                }
            }
            if (_requireCleanStore)
            {
                _store.ThrowIfRecoveryRequired();
            }
            await _store.SnapshotAsync(cancellationToken);
            State = TemplateStoreTransactionState.RecoveryRequired;
            TemplateEngineSession? engine = null;
            List<Exception> failures = [];
            bool RecordFailure(Exception failure)
            {
                failures.Add(failure);
                // A false filter observes the failure without replacing its propagation or stack.
                return false;
            }

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
            catch (Exception failure) when (RecordFailure(failure))
            {
                throw;
            }
            finally
            {
                try
                {
                    engine?.Dispose();
                }
                catch (Exception failure) when (RecordFailure(failure))
                {
                    throw;
                }
                finally
                {
                    try
                    {
                        if (State == TemplateStoreTransactionState.RecoveryRequired)
                        {
                            await _store.RecoverAsync(CancellationToken.None);
                            State = TemplateStoreTransactionState.Restored;
                            using TemplateEngineSession recovered = await _store.CreateRecoveryEngineAsync(_context);
                            RecoverySessionRecreated = true;
                        }
                    }
                    catch (Exception failure) when (RecordFailure(failure))
                    {
                        throw;
                    }
                    finally
                    {
                        if (failures.Count > 1)
                        {
                            throw new AggregateException("The template-store operation encountered multiple failures.", failures);
                        }
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
