// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Read-only stream wrapper that reports cumulative bytes read to an
/// <see cref="IProgress{T}"/> sink. Used by <see cref="CliUpdater"/> so the
/// existing <see cref="IUpdateFileSystem.SaveStreamToFileAsync"/> path can
/// stay simple while the download surface gets live byte-count updates.
/// </summary>
internal sealed class ProgressReadStream(
    Stream inner,
    long? totalBytes,
    IProgress<UpdateProgress> progress) : Stream
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IProgress<UpdateProgress> _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    private readonly long? _totalBytes = totalBytes;
    private long _bytesRead;

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        Track(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Track(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer, cancellationToken);
        Track(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Track(int justRead)
    {
        if (justRead <= 0)
        {
            return;
        }

        _bytesRead += justRead;
        _progress.Report(new UpdateProgress(UpdatePhase.Downloading, _bytesRead, _totalBytes));
    }
}
