// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Uses a persistent, OS-owned exclusive file handle. An abandoned dirty intent blocks writers instead of guessing payload recovery.
/// </summary>
internal sealed class FileWorkloadOperationCoordinator(IWorkloadPaths paths) : IWorkloadOperationCoordinator
{
    internal const string ControlDirectoryName = ".workload-operations";
    internal const string IntentFileName = "dirty.intent";
    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string home = ResolvePhysicalPath(_paths.Home);
        string registry = ResolvePhysicalPath(_paths.WorkloadRegistryPath);
        if (!SamePath(Path.GetDirectoryName(registry)!, home))
        {
            throw new ArgumentException("The workload registry must belong to the configured workload home.", nameof(paths));
        }

        string control = Path.Combine(home, ControlDirectoryName);
        Directory.CreateDirectory(control);
        string lockPath = Path.Combine(control, "operation.lock");
        bool reported = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? owner = TryAcquire(lockPath);
            if (owner is null)
            {
                if (!reported)
                {
                    waiting?.Invoke();
                    reported = true;
                }

                await Task.Delay(25, cancellationToken);
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using FileStream? probe = TryAcquire(lockPath);
                if (probe is not null)
                {
                    throw new WorkloadLockNotSupportedException();
                }

                string intent = Path.Combine(control, IntentFileName);
                if (File.Exists(intent))
                {
                    throw new WorkloadRecoveryRequiredException(intent);
                }

                return new Operation(this, owner, home, registry, intent);
            }
            catch
            {
                owner.Dispose();
                throw;
            }
        }
    }

    private FileStream? TryAcquire(string path)
    {
        try
        {
            return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
        }
        catch (IOException exception) when (IsContention(exception))
        {
            return null;
        }
        catch (IOException exception) when ((OperatingSystem.IsLinux() && exception.HResult == 95)
            || (OperatingSystem.IsMacOS() && exception.HResult == 45))
        {
            throw new WorkloadLockNotSupportedException(exception);
        }
    }

    private static bool IsContention(IOException exception)
        => OperatingSystem.IsWindows() ? exception.HResult == unchecked((int)0x80070020)
            : (OperatingSystem.IsLinux() && exception.HResult == 11) || (OperatingSystem.IsMacOS() && exception.HResult == 35);

    private string ResolvePhysicalPath(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        foreach (string segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            DirectoryInfo directory = new(current);
            if (directory.Exists && directory.LinkTarget is not null)
            {
                current = directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class Operation(FileWorkloadOperationCoordinator coordinator, FileStream owner, string home, string registry, string intent)
        : IWorkloadOperation
    {
        private bool _disposed;
        private bool _dirty;
        public bool IsDirty => _dirty;

        public void Validate(IWorkloadPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!SamePath(home, coordinator.ResolvePhysicalPath(paths.Home))
                || !SamePath(registry, coordinator.ResolvePhysicalPath(paths.WorkloadRegistryPath)))
            {
                throw new ArgumentException("The operation belongs to a different workload store.", nameof(paths));
            }
        }

        public void BeginMutation()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_dirty)
            {
                return;
            }

            // Even a truncated intent is a recovery gate. Flush it before the first live directory or registry mutation.
            using FileStream marker = new(intent, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            marker.Write(Encoding.UTF8.GetBytes($"workload-operation-v1\n{Guid.NewGuid():N}\n{home}\n{registry}\n"));
            marker.Flush(flushToDisk: true);
            _dirty = true;
        }

        public void CompleteMutation()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_dirty)
            {
                File.Delete(intent);
                _dirty = false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Dispose();
            }
        }
    }
}
