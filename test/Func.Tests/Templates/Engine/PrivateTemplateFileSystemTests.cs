// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class PrivateTemplateFileSystemTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"func-memory-test-{Guid.NewGuid():N}");

    [Fact]
    public void SealedSources_RejectWritesAndAncestorDeletionBeforeRendering()
    {
        using PrivateTemplateFileSystem memory = Create();
        string source = Path.Combine(_root, "content", "source.txt");
        memory.CreateDirectory(Path.GetDirectoryName(source)!);
        memory.WriteAllText(source, "source");
        memory.SealSource(Path.GetDirectoryName(source)!);

        Assert.Throws<UnauthorizedAccessException>(() => memory.WriteAllText(source, "changed"));
        Assert.Throws<UnauthorizedAccessException>(() => memory.FileDelete(source));
        Assert.Throws<UnauthorizedAccessException>(() => memory.DirectoryDelete(_root, true));
        using Stream input = memory.OpenRead(source);
        Assert.False(input.CanWrite);
        Assert.Equal("source", memory.ReadAllText(source));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Output_AllowsBoundedRewindAndTruncationForConditionalGeneration()
    {
        using PrivateTemplateFileSystem memory = Ready();
        string outputPath = Path.Combine(memory.OutputRoot, "output.txt");
        using (Stream output = memory.CreateFile(outputPath))
        {
            output.Write([1, 2, 3]);
            output.Position = 1;
            Assert.Equal(2, output.ReadByte());
            output.SetLength(1);
            output.Write([4, 5]);
        }

        memory.ThrowIfRenderFailed();
        Assert.Equal<byte>([1, 4, 5], memory.ReadAllBytes(outputPath));
    }

    [Theory]
    [InlineData("seek")]
    [InlineData("length")]
    [InlineData("quota")]
    public void Output_RejectsUnboundedGrowthAndRetainsFailure(string operation)
    {
        using PrivateTemplateFileSystem memory = Ready();
        using (Stream output = memory.CreateFile(Path.Combine(memory.OutputRoot, "output.txt")))
        {
            Action write = () =>
            {
                if (operation == "seek")
                {
                    output.Position = PrivateTemplateFileSystem.MaximumGeneratedFileByteCount + 1;
                }
                else if (operation == "length")
                {
                    output.SetLength(PrivateTemplateFileSystem.MaximumGeneratedFileByteCount + 1);
                }
                else
                {
                    output.Write(new byte[PrivateTemplateFileSystem.MaximumGeneratedFileByteCount + 1]);
                }
            };

            if (operation == "quota")
            {
                Assert.Throws<InvalidDataException>(write);
            }
            else
            {
                Assert.Throws<NotSupportedException>(write);
            }
        }

        Assert.Throws<InvalidDataException>(memory.ThrowIfRenderFailed);
    }

    [Fact]
    public void Output_CancellationStopsSynchronousWritesWithoutPhysicalEffects()
    {
        using CancellationTokenSource cancellation = new();
        using PrivateTemplateFileSystem memory = Ready(cancellation.Token);
        using Stream output = memory.CreateFile(Path.Combine(memory.OutputRoot, "output.txt"));
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => output.WriteByte(1));
        Assert.False(Directory.Exists(_root));
    }

    private PrivateTemplateFileSystem Create() => new(_root, Path.Combine(_root, "output"));

    private PrivateTemplateFileSystem Ready(CancellationToken cancellationToken = default)
    {
        PrivateTemplateFileSystem memory = Create();
        string sourceRoot = Path.Combine(_root, "source");
        memory.CreateDirectory(sourceRoot);
        memory.SealSource(sourceRoot);
        memory.BeginRendering(cancellationToken);
        return memory;
    }
}
