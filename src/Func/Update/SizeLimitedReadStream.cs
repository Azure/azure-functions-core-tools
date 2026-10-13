// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Read-only stream wrapper that rejects content exceeding a byte limit.
/// </summary>
internal sealed class SizeLimitedReadStream(Stream inner, long maximumByteCount) : Stream
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly long _maximumByteCount = maximumByteCount > 0
        ? maximumByteCount
        : throw new ArgumentOutOfRangeException(nameof(maximumByteCount));
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
        int read = _inner.Read(buffer, offset, GetAllowedReadCount(count));
        Track(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _inner.ReadAsync(buffer.AsMemory(offset, GetAllowedReadCount(count)), cancellationToken);
        Track(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer[..GetAllowedReadCount(buffer.Length)], cancellationToken);
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

    private int GetAllowedReadCount(int requestedCount)
    {
        long bytesThroughOverflow = (_maximumByteCount - _bytesRead) + 1;
        return (int)Math.Min(requestedCount, bytesThroughOverflow);
    }

    private void Track(int read)
    {
        _bytesRead += read;
        if (_bytesRead > _maximumByteCount)
        {
            throw new InvalidDataException($"Stream exceeds the {_maximumByteCount}-byte read limit.");
        }
    }
}
