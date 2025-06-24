// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Extensions
{
    /// <summary>
    /// Provides extension methods for the <see cref="Stream"/> class to read exactly a specified
    /// number of bytes from the stream. Required when building the CLI for .NET 6.
    /// </summary>
    public static class StreamExtensions
    {
        public static void ReadExactly(this Stream stream, Span<byte> buffer)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            ReadIntoSpan(stream, buffer);
        }

        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            ReadIntoSpan(stream, buffer.AsSpan(offset, count));
        }

        private static void ReadIntoSpan(Stream stream, Span<byte> span)
        {
            int totalRead = 0;

            while (totalRead < span.Length)
            {
                int bytesRead = stream.Read(span.Slice(totalRead));
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException($"Unable to read {span.Length} bytes from stream. Only read {totalRead} bytes.");
                }

                totalRead += bytesRead;
            }
        }
    }
}
