// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// LSP/DAP-style Content-Length frame codec for the workload protocol.
///
/// Wire format:
/// <code>
/// Content-Length: &lt;N&gt;\r\n
/// \r\n
/// &lt;N bytes of UTF-8 payload&gt;
/// </code>
///
/// Newlines and JSON escaping inside the payload are not a concern because the
/// payload is read by exact byte count. Other headers may be present but are
/// ignored; only Content-Length is required.
/// </summary>
public static class FrameCodec
{
    private const string HeaderTerminator = "\r\n\r\n";

    /// <summary>
    /// Reads a single framed payload from <paramref name="stream"/>.
    /// Returns null on clean EOF (peer closed cleanly between frames).
    /// Throws <see cref="InvalidDataException"/> on a malformed frame or partial read.
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var headerBytes = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
        if (headerBytes is null)
        {
            return null; // clean EOF
        }

        var header = Encoding.ASCII.GetString(headerBytes);
        var length = ParseContentLength(header);

        var payload = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(payload.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new InvalidDataException(
                    $"Unexpected EOF while reading frame payload (expected {length} bytes, got {read}).");
            }
            read += n;
        }

        return payload;
    }

    /// <summary>
    /// Writes a single framed payload to <paramref name="stream"/> and flushes.
    /// </summary>
    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var header = $"{WorkloadProtocol.ContentLengthHeader}: {payload.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Read byte-by-byte until "\r\n\r\n". Headers are tiny (<200 bytes typical)
        // so byte-at-a-time is fine and avoids overrunning into the payload.
        var buffer = new List<byte>(128);
        var single = new byte[1];
        var matched = 0;

        while (true)
        {
            var n = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (buffer.Count == 0)
                {
                    return null; // clean EOF between frames
                }

                throw new InvalidDataException(
                    $"Unexpected EOF while reading frame header (got {buffer.Count} bytes).");
            }

            buffer.Add(single[0]);
            if (single[0] == HeaderTerminator[matched])
            {
                matched++;
                if (matched == HeaderTerminator.Length)
                {
                    // Strip the trailing \r\n\r\n from the returned header.
                    return [.. buffer.Take(buffer.Count - HeaderTerminator.Length)];
                }
            }
            else
            {
                matched = single[0] == HeaderTerminator[0] ? 1 : 0;
            }

            if (buffer.Count > 8 * 1024)
            {
                throw new InvalidDataException("Frame header exceeded 8KB without terminator.");
            }
        }
    }

    private static int ParseContentLength(string header)
    {
        foreach (var rawLine in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            if (!string.Equals(name, WorkloadProtocol.ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            if (!int.TryParse(value, out var length) || length < 0)
            {
                throw new InvalidDataException($"Invalid Content-Length value: '{value}'.");
            }

            return length;
        }

        throw new InvalidDataException("Missing required Content-Length header.");
    }
}
