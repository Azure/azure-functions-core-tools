// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PackageIntake;

internal enum TemplatePackageEntryKind
{
    File,
    Directory,
}

internal enum TemplatePackageSource
{
    LocalArchive,
}

internal enum TemplatePackageTrust
{
    Unqualified,
}

internal enum TemplatePackageCleanupOutcome
{
    Active,
    Released,
}

internal sealed record TemplatePackageInventoryEntry(string Path, TemplatePackageEntryKind Kind, long Length, string? Sha256);

/// <summary>
/// Owns the retained archive that passed validation. Disposal revokes every reader and releases its intake slot.
/// </summary>
internal sealed class TemplatePackageContentLease : IDisposable
{
    private readonly object _gate = new();
    private readonly Action _release;
    private MemoryStream? _archive;

    internal TemplatePackageContentLease(
        MemoryStream archive,
        string packageId,
        string version,
        IEnumerable<string> packageTypes,
        string sha256,
        IEnumerable<TemplatePackageInventoryEntry> inventory,
        long expandedBytes,
        long allocatedBufferBytes,
        Action release)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentNullException.ThrowIfNull(packageTypes);
        ArgumentNullException.ThrowIfNull(inventory);
        PackageId = packageId;
        Version = version;
        string[] capturedTypes = [.. packageTypes];
        PackageTypes = Array.AsReadOnly(capturedTypes);
        Sha256 = sha256;
        TemplatePackageInventoryEntry[] capturedInventory = [.. inventory];
        Inventory = Array.AsReadOnly(capturedInventory);
        ArchiveBytes = archive.Length;
        RetainedBufferBytes = archive.Capacity;
        ExpandedBytes = expandedBytes;
        AllocatedBufferBytes = allocatedBufferBytes;
    }

    public string PackageId { get; }

    public string Version { get; }

    public IReadOnlyList<string> PackageTypes { get; }

    public string Sha256 { get; }

    public long ArchiveBytes { get; }

    public long ExpandedBytes { get; }

    public long RetainedBufferBytes { get; }

    /// <summary>
    /// Counts scratch, manifest, and all archive-buffer capacity allocations, including replaced buffers.
    /// </summary>
    public long AllocatedBufferBytes { get; }

    public IReadOnlyList<TemplatePackageInventoryEntry> Inventory { get; }

    public TemplatePackageSource Source => TemplatePackageSource.LocalArchive;

    public TemplatePackageTrust Trust => TemplatePackageTrust.Unqualified;

    public TemplatePackageCleanupOutcome CleanupOutcome
    {
        get
        {
            lock (_gate)
            {
                return _archive is null ? TemplatePackageCleanupOutcome.Released : TemplatePackageCleanupOutcome.Active;
            }
        }
    }

    /// <summary>
    /// Opens an independent read-only archive stream. The stream cannot outlive this lease.
    /// </summary>
    public Stream OpenRead()
    {
        EnsureActive();
        return new LeaseReadStream(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_archive is null)
            {
                return;
            }

            _archive.Dispose();
            _archive = null;
            _release();
        }
    }

    private void EnsureActive()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
        }
    }

    private int Read(long position, Span<byte> destination)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            int count = (int)Math.Min(destination.Length, Math.Max(0, ArchiveBytes - position));
            if (count > 0)
            {
                _archive.GetBuffer().AsSpan((int)position, count).CopyTo(destination);
            }

            return count;
        }
    }

    private sealed class LeaseReadStream(TemplatePackageContentLease owner) : Stream
    {
        private readonly TemplatePackageContentLease _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        private long _position;
        private bool _disposed;

        public override bool CanRead => !_disposed && _owner.CleanupOutcome == TemplatePackageCleanupOutcome.Active;

        public override bool CanSeek => CanRead;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                EnsureActive();
                return _owner.ArchiveBytes;
            }
        }

        public override long Position
        {
            get
            {
                EnsureActive();
                return _position;
            }

            set
            {
                EnsureActive();
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureActive();
            int count = _owner.Read(_position, buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureActive();
            long basis = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            long position;
            try
            {
                position = checked(basis + offset);
            }
            catch (OverflowException exception)
            {
                throw new IOException("The requested archive stream position is too large.", exception);
            }

            if (position < 0)
            {
                throw new IOException("Cannot seek before the beginning of the retained archive.");
            }

            Position = position;
            return position;
        }

        public override void Flush() => EnsureActive();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            return Task.CompletedTask;
        }

        public override void SetLength(long value) => throw new NotSupportedException("The retained archive is read-only.");

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The retained archive is read-only.");

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }

        private void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner.EnsureActive();
        }
    }
}
