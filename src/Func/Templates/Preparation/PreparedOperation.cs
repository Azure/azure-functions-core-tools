// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Preparation;

/// <summary>
/// Owns one retained plan, a single create-only commit attempt, and observable asynchronous cleanup.
/// </summary>
internal sealed class PreparedOperation(PreparedOperationPlan plan, IFileSystem fileSystem, IAsyncDisposable? retainedResource = null) : IAsyncDisposable
{
    private readonly PreparedOperationPlan _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _commitFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<PreparedEffect> _completed = [];
    private readonly List<string> _stagingDirectories = [];
    private IReadOnlyList<Exception> _cleanupErrors = [];
    private Task<PreparedOperationCompletion>? _commitTask;
    private Task? _cleanupTask;
    private bool _disposeRequested;
    private PreparedCommitStatus _status;
    private PreparedCommitStage _stage;
    private PreparedEffect? _pendingEffect;

    public PreparedOperationState State
    {
        get
        {
            lock (_gate)
            {
                return _disposeRequested
                    ? _cleanupTask?.IsCompleted == true ? PreparedOperationState.Disposed : PreparedOperationState.Disposing
                    : _status switch
                    {
                        PreparedCommitStatus.NotStarted => PreparedOperationState.Prepared,
                        PreparedCommitStatus.InProgress => PreparedOperationState.Committing,
                        _ => PreparedOperationState.Completed,
                    };
            }
        }
    }

    public PreparedOperationCompletion Completion
    {
        get
        {
            lock (_gate)
            {
                PreparedCleanupStatus cleanupStatus = !_disposeRequested ? PreparedCleanupStatus.Pending
                    : _cleanupTask?.IsCompleted != true ? PreparedCleanupStatus.InProgress
                    : _cleanupTask.IsCompletedSuccessfully ? PreparedCleanupStatus.Succeeded : PreparedCleanupStatus.Failed;
                IEnumerable<Exception> errors = _cleanupErrors.Concat(_cleanupTask?.Exception is { } failure
                    ? failure.Flatten().InnerExceptions : Enumerable.Empty<Exception>()).Distinct();
                return new(_status, _stage, _completed, _pendingEffect, _commitTask?.Exception?.InnerException, cleanupStatus, errors);
            }
        }
    }

    /// <summary>
    /// Reads the same immutable plan before, during, or after commit without consuming the commit right.
    /// </summary>
    public PreparedOperationPlan Preview()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            return _plan;
        }
    }

    /// <summary>
    /// Consumes the only commit attempt, including when already canceled, and publishes retained bytes without replacement.
    /// </summary>
    /// <exception cref="InvalidOperationException">A commit has already been attempted.</exception>
    /// <exception cref="ObjectDisposedException">Disposal has been requested.</exception>
    /// <exception cref="IOException">A destination or ancestor is unsafe or occupied, or publication fails.</exception>
    /// <exception cref="UnauthorizedAccessException">Filesystem access is denied.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before an effect is published.</exception>
    /// <exception cref="AggregateException">Staging I/O and stream close both fail; the primary failure is recorded first.</exception>
    public Task<PreparedOperationCompletion> CommitAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_status != PreparedCommitStatus.NotStarted)
            {
                throw new InvalidOperationException("This prepared operation has already consumed its commit attempt. Inspect its completion before preparing another operation.");
            }

            _status = PreparedCommitStatus.InProgress;
            _commitTask = CommitCoreAsync(cancellationToken);
            return _commitTask;
        }
    }

    /// <summary>
    /// Prevents new use, waits for any active commit, and releases staging and retained resources once.
    /// Repeated calls observe the same cleanup task and failures.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_cleanupTask is null)
            {
                _disposeRequested = true;
                TaskCompletionSource<Task> cleanupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _cleanupTask = cleanupStarted.Task.Unwrap();
                cleanupStarted.SetResult(CleanupAsync(_status == PreparedCommitStatus.NotStarted ? Task.CompletedTask : _commitFinished.Task));
            }

            return new(_cleanupTask);
        }
    }

    private async Task<PreparedOperationCompletion> CommitCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetProgress(PreparedCommitStage.Preflight, null);
            foreach (PreparedDirectory directory in _plan.RequiredDirectories)
            {
                SetProgress(PreparedCommitStage.Preflight, new(directory.Path, PreparedOutputKind.Directory));
                cancellationToken.ThrowIfCancellationRequested();
                CheckParents(directory.Path);
                NeedsDirectory(directory);
            }

            foreach (PreparedOutputEntry entry in _plan.FilesToCommit)
            {
                PreparedEffect effect = GetEffect(entry);
                SetProgress(PreparedCommitStage.Preflight, effect);
                cancellationToken.ThrowIfCancellationRequested();
                CheckParents(effect.Path);
                RequireAbsent(effect.Path);
            }

            foreach (PreparedDirectory directory in _plan.RequiredDirectories)
            {
                SetProgress(PreparedCommitStage.Publication, new(directory.Path, PreparedOutputKind.Directory));
                cancellationToken.ThrowIfCancellationRequested();
                PublishDirectory(directory, cancellationToken);
            }

            foreach (PreparedOutputEntry entry in _plan.FilesToCommit)
            {
                PreparedEffect effect = GetEffect(entry);
                SetProgress(PreparedCommitStage.Publication, effect);
                cancellationToken.ThrowIfCancellationRequested();
                await PublishFileAsync(entry, effect, cancellationToken);
            }

            lock (_gate)
            {
                _status = PreparedCommitStatus.Succeeded;
                _stage = PreparedCommitStage.Complete;
                _pendingEffect = null;
            }

            // Cancellation after the final publication does not turn completed output into a canceled operation.
            return Completion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                _status = PreparedCommitStatus.Canceled;
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (_status == PreparedCommitStatus.InProgress)
                {
                    _status = PreparedCommitStatus.Failed;
                }
            }

            _commitFinished.TrySetResult();
        }
    }

    private PreparedEffect GetEffect(PreparedOutputEntry entry)
        => new(_plan.GetDestination(entry), entry.Kind, ReferenceEquals(entry, _plan.Configuration?.Entry));

    private void SetProgress(PreparedCommitStage stage, PreparedEffect? effect)
    {
        lock (_gate)
        {
            _stage = stage;
            _pendingEffect = effect;
        }
    }

    private bool NeedsDirectory(PreparedDirectory directory)
    {
        FileAttributes? attributes = _fileSystem.GetAttributesIfExists(directory.Path);
        if (attributes is null)
        {
            return true;
        }

        if (directory.MustBeNew || (attributes.Value & FileAttributes.Directory) == 0
            || (attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Prepared directory destination '{directory.Path}' is occupied or unsafe. Choose another destination and prepare a new operation.");
        }

        return false;
    }

    private void CheckParents(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        while (parent is not null)
        {
            FileAttributes? attributes = _fileSystem.GetAttributesIfExists(parent);
            if (attributes is not null && ((attributes.Value & FileAttributes.Directory) == 0
                || (attributes.Value & FileAttributes.ReparsePoint) != 0))
            {
                throw new IOException($"Publication ancestor '{parent}' is not an ordinary directory. Choose an unlinked directory without file collisions.");
            }

            parent = Path.GetDirectoryName(parent);
        }
    }

    private void RequireAbsent(string path)
    {
        if (_fileSystem.GetAttributesIfExists(path) is not null)
        {
            throw new IOException($"Prepared destination '{path}' already exists. Choose an unoccupied destination and prepare a new operation.");
        }
    }

    private string CreateStage(string destination)
    {
        string parent = Path.GetDirectoryName(destination)
            ?? throw new DirectoryNotFoundException($"The publication destination '{destination}' has no parent directory.");
        string stagingDirectory = _fileSystem.CreateStagingDirectory(parent);
        lock (_gate)
        {
            _stagingDirectories.Add(stagingDirectory);
        }

        return stagingDirectory;
    }

    private void PublishDirectory(PreparedDirectory directory, CancellationToken cancellationToken)
    {
        CheckParents(directory.Path);
        if (!NeedsDirectory(directory))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        CheckParents(directory.Path);
        RequireAbsent(directory.Path);
        _fileSystem.CreateNewDirectory(directory.Path);
        lock (_gate)
        {
            _completed.Add(new(directory.Path, PreparedOutputKind.Directory));
        }
    }

    private async Task PublishFileAsync(PreparedOutputEntry entry, PreparedEffect effect, CancellationToken cancellationToken)
    {
        CheckParents(effect.Path);
        RequireAbsent(effect.Path);
        string stagingDirectory = CreateStage(effect.Path);
        string stagingFile = Path.Combine(stagingDirectory, "content");
        Stream stream = _fileSystem.CreateNewFile(stagingFile);
        await WriteAndCloseStagingAsync(stream, entry.Content.AsMemory(), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        CheckParents(effect.Path);
        RequireAbsent(effect.Path);
        _fileSystem.PublishFileNoReplace(stagingFile, effect.Path);
        lock (_gate)
        {
            _completed.Add(effect);
        }
    }

    private static async Task WriteAndCloseStagingAsync(Stream stream, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Task writing = WriteContentAsync(stream, content, cancellationToken);
        OperationCanceledException? writeCancellation = null;
        try
        {
            await writing;
        }
        catch (OperationCanceledException exception)
        {
            writeCancellation = exception;
            throw;
        }
        finally
        {
            Task closing = CloseStreamAsync(stream);
            OperationCanceledException? closeCancellation = null;
            try
            {
                await closing;
            }
            catch (OperationCanceledException exception)
            {
                closeCancellation = exception;
                throw;
            }
            finally
            {
                Exception? writeFailure = writing.Exception?.InnerException ?? writeCancellation;
                Exception? closeFailure = closing.Exception?.InnerException ?? closeCancellation;
                if (writeFailure is not null && closeFailure is not null)
                {
                    throw new AggregateException("Writing prepared output and closing its staging stream both failed.", writeFailure, closeFailure);
                }
            }
        }

        static async Task WriteContentAsync(Stream output, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            await output.WriteAsync(bytes, token);
            await output.FlushAsync(token);
        }

        static async Task CloseStreamAsync(Stream output) => await output.DisposeAsync();
    }

    private async Task CleanupAsync(Task commitFinished)
    {
        await commitFinished;
        // Join blocking staging cleanup and asynchronous lease cleanup without allowing either failure to hide the other.
        var stagingCleanup = Task.Run(CleanupStagingDirectories);
        Task retainedCleanup = ReleaseRetainedResourceAsync();
        Task[] cleanupAttempts = [stagingCleanup, retainedCleanup];
        var allCleanup = Task.WhenAll(cleanupAttempts);
        try
        {
            await allCleanup;
        }
        finally
        {
            lock (_gate)
            {
                IEnumerable<Exception> failures = allCleanup.Exception is { } failure ? failure.Flatten().InnerExceptions : [];
                _cleanupErrors = Array.AsReadOnly<Exception>([.. failures
                    .Concat(cleanupAttempts.Where(task => task.IsCanceled).Select(task => new TaskCanceledException(task)))]);
            }
        }
    }

    private void CleanupStagingDirectories()
    {
        List<Exception> failures = [];
        foreach (string stagingDirectory in _stagingDirectories)
        {
            try
            {
                _fileSystem.DeleteDirectory(stagingDirectory);
            }
            catch (IOException exception)
            {
                failures.Add(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Prepared output staging cleanup failed.", failures);
        }
    }

    private async Task ReleaseRetainedResourceAsync()
    {
        if (retainedResource is not null)
        {
            await retainedResource.DisposeAsync();
        }
    }
}
