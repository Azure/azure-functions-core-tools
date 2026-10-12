// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class InstallationMetadataReaderTests
{
    private static readonly string _processPath = Path.GetFullPath(Path.Combine("fake", "install", "func"));
    private static readonly string _physicalProcessPath =
        Path.GetFullPath(Path.Combine("physical", "install", "func"));
    private static readonly string _metadataPath =
        Path.Combine(Path.GetDirectoryName(_physicalProcessPath)!, InstallationMetadataReader.MetadataFileName);
    private static readonly byte[] _invalidUtf8 = [0xC3, 0x28];

    [Theory]
    [InlineData("standalone", "in-place", (int)InstallationMethod.Standalone, (int)InstallationUpdatePolicy.InPlace)]
    [InlineData("npm", "external", (int)InstallationMethod.Npm, (int)InstallationUpdatePolicy.External)]
    [InlineData("homebrew", "external", (int)InstallationMethod.Homebrew, (int)InstallationUpdatePolicy.External)]
    [InlineData("winget", "external", (int)InstallationMethod.Winget, (int)InstallationUpdatePolicy.External)]
    [InlineData("msi", "external", (int)InstallationMethod.Msi, (int)InstallationUpdatePolicy.External)]
    public async Task ReadAsync_ValidPair_ReturnsMetadata(
        string method,
        string policy,
        int expectedMethod,
        int expectedPolicy)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson(method, policy));

        // Act
        InstallationMetadata metadata = await reader.ReadAsync(CancellationToken.None);

        // Assert
        metadata.InstallMethod.Should().Be((InstallationMethod)expectedMethod);
        metadata.UpdatePolicy.Should().Be((InstallationUpdatePolicy)expectedPolicy);
    }

    [Fact]
    public async Task ReadAsync_ReadsMetadataBesideExecutableWithFourKiBCap()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        CancellationToken cancellationToken = CancellationToken.None;
        SetMetadata(fileSystem, ValidJson("standalone", "in-place"));

        // Act
        await reader.ReadAsync(cancellationToken);

        // Assert
        fileSystem.Received(1).ResolvePhysicalPath(_processPath);
        await fileSystem.Received(1).ReadAllBytesIfExistsAsync(
            _metadataPath,
            InstallationMetadataReader.MaximumMetadataByteCount,
            cancellationToken);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        (InstallationMetadataReader reader, _) = CreateReader();

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        FileNotFoundException exception = (await act.Should().ThrowAsync<FileNotFoundException>()).Which;
        exception.FileName.Should().Be(_metadataPath);
        exception.Message.Should().Contain("Could not determine the installation method");
        exception.Message.Should().Contain(Constants.DocsUrl);
    }

    [Fact]
    public async Task ReadAsync_OversizedFile_ThrowsInstallationMetadataTooLargeException()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        fileSystem.ReadAllBytesIfExistsAsync(
                _metadataPath,
                InstallationMetadataReader.MaximumMetadataByteCount,
                Arg.Any<CancellationToken>())
            .Returns<Task<byte[]?>>(_ => throw new InvalidDataException("too large"));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        InstallationMetadataTooLargeException exception =
            (await act.Should().ThrowAsync<InstallationMetadataTooLargeException>()).Which;
        exception.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Theory]
    [MemberData(nameof(InvalidJsonPayloads))]
    public async Task ReadAsync_InvalidJson_ThrowsInvalidInstallationMetadataException(byte[] payload)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        fileSystem.ReadAllBytesIfExistsAsync(_metadataPath, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(payload);

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        InvalidInstallationMetadataException exception =
            (await act.Should().ThrowAsync<InvalidInstallationMetadataException>()).Which;
        exception.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"installMethod":"standalone","updatePolicy":"in-place"}""")]
    [InlineData("""{"schemaVersion":1,"updatePolicy":"in-place"}""")]
    [InlineData("""{"schemaVersion":1,"installMethod":"standalone"}""")]
    [InlineData("""{"schemaVersion":1,"installMethod":"standalone","updatePolicy":"in-place","future":true}""")]
    [InlineData("""{"schemaVersion":2,"schemaVersion":1,"installMethod":"standalone","updatePolicy":"in-place"}""")]
    [InlineData("""{"schemaVersion":1,"installMethod":"npm","installMethod":"standalone","updatePolicy":"in-place"}""")]
    [InlineData("""{"schemaVersion":1,"installMethod":"standalone","updatePolicy":"external","updatePolicy":"in-place"}""")]
    [InlineData("null")]
    public async Task ReadAsync_MissingOrUnknownProperty_ThrowsInvalidInstallationMetadataException(string json)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, json);

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidInstallationMetadataException>();
    }

    [Fact]
    public async Task ReadAsync_UnsupportedSchema_ThrowsUnsupportedInstallationMetadataSchemaException()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(
            fileSystem,
            """{"schemaVersion":2,"installMethod":"standalone","updatePolicy":"in-place"}""");

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnsupportedInstallationMetadataSchemaException>();
    }

    [Theory]
    [InlineData("archive", "in-place")]
    [InlineData("standalone", "managed")]
    public async Task ReadAsync_UnknownWireValue_ThrowsInvalidInstallationMetadataException(string method, string policy)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson(method, policy));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidInstallationMetadataException>();
    }

    [Theory]
    [InlineData("standalone", "external")]
    [InlineData("npm", "in-place")]
    [InlineData("homebrew", "in-place")]
    [InlineData("winget", "in-place")]
    [InlineData("msi", "in-place")]
    public async Task ReadAsync_ContradictoryPair_ThrowsInvalidInstallationMetadataException(string method, string policy)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson(method, policy));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidInstallationMetadataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("func")]
    public async Task ReadAsync_UnresolvedExecutablePath_ThrowsInvalidOperationException(string processPath)
    {
        // Arrange
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions { ProcessPath = processPath }));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await fileSystem.DidNotReceiveWithAnyArgs()
            .ReadAllBytesIfExistsAsync(default!, default, default);
    }

    [Fact]
    public async Task ReadAsync_PhysicalPathResolutionFails_PropagatesIOExceptionWithoutReadingMetadata()
    {
        // Arrange
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.ResolvePhysicalPath(_processPath)
            .Returns<string>(_ => throw new IOException("link cycle"));
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions { ProcessPath = _processPath }));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IOException>().WithMessage("*link cycle*");
        await fileSystem.DidNotReceiveWithAnyArgs()
            .ReadAllBytesIfExistsAsync(default!, default, default);
    }

    [Fact]
    public async Task ReadAsync_PreCancelled_DoesNotResolveExecutablePath()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => reader.ReadAsync(cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.DidNotReceiveWithAnyArgs().ResolvePhysicalPath(default!);
        await fileSystem.DidNotReceiveWithAnyArgs()
            .ReadAllBytesIfExistsAsync(default!, default, default);
    }

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        Action act = () => _ = new InstallationMetadataReader(
            null!,
            Options.Create(new CliEnvironmentOptions { ProcessPath = _processPath }));

        act.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
    }

    [Fact]
    public void Constructor_NullEnvironmentOptions_ThrowsArgumentNullException()
    {
        Action act = () => _ = new InstallationMetadataReader(Substitute.For<IFileSystem>(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("environmentOptions");
    }

    public static TheoryData<byte[]> InvalidJsonPayloads =>
    [
        Encoding.UTF8.GetBytes("{"),
        _invalidUtf8,
    ];

    private static (InstallationMetadataReader Reader, IFileSystem FileSystem) CreateReader()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.ResolvePhysicalPath(_processPath).Returns(_physicalProcessPath);
        fileSystem.ReadAllBytesIfExistsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions { ProcessPath = _processPath }));
        return (reader, fileSystem);
    }

    private static void SetMetadata(IFileSystem fileSystem, string json)
    {
        fileSystem.ReadAllBytesIfExistsAsync(_metadataPath, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes(json));
    }

    private static string ValidJson(string method, string policy) =>
        $$"""{"schemaVersion":1,"installMethod":"{{method}}","updatePolicy":"{{policy}}"}""";
}
