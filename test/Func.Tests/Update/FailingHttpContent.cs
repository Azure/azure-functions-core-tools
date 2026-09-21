// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Update;

internal sealed class FailingHttpContent(Exception failure, bool failOnOpen, Action? beforeFailure = null) : HttpContent
{
    private readonly Exception _failure = failure ?? throw new ArgumentNullException(nameof(failure));

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        if (failOnOpen)
        {
            beforeFailure?.Invoke();
            return Task.FromException<Stream>(_failure);
        }

        Stream stream = Substitute.For<Stream>();
        stream.CanRead.Returns(true);
        stream.ReadAsync(Arg.Any<Memory<byte>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            beforeFailure?.Invoke();
            return ValueTask.FromException<int>(_failure);
        });
        return Task.FromResult(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => throw new NotSupportedException();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
