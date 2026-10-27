// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Update;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class ProgressReadStreamTests
{
    private static readonly byte[] _payload = [1, 2, 3, 4, 5];

    [Fact]
    public async Task ReadAsync_MultipleReads_ReportsCumulativeUnknownTotalAndNoEofNotification()
    {
        RecordingProgress progress = new();
        await using ProgressReadStream stream = new(new MemoryStream(_payload), totalBytes: null, progress);
        byte[] buffer = new byte[2];
        List<byte> received = [];

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, CancellationToken.None)) > 0)
        {
            received.AddRange(buffer[..bytesRead]);
        }

        int reportsAtEof = progress.Values.Count;
        int repeatedEofRead = await stream.ReadAsync(buffer, CancellationToken.None);

        Assert.Equal(_payload, received);
        Assert.Equal(0, repeatedEofRead);
        Assert.Equal(reportsAtEof, progress.Values.Count);
        Assert.Collection(
            progress.Values,
            value => AssertProgress(value, bytesRead: 2, totalBytes: null),
            value => AssertProgress(value, bytesRead: 4, totalBytes: null),
            value => AssertProgress(value, bytesRead: 5, totalBytes: null));
    }

    [Fact]
    public void Read_MultipleReads_ReportsCumulativeKnownTotalAndReturnsUnchangedBytes()
    {
        RecordingProgress progress = new();
        using ProgressReadStream stream = new(new MemoryStream(_payload), _payload.Length, progress);
        byte[] buffer = new byte[3];
        List<byte> received = [];

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            received.AddRange(buffer[..bytesRead]);
        }

        Assert.Equal(_payload, received);
        Assert.Collection(
            progress.Values,
            value => AssertProgress(value, bytesRead: 3, totalBytes: _payload.Length),
            value => AssertProgress(value, bytesRead: 5, totalBytes: _payload.Length));
    }

    private static void AssertProgress(UpdateProgress progress, long bytesRead, long? totalBytes)
    {
        Assert.Equal(UpdatePhase.Downloading, progress.Phase);
        Assert.Equal(bytesRead, progress.BytesRead);
        Assert.Equal(totalBytes, progress.TotalBytes);
    }

    private sealed class RecordingProgress : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Values { get; } = [];

        public void Report(UpdateProgress value) => Values.Add(value);
    }
}
