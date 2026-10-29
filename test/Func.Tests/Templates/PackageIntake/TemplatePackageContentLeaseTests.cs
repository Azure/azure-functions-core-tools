// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PackageIntake;
using NSubstitute;
using static Azure.Functions.Cli.Tests.Templates.PackageIntake.TemplatePackageTestData;

namespace Azure.Functions.Cli.Tests.Templates.PackageIntake;

public sealed class TemplatePackageContentLeaseTests
{
    [Fact]
    public async Task OpenRead_SourceChangesAfterCapture_StillReturnsValidatedArchiveAndInventory()
    {
        var source = Archive([new("content/file.txt", "validated")]);
        byte[] expected = [.. source];
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ => new MemoryStream(source, writable: false));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        source.AsSpan().Fill(0);
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ => new MemoryStream(Archive(manifest: Manifest(id: "Replacement.Package"))));

        using var retained = lease.OpenRead();
        using MemoryStream observed = new();
        await retained.CopyToAsync(observed, CancellationToken.None);

        observed.ToArray().Should().Equal(expected);
        lease.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(expected)));
        using ZipArchive archive = new(lease.OpenRead(), ZipArchiveMode.Read);
        using StreamReader content = new(archive.GetEntry("content/file.txt")!.Open(), Encoding.UTF8);
        (await content.ReadToEndAsync(CancellationToken.None)).Should().Be("validated");
        fileSystem.Received(1).OpenReadIfExists(ArchivePath);
    }

    [Fact]
    public async Task OpenRead_IndependentReaders_KeepSeparatePositionsWithoutExposingMutableBuffers()
    {
        var bytes = Archive();
        var (intake, _) = Intake(bytes);
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        using var first = lease.OpenRead();
        using var second = lease.OpenRead();

        first.ReadByte().Should().Be(bytes[0]);
        second.Position.Should().Be(0);
        first.Position.Should().Be(1);
        second.Seek(-1, SeekOrigin.End).Should().Be(bytes.Length - 1);
        second.ReadByte().Should().Be(bytes[^1]);
        second.ReadByte().Should().Be(-1);
        first.Should().NotBeAssignableTo<MemoryStream>();
        first.CanWrite.Should().BeFalse();
        Action write = () => first.WriteByte(0);
        Action resize = () => first.SetLength(0);
        write.Should().Throw<NotSupportedException>();
        resize.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task OpenRead_ConcurrentIndependentReaders_ReturnIdenticalValidatedBytes()
    {
        var bytes = Archive([new("content/file", new string('x', 100_000))]);
        var (intake, _) = Intake(bytes);
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<byte[]>[] pending = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            using var stream = lease.OpenRead();
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy, CancellationToken.None);
            return copy.ToArray();
        }, CancellationToken.None))];

        start.SetResult();
        var copies = await Task.WhenAll(pending);

        copies.Should().OnlyContain(copy => copy.SequenceEqual(bytes));
    }

    [Fact]
    public async Task Dispose_ExistingAndFutureReaders_AreRevokedAndCleanupRemainsObservable()
    {
        var (intake, _) = Intake(Archive());
        var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        using var reader = lease.OpenRead();

        lease.Dispose();
        lease.Dispose();

        lease.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        lease.Inventory.Should().NotBeEmpty();
        lease.PackageId.Should().Be(PackageId);
        reader.CanRead.Should().BeFalse();
        reader.CanSeek.Should().BeFalse();
        Action read = () => reader.ReadByte();
        Action seek = () => reader.Seek(0, SeekOrigin.Begin);
        Action reopen = () => lease.OpenRead();
        read.Should().Throw<ObjectDisposedException>();
        seek.Should().Throw<ObjectDisposedException>();
        reopen.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_OneReader_DoesNotDisposeLeaseOrAnotherReader()
    {
        var (intake, _) = Intake(Archive());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        var first = lease.OpenRead();
        using var second = lease.OpenRead();

        first.Dispose();

        lease.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Active);
        second.ReadByte().Should().NotBe(-1);
        Action read = () => first.ReadByte();
        read.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ReadAsync_CancelledToken_DoesNotAdvanceReader()
    {
        var (intake, _) = Intake(Archive());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        using var reader = lease.OpenRead();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => reader.ReadAsync(new byte[10], cancellation.Token).AsTask();

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        reader.Position.Should().Be(0);
    }

    [Fact]
    public async Task OpenRead_InventoryAndPackageTypes_CannotBeMutated()
    {
        var (intake, _) = Intake(Archive());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        Action inventory = () => ((IList<TemplatePackageInventoryEntry>)lease.Inventory).Clear();
        Action types = () => ((IList<string>)lease.PackageTypes).Clear();

        inventory.Should().Throw<NotSupportedException>();
        types.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Read_NullByteArray_ThrowsEvenForAnEmptyRead()
    {
        var (intake, _) = Intake(Archive());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        using var stream = lease.OpenRead();

        Func<int> read = () => stream.Read(null!, 0, 0);
        Func<Task> readAsync = () => stream.ReadAsync(null!, 0, 0, CancellationToken.None);

        read.Should().Throw<ArgumentNullException>().WithParameterName("buffer");
        await readAsync.Should().ThrowAsync<ArgumentNullException>().WithParameterName("buffer");
        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task Seek_InvalidOrBeyondEndPositions_FollowsReadOnlyStreamSemantics()
    {
        var (intake, _) = Intake(Archive());
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
        using var stream = lease.OpenRead();
        Action negative = () => stream.Position = -1;
        Action beforeStart = () => stream.Seek(-1, SeekOrigin.Begin);
        Action invalidOrigin = () => stream.Seek(0, (SeekOrigin)100);
        stream.Position = long.MaxValue;
        Action overflow = () => stream.Seek(1, SeekOrigin.Current);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        beforeStart.Should().Throw<IOException>();
        invalidOrigin.Should().Throw<ArgumentOutOfRangeException>();
        overflow.Should().Throw<IOException>();
        stream.ReadByte().Should().Be(-1);
        stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        stream.ReadByte().Should().NotBe(-1);
    }
}
