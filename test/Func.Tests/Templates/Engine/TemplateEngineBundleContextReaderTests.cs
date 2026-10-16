// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateEngineBundleContextReaderTests
{
    private readonly IFileSystem _files = Substitute.For<IFileSystem>();
    private readonly string _path = Path.GetFullPath("selected-bundle");

    [Fact]
    public async Task ReadAsync_SelectedResolution_ReadsOnlyItsLocalMetadataAndCapturesEveryExtension()
    {
        using CancellationTokenSource cancellation = new();
        var resolution = Resolution();
        var metadataPath = Path.Combine(_path, "bin", "extensions.json");
        _files.ReadAllTextIfExistsAsync(metadataPath, cancellation.Token).Returns(
            """{"extensions":[{"name":"storage","bindings":["blobTrigger","blob"]},{"name":"events","bindings":["eventGridTrigger"]},{"name":"startup"}]}""");
        TemplateEngineBundleContextReader reader = new(_files);

        var bundle = await reader.ReadAsync(resolution, cancellation.Token);

        bundle.Id.Should().Be(resolution.BundleId);
        bundle.Version.Should().Be(resolution.Version);
        bundle.Path.Should().Be(_path);
        bundle.Bindings!.Diagnostic.Should().BeNull();
        bundle.Bindings.MatchesSource(bundle).Should().BeTrue();
        bundle.Bindings.Contains("BLOBTRIGGER").Should().BeTrue();
        bundle.Bindings.Contains("blob").Should().BeTrue();
        bundle.Bindings.Contains("eventGridTrigger").Should().BeTrue();
        bundle.Bindings.Contains("unknownFutureBinding").Should().BeFalse();
        await _files.Received(1).ReadAllTextIfExistsAsync(metadataPath, cancellation.Token);
        _files.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"extensions\":{}}")]
    [InlineData("{\"extensions\":[null]}")]
    [InlineData("{\"extensions\":[{\"bindings\":null}]}")]
    [InlineData("{\"extensions\":[{\"bindings\":\"blob\"}]}")]
    [InlineData("{\"extensions\":[{\"bindings\":[\"blob\",3]}]}")]
    [InlineData("{\"extensions\":[{\"bindings\":[\"blob\",\"\"]}]}")]
    [InlineData("{\"extensions\":[{\"bindings\":[\"blob\",\"\\uD800\"]}]}")]
    [InlineData("{\"extensions\":[],\"Extensions\":[{\"bindings\":[\"blob\"]}]}")]
    [InlineData("{\"extensions\":[{\"bindings\":[\"blob\"],\"Bindings\":[]}]}")]
    public async Task ReadAsync_MissingOrMalformedMetadata_RetainsExplicitFailureWithoutPartialCapabilities(string? metadata)
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(metadata);
        TemplateEngineBundleContextReader reader = new(_files);

        var bundle = await reader.ReadAsync(Resolution(), CancellationToken.None);

        bundle.Bindings!.Diagnostic.Should().Contain("Cannot read binding capabilities").And.Contain(_path);
        bundle.Bindings.MatchesSource(bundle).Should().BeTrue();
        bundle.Bindings.Contains("blob").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_UnreadableMetadata_RetainsExplicitFailure(bool unauthorized)
    {
        Exception failure = unauthorized ? new UnauthorizedAccessException("permissions") : new IOException("offline");
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        TemplateEngineBundleContextReader reader = new(_files);

        var bundle = await reader.ReadAsync(Resolution(), CancellationToken.None);

        bundle.Bindings!.Diagnostic.Should().Contain(failure.Message).And.Contain(_path);
        bundle.Bindings.Contains("blob").Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"extensions\":[]}")]
    [InlineData("{\"extensions\":[{\"bindings\":[]}]}")]
    [InlineData("{\"Extensions\":[{\"Bindings\":[]}]}")]
    public async Task ReadAsync_EmptyValidMetadata_CapturesKnownEmptySet(string metadata)
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(metadata);
        TemplateEngineBundleContextReader reader = new(_files);

        var bundle = await reader.ReadAsync(Resolution(), CancellationToken.None);

        bundle.Bindings!.Diagnostic.Should().BeNull();
        bundle.Bindings.Contains("blob").Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_MetadataChanges_DoesNotMutateEarlierContext()
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            """{"extensions":[{"bindings":["blob"]}]}""",
            """{"extensions":[{"bindings":["queue"]}]}""");
        TemplateEngineBundleContextReader reader = new(_files);
        var first = await reader.ReadAsync(Resolution(), CancellationToken.None);

        var second = await reader.ReadAsync(Resolution(), CancellationToken.None);

        first.Bindings!.Contains("blob").Should().BeTrue();
        first.Bindings.Contains("queue").Should().BeFalse();
        second.Bindings!.Contains("blob").Should().BeFalse();
        second.Bindings.Contains("queue").Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_UnexpectedProviderFault_Propagates()
    {
        var failure = new InvalidOperationException("provider bug");
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        TemplateEngineBundleContextReader reader = new(_files);

        Func<Task> read = () => reader.ReadAsync(Resolution(), CancellationToken.None);

        (await read.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ReadAsync_ProviderCancellation_Propagates()
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        TemplateEngineBundleContextReader reader = new(_files);

        Func<Task> read = () => reader.ReadAsync(Resolution(), CancellationToken.None);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_PreCanceled_DoesNotReadFiles()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        TemplateEngineBundleContextReader reader = new(_files);

        Func<Task> read = () => reader.ReadAsync(Resolution(), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        _files.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_CancellationDuringIoFailure_StillPropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<string?>(new IOException("read interrupted"));
        });
        TemplateEngineBundleContextReader reader = new(_files);

        Func<Task> read = () => reader.ReadAsync(Resolution(), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_NullResolution_Throws()
    {
        TemplateEngineBundleContextReader reader = new(_files);

        Func<Task> read = () => reader.ReadAsync(null!, CancellationToken.None);

        await read.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAsync_RelativeResolvedPath_ThrowsWithoutReadingFiles()
    {
        TemplateEngineBundleContextReader reader = new(_files);
        var resolution = Resolution() with { Path = "relative" };

        Func<Task> read = () => reader.ReadAsync(resolution, CancellationToken.None);

        await read.Should().ThrowAsync<ArgumentException>();
        _files.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Action create = () => new TemplateEngineBundleContextReader(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
    }

    private ExtensionBundleResolution.Resolved Resolution() => new("selected.bundle", "4.20.0", _path, null);
}
