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

public sealed class LocalTemplatePackageIntakeTests
{
    [Fact]
    public async Task IntakeAsync_ValidArchive_RetainsExactBytesAndHonestMetadata()
    {
        var bytes = Archive();
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var (intake, fileSystem) = Intake(bytes);

        using var lease = await intake.IntakeAsync(Request(digest: digest), CancellationToken.None);
        using var retained = lease.OpenRead();
        using MemoryStream copy = new();
        await retained.CopyToAsync(copy, CancellationToken.None);

        copy.ToArray().Should().Equal(bytes);
        lease.PackageId.Should().Be(PackageId);
        lease.Version.Should().Be(PackageVersion);
        lease.PackageTypes.Should().Equal("FuncTemplate", "Template");
        lease.Sha256.Should().Be(digest.ToLowerInvariant());
        lease.ArchiveBytes.Should().Be(bytes.Length);
        lease.Source.Should().Be(TemplatePackageSource.LocalArchive);
        lease.Trust.Should().Be(TemplatePackageTrust.Unqualified);
        lease.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Active);
        fileSystem.Received(1).OpenReadIfExists(ArchivePath);
        fileSystem.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task IntakeAsync_Inventory_IncludesImplicitDirectoriesAndActualFileHashes()
    {
        var (intake, _) = Intake(Archive([new("content/deep/file.txt", "abc"), new("empty/")]));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.Inventory.Select(entry => entry.Path).Should().Equal(
            ManifestPath, "content", "content/deep", "content/deep/file.txt", "empty");
        var file = lease.Inventory.Single(entry => entry.Path == "content/deep/file.txt");
        file.Kind.Should().Be(TemplatePackageEntryKind.File);
        file.Length.Should().Be(3);
        file.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData("abc"u8)));
        lease.Inventory.Where(entry => entry.Kind == TemplatePackageEntryKind.Directory)
            .Should().OnlyContain(entry => entry.Length == 0 && entry.Sha256 == null);
        lease.ExpandedBytes.Should().Be(Encoding.UTF8.GetByteCount(Manifest()) + 3);
    }

    [Fact]
    public async Task IntakeAsync_ExplicitDirectoryAfterChild_CompletesItsImplicitInventoryEntry()
    {
        var (intake, _) = Intake(Archive([new("content/file.txt", "a"), new("content/")]));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.Inventory.Count(entry => entry.Path == "content").Should().Be(1);
    }

    [Fact]
    public async Task IntakeAsync_OpaqueTemplateMetadata_DoesNotInterpretEngineOrRuntimePolicy()
    {
        var (intake, _) = Intake(Archive([new("content/.template.config/template.json", "adapter must interpret this")]));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.Inventory.Should().Contain(entry => entry.Path == "content/.template.config/template.json");
        lease.Trust.Should().Be(TemplatePackageTrust.Unqualified);
    }

    [Fact]
    public async Task IntakeAsync_NoDigestExpectation_StillReportsObservedDigest()
    {
        var bytes = Archive();
        var (intake, _) = Intake(bytes);

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task IntakeAsync_PlainTemplateTypeAndPrereleaseVersion_MatchesExplicitExpectations()
    {
        var (intake, _) = Intake(Archive(manifest: Manifest(PackageId, "1.2.3-preview.4", "Template")));

        using var lease = await intake.IntakeAsync(Request(version: "1.2.3-preview.4", type: "Template"), CancellationToken.None);

        lease.PackageTypes.Should().Equal("Template");
        lease.Version.Should().Be("1.2.3-preview.4");
        lease.Trust.Should().Be(TemplatePackageTrust.Unqualified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x800)]
    public async Task IntakeAsync_InvalidUtf8Filename_RejectsWithOrWithoutUtf8Flag(int flags)
    {
        var bytes = ChangeEntryHeaders(Archive(), "content/file.txt", flags: (ushort)flags, invalidUtf8Name: true);
        var (intake, _) = Intake(bytes);

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IntakeAsync_EncryptedOrUnsupportedCompressionEntry_Rejects(bool encrypted)
    {
        var bytes = encrypted
            ? ChangeEntryHeaders(Archive(), "content/file.txt", flags: 1)
            : ChangeEntryHeaders(Archive(), "content/file.txt", method: 99);
        var (intake, _) = Intake(bytes);

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidArchive);
    }

    [Theory]
    [InlineData("Another.Package", PackageVersion, "Template", (int)TemplatePackageFailure.IdentityMismatch)]
    [InlineData(PackageId, "1.2.4", "Template", (int)TemplatePackageFailure.VersionMismatch)]
    [InlineData(PackageId, PackageVersion, "OtherType", (int)TemplatePackageFailure.TypeMismatch)]
    [InlineData(PackageId, PackageVersion, "FuncTemplate", (int)TemplatePackageFailure.TypeMismatch)]
    public async Task IntakeAsync_WrongPackageMetadata_RejectsWithoutAnotherSource(
        string id, string version, string type, int failure)
    {
        var (intake, fileSystem) = Intake(Archive(manifest: Manifest(id, version, type)));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be((TemplatePackageFailure)failure);
        fileSystem.Received(1).OpenReadIfExists(ArchivePath);
        fileSystem.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task IntakeAsync_CaseVariantPackageId_UsesNuGetIdComparison()
    {
        var (intake, _) = Intake(Archive(manifest: Manifest(id: "example.templates")));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.PackageId.Should().Be("example.templates");
    }

    [Fact]
    public async Task IntakeAsync_DigestMismatch_FailsWithoutSelectingAnotherSource()
    {
        var (intake, fileSystem) = Intake(Archive());

        Func<Task> action = () => intake.IntakeAsync(Request(digest: new string('0', 64)), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.DigestMismatch);
        fileSystem.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntakeAsync_MissingOrUnreadableExplicitSource_PropagatesFailureWithoutFallback(bool unreadable)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ =>
            unreadable ? throw new UnauthorizedAccessException("source denied") : null);
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits());

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        if (unreadable)
        {
            await action.Should().ThrowExactlyAsync<UnauthorizedAccessException>().WithMessage("source denied");
        }
        else
        {
            await action.Should().ThrowExactlyAsync<FileNotFoundException>();
        }

        fileSystem.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntakeAsync_MalformedOrTruncatedArchive_Rejects(bool truncate)
    {
        var bytes = truncate ? Archive()[..^10] : "not a zip archive"u8.ToArray();
        var (intake, _) = Intake(bytes);

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidArchive);
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.SmallestSize)]
    public async Task IntakeAsync_CorruptPayload_RejectsEvenWithoutExpectedArchiveDigest(CompressionLevel compression)
    {
        var bytes = Archive([new("content/file.txt", new string('a', 1000), Compression: compression)]);
        var (intake, _) = Intake(ChangePayloadByte(bytes, "content/file.txt"));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplatePackageException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task IntakeAsync_IncorrectDeclaredExpandedLength_Rejects(int declaredLength)
    {
        var bytes = Archive([new("content/file.txt", "actual bytes")]);
        var (intake, _) = Intake(ChangeDeclaredLength(bytes, "content/file.txt", declaredLength));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.IntegrityMismatch);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("content/../../escape")]
    [InlineData("./file")]
    [InlineData("content/./file")]
    [InlineData("/absolute")]
    [InlineData("//server/share")]
    [InlineData("C:/absolute")]
    [InlineData("C:relative")]
    [InlineData("\\rooted")]
    [InlineData("content\\file")]
    [InlineData("content//file")]
    [InlineData("content/../file")]
    [InlineData("content/stream:ads")]
    [InlineData("content/trailing.")]
    [InlineData("content/trailing ")]
    [InlineData("content/CON")]
    [InlineData("content/nul.txt")]
    [InlineData("content/COM1.log")]
    [InlineData("content/LPT\u00b9.txt")]
    [InlineData("content/NUL .txt")]
    [InlineData("content/CON .txt")]
    [InlineData("content/COM1 .log")]
    [InlineData("content/NUL   .txt")]
    [InlineData("content/cOn  .txt")]
    [InlineData("content/cOm1 .log")]
    [InlineData("content/PRN .txt")]
    [InlineData("content/AUX  .dat")]
    [InlineData("content/LPT\u00b9 .txt")]
    [InlineData("content/deep/NUL .txt/child.txt")]
    [InlineData("content/deep/CON  .cfg/")]
    [InlineData("content/deep/cOm1   .log/child.txt")]
    [InlineData("content/CONIN$")]
    [InlineData("content/name?")]
    [InlineData("content/name*")]
    [InlineData("content/name\"")]
    [InlineData("content/name<")]
    [InlineData("content/name|")]
    [InlineData("content/control\u001b")]
    [InlineData("content/null\0")]
    [InlineData("content/cafe\u0301")]
    [InlineData("content/replacement\ufffd")]
    [InlineData("/")]
    public async Task IntakeAsync_UnsafeOrNoncanonicalPath_Rejects(string path)
    {
        var (intake, _) = Intake(Archive([new(path)]));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidPath);
    }

    [Theory]
    [InlineData("content/ordinary .txt")]
    [InlineData("content/ordinary   .txt")]
    [InlineData("content/deep/ordinary .dir/file .txt")]
    [InlineData("content/NUL-safe .txt")]
    [InlineData("content/COM10 .log")]
    [InlineData("content/NUL\u00a0.txt")]
    public async Task IntakeAsync_PermittedSpaceBeforeExtension_PreservesExactInventoryPath(string path)
    {
        var (intake, _) = Intake(Archive([new(path, "unchanged")]));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.Inventory.Where(entry => entry.Kind == TemplatePackageEntryKind.File && entry.Path != ManifestPath)
            .Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Theory]
    [InlineData("content/file", "content/file")]
    [InlineData("content/file", "content/FILE")]
    [InlineData("content/file", "Content/other")]
    [InlineData("content/", "content/")]
    [InlineData("content", "content/")]
    [InlineData("content/", "content")]
    [InlineData("content", "content/child")]
    [InlineData("content/child", "content")]
    [InlineData("content/child", "CONTENT")]
    public async Task IntakeAsync_DuplicateOrConflictingPaths_RejectsRegardlessOfEntryOrder(string first, string second)
    {
        var (intake, _) = Intake(Archive([new(first), new(second)]));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.PathCollision);
    }

    [Theory]
    [InlineData(unchecked((int)0xa1ff0000))]
    [InlineData(unchecked((int)0x11ff0000))]
    [InlineData(unchecked((int)0x61ff0000))]
    [InlineData(0x400)]
    [InlineData(0x10)]
    [InlineData(0x41ed0000)]
    public async Task IntakeAsync_LinkReparseOrSpecialFileAttributes_Rejects(int attributes)
    {
        var (intake, _) = Intake(Archive([new("content/file", "link target", attributes)]));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.UnsupportedEntry);
    }

    [Fact]
    public async Task IntakeAsync_DirectoryWithPayload_Rejects()
    {
        var (intake, _) = Intake(Archive([new("content/", "not an empty directory")]));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.IntegrityMismatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<package>")]
    [InlineData("<wrong />")]
    [InlineData("<package><metadata /></package>")]
    [InlineData("<!DOCTYPE package [<!ENTITY x SYSTEM 'file:///not-read'>]><package>&x;</package>")]
    public async Task IntakeAsync_InvalidOrDtdManifest_Rejects(string manifest)
    {
        var (intake, _) = Intake(Archive(manifest: manifest));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidMetadata);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("packageTypes")]
    [InlineData("metadata")]
    public async Task IntakeAsync_DuplicateCriticalManifestElement_RejectsBeforeNuGetNormalization(string element)
    {
        var manifest = Manifest();
        var start = manifest.IndexOf($"<{element}", StringComparison.Ordinal);
        var end = manifest.IndexOf($"</{element}>", start, StringComparison.Ordinal) + element.Length + 3;
        manifest = manifest.Insert(end, manifest[start..end]);
        var (intake, _) = Intake(Archive(manifest: manifest));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidMetadata);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("[1.0,2.0)")]
    [InlineData("1.2")]
    [InlineData("1.2.3+metadata")]
    [InlineData(" 1.2.3 ")]
    public async Task IntakeAsync_InvalidOrNoncanonicalManifestVersion_Rejects(string version)
    {
        var (intake, _) = Intake(Archive(manifest: Manifest(version: version)));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidMetadata);
    }

    [Fact]
    public async Task IntakeAsync_RepeatedPackageType_Rejects()
    {
        var (intake, _) = Intake(Archive(manifest: Manifest(PackageId, PackageVersion, "Template", "template", "FuncTemplate")));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidMetadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntakeAsync_MissingOrMultipleRootManifests_Rejects(bool multiple)
    {
        var bytes = multiple
            ? Archive([new("other.nuspec", Manifest())])
            : Archive([new("nested/package.nuspec", Manifest())], includeManifest: false);
        var (intake, _) = Intake(bytes);

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.InvalidMetadata);
    }

    [Fact]
    public async Task IntakeAsync_EntryCountBoundary_AcceptsExactLimitAndRejectsOneLess()
    {
        var bytes = Archive();
        var (accepted, _) = Intake(bytes, Limits(entries: 2));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(entries: 1));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        lease.Inventory.Count(entry => entry.Kind == TemplatePackageEntryKind.File).Should().Be(2);
    }

    [Fact]
    public async Task IntakeAsync_ArchiveReadBoundary_CountsActualNonseekableReadsIncludingRejectionSentinel()
    {
        var bytes = Archive();
        var fileSystem = Substitute.For<IFileSystem>();
        var source = new ObservedReadStream(bytes);
        fileSystem.OpenReadIfExists(ArchivePath).Returns(source);
        var rejected = new LocalTemplatePackageIntake(fileSystem, Limits(archiveBytes: bytes.Length - 1));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        source.BytesRead.Should().Be(bytes.Length);
        source.Disposed.Should().BeTrue();
        var (accepted, _) = Intake(bytes, Limits(archiveBytes: bytes.Length));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        lease.ArchiveBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task IntakeAsync_ExpandedByteBoundary_CountsActualDecompressedBytes()
    {
        const int payloadBytes = 100_000;
        var bytes = Archive([new("content/file", new string('a', payloadBytes), Compression: CompressionLevel.SmallestSize)]);
        var expanded = Encoding.UTF8.GetByteCount(Manifest()) + payloadBytes;
        var (accepted, _) = Intake(bytes, Limits(expandedBytes: expanded, entryBytes: payloadBytes));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(expandedBytes: expanded - 1, entryBytes: payloadBytes));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        lease.ExpandedBytes.Should().Be(expanded).And.BeGreaterThan(bytes.Length);
    }

    [Fact]
    public async Task IntakeAsync_PerEntryByteBoundary_RejectsOneByteOver()
    {
        var bytes = Archive([new("content/file", new string('a', 1000))]);
        var (accepted, _) = Intake(bytes, Limits(entryBytes: 1000));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(entryBytes: 999));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        lease.Inventory.Single(entry => entry.Path == "content/file").Length.Should().Be(1000);
    }

    [Fact]
    public async Task IntakeAsync_ManifestByteBoundary_RejectsOneByteOver()
    {
        var manifestBytes = Encoding.UTF8.GetByteCount(Manifest());
        var bytes = Archive();
        var (accepted, _) = Intake(bytes, Limits(manifestBytes: manifestBytes));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(manifestBytes: manifestBytes - 1));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
    }

    [Fact]
    public async Task IntakeAsync_BufferAllocationBoundary_CountsScratchArchiveAndManifest()
    {
        var bytes = Archive();
        var expected = 16 * 1024 + bytes.Length + Encoding.UTF8.GetByteCount(Manifest());
        var (accepted, _) = Intake(bytes, Limits(bufferBytes: expected));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(bufferBytes: expected - 1));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        lease.AllocatedBufferBytes.Should().Be(expected);
        lease.RetainedBufferBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task IntakeAsync_BufferGrowth_CountsReplacedArchiveAllocations()
    {
        var bytes = Archive([new("content/file", new string('a', 70_000))]);
        var (intake, _) = Intake(bytes);

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.AllocatedBufferBytes.Should().Be(
            16 * 1024 + 16 * 1024 + 32 * 1024 + 64 * 1024 + 128 * 1024 + Encoding.UTF8.GetByteCount(Manifest()));
        var (rejected, _) = Intake(bytes, Limits(
            bufferBytes: lease.RetainedBufferBytes + 16 * 1024 + Encoding.UTF8.GetByteCount(Manifest())));
        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);
        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
    }

    [Fact]
    public async Task IntakeAsync_PathCountBoundary_CountsImplicitParents()
    {
        var bytes = Archive([new("a/b/c", "x")]);
        var (accepted, _) = Intake(bytes, Limits(paths: 4));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(paths: 3));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        lease.Inventory.Should().HaveCount(4);
    }

    [Fact]
    public async Task IntakeAsync_PathLengthBoundary_UsesCompleteArchiveName()
    {
        var path = new string('a', 100);
        var bytes = Archive([new(path)]);
        var (accepted, _) = Intake(bytes, Limits(pathLength: 100));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(pathLength: 99));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
    }

    [Fact]
    public async Task IntakeAsync_UnicodePathLengthBoundary_CountsUtf16CodeUnitsRatherThanUtf8Bytes()
    {
        var path = new string('\u00e9', 80);
        var bytes = Archive([new(path)]);
        var (accepted, _) = Intake(bytes, Limits(pathLength: 80));
        using var lease = await accepted.IntakeAsync(Request(), CancellationToken.None);
        var (rejected, _) = Intake(bytes, Limits(pathLength: 79));

        Func<Task> action = () => rejected.IntakeAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        Encoding.UTF8.GetByteCount(path).Should().Be(160);
        lease.Inventory.Should().Contain(entry => entry.Path == path);
    }

    [Fact]
    public async Task IntakeAsync_MaximumLongExpansionLimits_DoNotOverflowReadAccounting()
    {
        var (intake, _) = Intake(Archive(), Limits(expandedBytes: long.MaxValue, entryBytes: long.MaxValue));

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        lease.ExpandedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IntakeAsync_ActiveLeaseLimit_RejectsBeforeOpeningAnotherSourceAndReleasesOnDispose()
    {
        var (intake, fileSystem) = Intake(Archive(), Limits(activeLeases: 1));
        var first = await intake.IntakeAsync(Request(), CancellationToken.None);

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);
        (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        fileSystem.Received(1).OpenReadIfExists(ArchivePath);
        first.Dispose();
        first.Dispose();
        using var next = await intake.IntakeAsync(Request(), CancellationToken.None);
        next.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Active);
        fileSystem.Received(2).OpenReadIfExists(ArchivePath);
    }

    [Fact]
    public async Task IntakeAsync_ConcurrentCapture_OccupiesTheSameLeaseLimit()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var source = new BlockingReadStream(Archive());
        fileSystem.OpenReadIfExists(ArchivePath).Returns(source);
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(activeLeases: 1));
        var pending = intake.IntakeAsync(Request(), CancellationToken.None);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        try
        {
            Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);
            (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
            fileSystem.Received(1).OpenReadIfExists(ArchivePath);
        }
        finally
        {
            source.Resume.TrySetResult();
        }

        using var lease = await pending;
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task IntakeAsync_ValidationFailure_ReleasesReservationForTheNextRequest()
    {
        var (intake, fileSystem) = Intake(Archive(), Limits(activeLeases: 1));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Func<Task> action = () => intake.IntakeAsync(Request(digest: new string('0', 64)), CancellationToken.None);
            (await action.Should().ThrowAsync<InvalidTemplatePackageException>()).Which.Failure.Should().Be(TemplatePackageFailure.DigestMismatch);
        }

        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);

        fileSystem.Received(4).OpenReadIfExists(ArchivePath);
    }

    [Fact]
    public async Task IntakeAsync_CancelledBeforeCapture_DoesNotOpenSource()
    {
        var (intake, fileSystem) = Intake(Archive());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => intake.IntakeAsync(Request(), cancellation.Token);

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task IntakeAsync_CancelledDuringCapture_ClosesSourceAndReleasesReservation()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var source = new BlockingReadStream(Archive());
        var bytes = Archive();
        var attempt = 0;
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ => attempt++ == 0 ? source : new MemoryStream(bytes, writable: false));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(activeLeases: 1));
        using CancellationTokenSource cancellation = new();
        var pending = intake.IntakeAsync(Request(), cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        cancellation.Cancel();
        Func<Task> action = () => pending;

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        source.Disposed.Should().BeTrue();
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
    }

    [Fact]
    public async Task IntakeAsync_CancelledAfterSourceRead_DoesNotReturnALease()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        using CancellationTokenSource cancellation = new();
        var source = new ObservedReadStream(Archive(), afterRead: cancellation.Cancel);
        fileSystem.OpenReadIfExists(ArchivePath).Returns(source);
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(activeLeases: 1));

        Func<Task> action = () => intake.IntakeAsync(Request(), cancellation.Token);

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task IntakeAsync_CancelledWhenSourceCloses_DoesNotReturnALeaseAndReleasesReservation()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        using CancellationTokenSource cancellation = new();
        var bytes = Archive();
        var source = new ObservedReadStream(bytes, afterDispose: cancellation.Cancel);
        var attempt = 0;
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ => attempt++ == 0 ? source : new MemoryStream(bytes, writable: false));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(activeLeases: 1));

        Func<Task> action = () => intake.IntakeAsync(Request(), cancellation.Token);

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        source.Disposed.Should().BeTrue();
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
    }

    [Fact]
    public async Task IntakeAsync_SourceCloseFailure_IsObservableAndDoesNotLeakReservation()
    {
        var bytes = Archive();
        var fileSystem = Substitute.For<IFileSystem>();
        var attempt = 0;
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ =>
            attempt++ == 0 ? new CloseFailureStream(bytes) : new MemoryStream(bytes, writable: false));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(activeLeases: 1));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        await action.Should().ThrowExactlyAsync<IOException>().WithMessage("source close failed");
        using var lease = await intake.IntakeAsync(Request(), CancellationToken.None);
    }

    [Fact]
    public async Task IntakeAsync_CancellationAndSourceCloseFailure_PreservesBothFailures()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        using CancellationTokenSource cancellation = new();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(new CloseFailureStream(Archive(), cancellation.Cancel));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits());

        Func<Task> action = () => intake.IntakeAsync(Request(), cancellation.Token);

        var failure = (await action.Should().ThrowExactlyAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().HaveCount(2);
        failure.InnerExceptions.OfType<OperationCanceledException>().Should().ContainSingle()
            .Which.CancellationToken.Should().Be(cancellation.Token);
        failure.InnerExceptions.OfType<IOException>().Should().ContainSingle().Which.Message.Should().Be("source close failed");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IntakeAsync_SourceReadAndCloseFailure_PreservesBothFailures(bool readAccessDenied, bool closeAccessDenied)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(new ReadAndCloseFailureStream(readAccessDenied, closeAccessDenied));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits());

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        var failure = (await action.Should().ThrowExactlyAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().HaveCount(2);
        failure.InnerExceptions.Select(exception => exception.Message).Should().Equal("source read failed", "source close failed");
        failure.InnerExceptions[0].GetType().Should().Be(readAccessDenied ? typeof(UnauthorizedAccessException) : typeof(IOException));
        failure.InnerExceptions[1].GetType().Should().Be(closeAccessDenied ? typeof(UnauthorizedAccessException) : typeof(IOException));
    }

    [Fact]
    public async Task IntakeAsync_ArchiveLimitAndSourceCloseFailure_PreservesRejectionAndCleanupFailure()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var bytes = Archive();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(new CloseFailureStream(bytes));
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits(archiveBytes: bytes.Length - 1));

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        var failure = (await action.Should().ThrowExactlyAsync<AggregateException>()).Which;
        failure.InnerExceptions.OfType<InvalidTemplatePackageException>().Should().ContainSingle()
            .Which.Failure.Should().Be(TemplatePackageFailure.ResourceLimitExceeded);
        failure.InnerExceptions.OfType<IOException>().Should().ContainSingle().Which.Message.Should().Be("source close failed");
    }

    [Fact]
    public async Task IntakeAsync_SourceReadFailure_PropagatesAndClosesSource()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var source = new ReadFailureStream();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(source);
        var intake = new LocalTemplatePackageIntake(fileSystem, Limits());

        Func<Task> action = () => intake.IntakeAsync(Request(), CancellationToken.None);

        await action.Should().ThrowExactlyAsync<IOException>().WithMessage("source read failed");
        source.Disposed.Should().BeTrue();
        fileSystem.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task IntakeAsync_NullRequest_ThrowsBeforeOpeningSource()
    {
        var (intake, fileSystem) = Intake(Archive());

        Func<Task> action = () => intake.IntakeAsync(null!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    private class ObservedReadStream(byte[] bytes, Action? afterRead = null, Action? afterDispose = null) : MemoryStream(bytes, writable: false)
    {
        public long BytesRead { get; private set; }

        public bool Disposed { get; private set; }

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException("Source lengths are not authoritative.");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            afterRead?.Invoke();
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            afterDispose?.Invoke();
        }
    }

    private sealed class BlockingReadStream(byte[] bytes) : ObservedReadStream(bytes)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class CloseFailureStream(byte[] bytes, Action? afterRead = null) : ObservedReadStream(bytes, afterRead)
    {
        public override ValueTask DisposeAsync()
        {
            base.Dispose(true);
            return ValueTask.FromException(new IOException("source close failed"));
        }
    }

    private sealed class ReadFailureStream() : ObservedReadStream([])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("source read failed"));
    }

    private sealed class ReadAndCloseFailureStream(bool readAccessDenied, bool closeAccessDenied) : ObservedReadStream([])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(readAccessDenied
                ? new UnauthorizedAccessException("source read failed")
                : new IOException("source read failed"));

        public override ValueTask DisposeAsync()
        {
            base.Dispose(true);
            return ValueTask.FromException(closeAccessDenied
                ? new UnauthorizedAccessException("source close failed")
                : new IOException("source close failed"));
        }
    }
}
