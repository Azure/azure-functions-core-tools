// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Update;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class SizeLimitedReadStreamTests
{
    [Fact]
    public async Task CopyToAsync_ContentAtLimit_ReturnsAllBytes()
    {
        // Arrange
        byte[] expected = [1, 2, 3, 4];
        await using SizeLimitedReadStream stream = new(new MemoryStream(expected), expected.Length);
        using MemoryStream actual = new();

        // Act
        await stream.CopyToAsync(actual, CancellationToken.None);

        // Assert
        actual.ToArray().Should().Equal(expected);
    }

    [Fact]
    public async Task CopyToAsync_ContentOverLimit_ThrowsInvalidDataExceptionOnOverflowByte()
    {
        // Arrange
        byte[] contents = [1, 2, 3, 4, 5];
        await using SizeLimitedReadStream stream = new(new MemoryStream(contents), maximumByteCount: 4);
        using MemoryStream destination = new();

        // Act
        Func<Task> act = async () => await stream.CopyToAsync(destination, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*4-byte*");
    }

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        Action act = () => _ = new SizeLimitedReadStream(null!, maximumByteCount: 1);

        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveLimit_ThrowsArgumentOutOfRangeException(long maximumByteCount)
    {
        Action act = () => _ = new SizeLimitedReadStream(new MemoryStream(), maximumByteCount);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maximumByteCount");
    }
}
