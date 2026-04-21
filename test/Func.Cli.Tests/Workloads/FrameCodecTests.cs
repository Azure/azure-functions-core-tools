// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class FrameCodecTests
{
    [Fact]
    public async Task Roundtrip_PreservesPayloadBytes()
    {
        var payload = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"hello\"}");
        using var stream = new MemoryStream();
        await FrameCodec.WriteFrameAsync(stream, payload);
        stream.Position = 0;

        var read = await FrameCodec.ReadFrameAsync(stream);

        Assert.NotNull(read);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task Roundtrip_MultipleFrames_DoesNotInterleave()
    {
        var first = Encoding.UTF8.GetBytes("first");
        var second = Encoding.UTF8.GetBytes("second-with-newlines\r\nstill-second");

        using var stream = new MemoryStream();
        await FrameCodec.WriteFrameAsync(stream, first);
        await FrameCodec.WriteFrameAsync(stream, second);
        stream.Position = 0;

        Assert.Equal(first, await FrameCodec.ReadFrameAsync(stream));
        Assert.Equal(second, await FrameCodec.ReadFrameAsync(stream));
        Assert.Null(await FrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrame_OnEmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var result = await FrameCodec.ReadFrameAsync(stream);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadFrame_WithMissingHeader_Throws()
    {
        var bytes = Encoding.ASCII.GetBytes("X-Bad: 5\r\n\r\nhello");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrame_WithTruncatedPayload_Throws()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Length: 100\r\n\r\nshort");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameCodec.ReadFrameAsync(stream));
    }
}
