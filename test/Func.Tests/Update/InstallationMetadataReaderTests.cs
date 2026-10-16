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
    private static readonly string _baseDirectory =
        Path.GetFullPath(Path.Combine("fake", "install")) + Path.DirectorySeparatorChar;
    private static readonly string _metadataPath =
        Path.Combine(_baseDirectory, InstallationMetadataReader.MetadataFileName);
    private static readonly byte[] _invalidUtf8 = [0xC3, 0x28];

    [Theory]
    [InlineData("standalone", "in-place", (int)InstallationUpdatePolicy.InPlace)]
    [InlineData("future-package-manager", "external", (int)InstallationUpdatePolicy.External)]
    [InlineData("future-installer", "in-place", (int)InstallationUpdatePolicy.InPlace)]
    public async Task ReadAsync_ValidMetadata_ReturnsOpaqueInstallMethodAndPolicy(
        string method,
        string policy,
        int expectedPolicy)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson(method, policy));

        // Act
        InstallationMetadata metadata = await reader.ReadAsync(CancellationToken.None);

        // Assert
        metadata.InstallMethod.Should().Be(method);
        metadata.UpdatePolicy.Should().Be((InstallationUpdatePolicy)expectedPolicy);
    }

    [Fact]
    public async Task ReadAsync_ValidMetadata_ReadsFromApplicationBaseDirectory()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        CancellationToken cancellationToken = CancellationToken.None;
        SetMetadata(fileSystem, ValidJson("standalone", "in-place"));

        // Act
        await reader.ReadAsync(cancellationToken);

        // Assert
        fileSystem.Received(1).OpenReadIfExists(_metadataPath);
    }

    [Fact]
    public async Task ReadAsync_DefaultEnvironmentOptions_ReadsFromAppContextBaseDirectory()
    {
        // Arrange
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        string metadataPath = Path.Combine(AppContext.BaseDirectory, InstallationMetadataReader.MetadataFileName);
        fileSystem.OpenReadIfExists(metadataPath)
            .Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes(ValidJson("standalone", "in-place"))));
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions()));

        // Act
        await reader.ReadAsync(CancellationToken.None);

        // Assert
        fileSystem.Received(1).OpenReadIfExists(metadataPath);
    }

    [Fact]
    public async Task ReadAsync_ValidMetadataAtLimit_ReturnsMetadata()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        string json = ValidJson("standalone", "in-place");
        string paddedJson = json.PadRight(InstallationMetadataReader.MaximumMetadataByteCount);
        Encoding.UTF8.GetByteCount(paddedJson).Should().Be(InstallationMetadataReader.MaximumMetadataByteCount);
        SetMetadata(fileSystem, paddedJson);

        // Act
        InstallationMetadata metadata = await reader.ReadAsync(CancellationToken.None);

        // Assert
        metadata.Should().Be(new InstallationMetadata("standalone", InstallationUpdatePolicy.InPlace));
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
        fileSystem.OpenReadIfExists(_metadataPath)
            .Returns(new MemoryStream(new byte[InstallationMetadataReader.MaximumMetadataByteCount + 1]));

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
        fileSystem.OpenReadIfExists(_metadataPath).Returns(new MemoryStream(payload));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        InvalidInstallationMetadataException exception =
            (await act.Should().ThrowAsync<InvalidInstallationMetadataException>()).Which;
        exception.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
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

    [Fact]
    public async Task ReadAsync_UnsupportedSchemaWithUnknownProperty_ThrowsUnsupportedInstallationMetadataSchemaException()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, """{"schemaVersion":2,"newField":1}""");

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnsupportedInstallationMetadataSchemaException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" npm")]
    [InlineData("npm ")]
    public async Task ReadAsync_InvalidInstallMethod_ThrowsInvalidInstallationMetadataException(string method)
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson(method, "external"));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidInstallationMetadataException>();
    }

    [Fact]
    public async Task ReadAsync_UnknownUpdatePolicy_ThrowsInvalidInstallationMetadataException()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        SetMetadata(fileSystem, ValidJson("standalone", "managed"));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidInstallationMetadataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("func")]
    public async Task ReadAsync_InvalidApplicationBaseDirectory_ThrowsInvalidOperationException(string baseDirectory)
    {
        // Arrange
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions { BaseDirectory = baseDirectory }));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        fileSystem.DidNotReceiveWithAnyArgs().OpenReadIfExists(default!);
    }

    [Fact]
    public async Task ReadAsync_MetadataAccessDenied_PropagatesUnauthorizedAccessException()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        fileSystem.OpenReadIfExists(_metadataPath)
            .Returns<Stream?>(_ => throw new UnauthorizedAccessException("access denied"));

        // Act
        Func<Task> act = () => reader.ReadAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*access denied*");
    }

    [Fact]
    public async Task ReadAsync_PreCancelled_DoesNotReadMetadata()
    {
        // Arrange
        (InstallationMetadataReader reader, IFileSystem fileSystem) = CreateReader();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => reader.ReadAsync(cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.DidNotReceiveWithAnyArgs().OpenReadIfExists(default!);
    }

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        Action act = () => _ = new InstallationMetadataReader(
            null!,
            Options.Create(new CliEnvironmentOptions { BaseDirectory = _baseDirectory }));

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
        fileSystem.OpenReadIfExists(Arg.Any<string>()).Returns((Stream?)null);
        InstallationMetadataReader reader = new(
            fileSystem,
            Options.Create(new CliEnvironmentOptions { BaseDirectory = _baseDirectory }));
        return (reader, fileSystem);
    }

    private static void SetMetadata(IFileSystem fileSystem, string json)
    {
        fileSystem.OpenReadIfExists(_metadataPath)
            .Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static string ValidJson(string method, string policy) =>
        $$"""{"schemaVersion":1,"installMethod":"{{method}}","updatePolicy":"{{policy}}"}""";
}
