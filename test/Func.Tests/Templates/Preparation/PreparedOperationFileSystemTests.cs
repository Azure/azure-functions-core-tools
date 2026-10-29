// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Preparation;

namespace Azure.Functions.Cli.Tests.Templates.Preparation;

[Trait("Category", "Filesystem")]
public sealed class PreparedOperationFileSystemTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "func-prepared-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();

    public PreparedOperationFileSystemTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(".")]
    [InlineData("nested")]
    public async Task CommitAsync_PhysicalFilesystem_CreatesExactProjectAndConfigurationAndCleansStages(string projectRoot)
    {
        string target = Path.Combine(_directory, "parent", "project");
        string prefix = projectRoot == "." ? string.Empty : projectRoot + "/";
        string contentRoot = projectRoot == "." ? target : Path.Combine(target, projectRoot);
        byte[] bytes = [0, 255, 13, 10, 0];
        byte[] configuration = [123, 13, 10, 125];
        PreparedOperationPlan plan = new(target, PreparedOperationTestFileSystem.Identity,
            [new(prefix + "host.json", PreparedOutputKind.File, [123, 125]), new(prefix + "src/item.bin", PreparedOutputKind.File, bytes),
                new(prefix + "empty", PreparedOutputKind.Directory)], new(projectRoot, configuration));
        PreparedOperation operation = new(plan, _fileSystem);

        operation.Preview().Should().BeSameAs(plan);
        Directory.Exists(target).Should().BeFalse();
        PreparedOperationCompletion result = await operation.CommitAsync(CancellationToken.None);
        await operation.DisposeAsync();

        (await File.ReadAllBytesAsync(Path.Combine(contentRoot, "src", "item.bin"), CancellationToken.None)).Should().Equal(bytes);
        (await File.ReadAllBytesAsync(Path.Combine(contentRoot, ".func", "config.json"), CancellationToken.None)).Should().Equal(configuration);
        Directory.Exists(Path.Combine(contentRoot, "empty")).Should().BeTrue();
        result.CompletedEffects.Should().HaveCount(projectRoot == "." ? 8 : 9);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Should().HaveCount(3);
        Directory.GetDirectories(_directory, "*", SearchOption.AllDirectories).Should().HaveCount(projectRoot == "." ? 5 : 6);
    }

    [Fact]
    public async Task PublishFileNoReplace_OccupiedDestination_RefusesExistingFileAndDirectory()
    {
        string stagingDirectory = _fileSystem.CreateStagingDirectory(_directory);
        string source = Path.Combine(stagingDirectory, "source");
        string targetFile = Path.Combine(_directory, "existing");
        string targetDirectory = Path.Combine(_directory, "existing-directory");
        await File.WriteAllBytesAsync(source, [1], CancellationToken.None);
        await File.WriteAllBytesAsync(targetFile, [99], CancellationToken.None);
        Directory.CreateDirectory(targetDirectory);

        Action fileCollision = () => _fileSystem.PublishFileNoReplace(source, targetFile);
        Action directoryCollision = () => _fileSystem.PublishFileNoReplace(source, targetDirectory);

        fileCollision.Should().Throw<IOException>();
        directoryCollision.Should().Throw<IOException>();
        (await File.ReadAllBytesAsync(targetFile, CancellationToken.None)).Should().Equal(99);
        (await File.ReadAllBytesAsync(source, CancellationToken.None)).Should().Equal(1);
    }

    [Fact]
    public async Task PublishFileNoReplace_Success_StagingRemovalDoesNotRemovePublishedBytes()
    {
        string stagingDirectory = _fileSystem.CreateStagingDirectory(_directory);
        string source = Path.Combine(stagingDirectory, "source");
        string destination = Path.Combine(_directory, "published");
        byte[] bytes = [0, 255, 13, 10];
        await File.WriteAllBytesAsync(source, bytes, CancellationToken.None);

        _fileSystem.PublishFileNoReplace(source, destination);

        (await File.ReadAllBytesAsync(destination, CancellationToken.None)).Should().Equal(bytes);
        if (!OperatingSystem.IsWindows())
        {
            File.Exists(source).Should().BeTrue("Unix publication retains the staging link for separately observable cleanup");
        }

        Directory.Delete(stagingDirectory, recursive: true);
        (await File.ReadAllBytesAsync(destination, CancellationToken.None)).Should().Equal(bytes);
    }

    [Fact]
    public async Task PublishFileNoReplace_ConcurrentPublishers_ExactlyOneWinsWithoutReplacement()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            string firstStage = _fileSystem.CreateStagingDirectory(_directory);
            string secondStage = _fileSystem.CreateStagingDirectory(_directory);
            string firstSource = Path.Combine(firstStage, "source");
            string secondSource = Path.Combine(secondStage, "source");
            string destination = Path.Combine(_directory, $"published-{attempt}");
            await File.WriteAllBytesAsync(firstSource, [1], CancellationToken.None);
            await File.WriteAllBytesAsync(secondSource, [2], CancellationToken.None);
            using Barrier start = new(2);

            bool[] winners = await Task.WhenAll(PublishAsync(firstSource), PublishAsync(secondSource));

            winners.Count(winner => winner).Should().Be(1);
            (await File.ReadAllBytesAsync(destination, CancellationToken.None)).Should().Equal(winners[0] ? (byte)1 : (byte)2);
            string losingSource = winners[0] ? secondSource : firstSource;
            (await File.ReadAllBytesAsync(losingSource, CancellationToken.None)).Should().Equal(winners[0] ? (byte)2 : (byte)1);

            Task<bool> PublishAsync(string source) => Task.Run(() =>
            {
                start.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                try
                {
                    _fileSystem.PublishFileNoReplace(source, destination);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            });
        }
    }

    [Fact]
    public async Task PublishFileNoReplace_StagingOutsideDestinationParent_RefusesWithoutMutation()
    {
        string stagingDirectory = _fileSystem.CreateStagingDirectory(_directory);
        string source = Path.Combine(stagingDirectory, "source");
        string destination = Path.Combine(_directory, "other-parent", "destination");
        await File.WriteAllBytesAsync(source, [1], CancellationToken.None);

        Action publish = () => _fileSystem.PublishFileNoReplace(source, destination);

        publish.Should().Throw<ArgumentException>().WithMessage("*directly beneath*");
        File.Exists(source).Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(destination)).Should().BeFalse();
    }

    [Fact]
    public void PublishFileNoReplace_MissingStagedFile_RefusesWithoutCreatingDestination()
    {
        string stagingDirectory = _fileSystem.CreateStagingDirectory(_directory);
        string source = Path.Combine(stagingDirectory, "missing");
        string destination = Path.Combine(_directory, "destination");

        Action publish = () => _fileSystem.PublishFileNoReplace(source, destination);

        publish.Should().Throw<IOException>();
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void Constructor_TargetExistenceChanges_RetainsTheSameLexicalTargetWithoutMutation()
    {
        string target = Path.Combine(_directory, "SHORT~1", "nested", "..", "project");
        string expected = Path.Combine(_directory, "SHORT~1", "project");
        PreparedOutputEntry[] entries = [new("item", PreparedOutputKind.File)];
        PreparedOperationPlan absent = new(target, PreparedOperationTestFileSystem.Identity, entries);
        Directory.CreateDirectory(expected);
        File.WriteAllBytes(Path.Combine(expected, "unrelated"), [99]);

        PreparedOperationPlan existing = new(target, PreparedOperationTestFileSystem.Identity, entries);

        absent.TargetDirectory.Should().Be(expected);
        existing.TargetDirectory.Should().Be(expected);
        Directory.GetFiles(expected).Should().ContainSingle();
        Directory.Exists(Path.Combine(_directory, "SHORT~1", "nested")).Should().BeFalse();
    }

    [Fact]
    public void CreateNewDirectory_NoReplace_RefusesExistingDirectoryAndFile()
    {
        string targetDirectory = Path.Combine(_directory, "existing-directory");
        string targetFile = Path.Combine(_directory, "existing-file");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllBytes(targetFile, [99]);

        Action directoryCollision = () => _fileSystem.CreateNewDirectory(targetDirectory);
        Action fileCollision = () => _fileSystem.CreateNewDirectory(targetFile);

        directoryCollision.Should().Throw<IOException>();
        fileCollision.Should().Throw<IOException>();
        File.ReadAllBytes(targetFile).Should().Equal(99);
    }

    [Fact]
    public void CreateNewDirectory_AbsentPath_CreatesOneDirectoryWithoutCreatingParents()
    {
        string target = Path.Combine(_directory, "new-directory");
        string missingParent = Path.Combine(_directory, "absent");

        _fileSystem.CreateNewDirectory(target);
        Action missing = () => _fileSystem.CreateNewDirectory(Path.Combine(missingParent, "child"));

        Directory.Exists(target).Should().BeTrue();
        missing.Should().Throw<DirectoryNotFoundException>();
        Directory.Exists(missingParent).Should().BeFalse();
    }

    [Fact]
    public void CreateStagingDirectory_ExistingParent_ReturnsDistinctOwnedEmptyDirectories()
    {
        string existing = Path.Combine(_directory, "existing");
        Directory.CreateDirectory(existing);
        File.WriteAllBytes(Path.Combine(existing, "user-content"), [99]);

        string first = _fileSystem.CreateStagingDirectory(_directory);
        string second = _fileSystem.CreateStagingDirectory(_directory);

        first.Should().NotBe(second);
        Path.GetDirectoryName(first).Should().Be(_directory);
        Path.GetDirectoryName(second).Should().Be(_directory);
        Directory.GetFileSystemEntries(first).Should().BeEmpty();
        Directory.GetFileSystemEntries(second).Should().BeEmpty();
        File.ReadAllBytes(Path.Combine(existing, "user-content")).Should().Equal(99);
    }

    [Fact]
    public void CreateStagingDirectory_MissingParent_RefusesWithoutCreatingAncestors()
    {
        string missingParent = Path.Combine(_directory, "absent");

        Action create = () => _fileSystem.CreateStagingDirectory(missingParent);

        create.Should().Throw<DirectoryNotFoundException>();
        Directory.Exists(missingParent).Should().BeFalse();
    }

    [Fact]
    public async Task CreateNewFile_OccupiedPath_RefusesWithoutTruncatingContent()
    {
        string path = Path.Combine(_directory, "existing");
        await File.WriteAllBytesAsync(path, [99], CancellationToken.None);

        Action create = () => _fileSystem.CreateNewFile(path).Dispose();

        create.Should().Throw<IOException>();
        (await File.ReadAllBytesAsync(path, CancellationToken.None)).Should().Equal(99);
    }

    [Fact]
    public async Task GetAttributesIfExists_OrdinaryPaths_DistinguishesKindsAndAbsence()
    {
        string file = Path.Combine(_directory, "file");
        await File.WriteAllBytesAsync(file, [1], CancellationToken.None);

        _fileSystem.GetAttributesIfExists(file).Should().NotBeNull();
        (_fileSystem.GetAttributesIfExists(_directory)!.Value & FileAttributes.Directory).Should().NotBe(0);
        _fileSystem.GetAttributesIfExists(Path.Combine(_directory, "missing")).Should().BeNull();
        _fileSystem.GetAttributesIfExists(Path.Combine(_directory, "missing", "child")).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FilePrimitives_InvalidPaths_Reject(string? path)
    {
        Action attributes = () => _fileSystem.GetAttributesIfExists(path!);
        Action create = () => _fileSystem.CreateNewFile(path!).Dispose();
        Action directory = () => _fileSystem.CreateNewDirectory(path!);
        Action staging = () => _fileSystem.CreateStagingDirectory(path!);
        Action publishSource = () => _fileSystem.PublishFileNoReplace(path!, Path.Combine(_directory, "destination"));
        Action publishDestination = () => _fileSystem.PublishFileNoReplace(Path.Combine(_directory, "stage", "source"), path!);

        attributes.Should().Throw<ArgumentException>();
        create.Should().Throw<ArgumentException>();
        directory.Should().Throw<ArgumentException>();
        staging.Should().Throw<ArgumentException>();
        publishSource.Should().Throw<ArgumentException>();
        publishDestination.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DirectoryPrimitives_EmbeddedNull_RejectsBeforeCallingNativeCode()
    {
        string path = Path.Combine(_directory, "unsafe\0suffix");
        Action directory = () => _fileSystem.CreateNewDirectory(path);
        Action staging = () => _fileSystem.CreateStagingDirectory(path);

        directory.Should().Throw<ArgumentException>();
        staging.Should().Throw<ArgumentException>();
        Directory.Exists(Path.Combine(_directory, "unsafe")).Should().BeFalse();
    }

    [Fact]
    public void PublishFileNoReplace_RelativeOrNullContainingPaths_RejectsBeforeNativePublication()
    {
        string source = Path.Combine(_directory, "stage", "source");
        string destination = Path.Combine(_directory, "destination");
        Action relativeSource = () => _fileSystem.PublishFileNoReplace("relative", destination);
        Action relativeDestination = () => _fileSystem.PublishFileNoReplace(source, "relative");
        Action nullSource = () => _fileSystem.PublishFileNoReplace(source + "\0suffix", destination);
        Action nullDestination = () => _fileSystem.PublishFileNoReplace(source, destination + "\0suffix");

        relativeSource.Should().Throw<ArgumentException>();
        relativeDestination.Should().Throw<ArgumentException>();
        nullSource.Should().Throw<ArgumentException>();
        nullDestination.Should().Throw<ArgumentException>();
        File.Exists(destination).Should().BeFalse();
    }
}
