// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NewExecutionBundleBindingContextTests
{
    private readonly IFileSystem _files = Substitute.For<IFileSystem>();
    private readonly IFunctionsProjectResolver _projects = Substitute.For<IFunctionsProjectResolver>();
    private readonly IProfileResolver _profiles = Substitute.For<IProfileResolver>();
    private readonly IOptionsMonitor<StackOptions> _options = Substitute.For<IOptionsMonitor<StackOptions>>();
    private readonly IProjectInitializer _initializer = Substitute.For<IProjectInitializer>();
    private readonly IHostJsonBundleSectionReader _host = Substitute.For<IHostJsonBundleSectionReader>();
    private readonly IExtensionBundleResolver _bundles = Substitute.For<IExtensionBundleResolver>();
    private readonly WorkingDirectory _root = new(new DirectoryInfo(Path.GetFullPath("binding-project")), WasExplicit: true);
    private readonly string _bundlePath = Path.GetFullPath("actual-selected-bundle");

    public NewExecutionBundleBindingContextTests()
    {
        _files.DirectoryExists(_root.Info.FullName).Returns(true);
        _files.FileExists(Path.Combine(_root.Info.FullName, "host.json")).Returns(true);
        var project = Substitute.For<FunctionsProject>();
        project.WorkingDirectory.Returns(_root);
        project.StackName.Returns("node");
        project.Language.Returns("JavaScript");
        _projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectResolutionResult.Resolved(project, "resolved"));
        _initializer.Stack.Returns("node");
        _initializer.SupportedLanguages.Returns(["JavaScript"]);
        _options.Get(_root.Info.FullName).Returns(new StackOptions());
        _profiles.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProfileResolution.None([]));
        _host.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0,5.0)"));
        _bundles.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.20.0", _bundlePath, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ControlledComposition_RetainsExactResolutionAndReadsOnlyWhenEnabled(bool enabled)
    {
        using CancellationTokenSource cancellation = new();
        var metadataPath = Path.Combine(_bundlePath, "bin", "extensions.json");
        _files.ReadAllTextIfExistsAsync(metadataPath, cancellation.Token)
            .Returns("""{"extensions":[{"bindings":["blob"]}]}""");
        var resolver = Resolver(enabled);

        var result = await resolver.ResolveAsync(Request(), canPrompt: false, cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.EnableBundleBindingConstraints.Should().Be(enabled);
        var bundle = result.Context.Bundle!;
        bundle.Id.Should().Be(BundleHelpers.StableBundleId);
        bundle.Version.Should().Be("4.20.0");
        bundle.Path.Should().Be(_bundlePath);
        if (enabled)
        {
            bundle.Bindings!.Contains("blob").Should().BeTrue();
            await _files.Received(1).ReadAllTextIfExistsAsync(metadataPath, cancellation.Token);
        }
        else
        {
            bundle.Bindings.Should().BeNull();
            await _files.DidNotReceive().ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task ResolveAsync_SelectedBundleChanges_ReadsNewSourceWithoutReusingPreviousCapabilities()
    {
        var secondPath = Path.GetFullPath("another-selected-bundle");
        var first = new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.20.0", _bundlePath, null);
        var second = new ExtensionBundleResolution.Resolved(BundleHelpers.PreviewBundleId, "4.21.0", secondPath, null);
        _bundles.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>()).Returns(first, second, first);
        _files.ReadAllTextIfExistsAsync(Path.Combine(_bundlePath, "bin", "extensions.json"), Arg.Any<CancellationToken>())
            .Returns("""{"extensions":[{"bindings":["blob"]}]}""");
        _files.ReadAllTextIfExistsAsync(Path.Combine(secondPath, "bin", "extensions.json"), Arg.Any<CancellationToken>())
            .Returns("""{"extensions":[{"bindings":["queue"]}]}""");
        var resolver = Resolver(enabled: true);
        var before = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        var changed = await resolver.ResolveAsync(Request(), false, CancellationToken.None);
        var returned = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        before.Context!.Bundle!.Bindings!.Contains("blob").Should().BeTrue();
        changed.Context!.Bundle!.Id.Should().Be(second.BundleId);
        changed.Context.Bundle.Version.Should().Be(second.Version);
        changed.Context.Bundle.Path.Should().Be(second.Path);
        changed.Context.Bundle.Bindings!.Contains("blob").Should().BeFalse();
        changed.Context.Bundle.Bindings.Contains("queue").Should().BeTrue();
        returned.Context!.Bundle!.Bindings!.Contains("blob").Should().BeTrue();
        returned.Context.Bundle.Bindings.Contains("queue").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_NoBundle_DoesNotReadMetadataEvenWhenComposed(bool enabled)
    {
        _host.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        var resolver = Resolver(enabled);

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Context!.Bundle.Should().BeNull();
        result.Context.EnableBundleBindingConstraints.Should().Be(enabled);
        await _files.DidNotReceive().ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bundles.DidNotReceive().ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MissingMetadata_PreservesFailureSnapshotInsteadOfInventingCapabilities()
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        var resolver = Resolver(enabled: true);

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Failure.Should().BeNull();
        result.Context!.Bundle!.Bindings!.Diagnostic.Should().Contain("metadata file is missing");
        FuncTemplateConstraintEvaluator evaluator = new(new(), enableBundleBindingConstraints: true);
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"bindings":{"type":"func-bundle-bindings","args":["blob"]}}}""")[0];
        evaluator.Evaluate(declaration, new TemplateConstraintEvaluationSnapshot([]), result.Context.Bundle)
            .Should().Be(FuncTemplateConstraintOutcome.Failed);
    }

    [Fact]
    public async Task ResolveAsync_UnexpectedReaderDependencyFault_Propagates()
    {
        _files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider defect"));
        var resolver = Resolver(enabled: true);

        Func<Task> resolve = () => resolver.ResolveAsync(Request(), false, CancellationToken.None);

        await resolve.Should().ThrowAsync<InvalidOperationException>().WithMessage("provider defect");
    }

    [Fact]
    public void Constructor_EnabledWithoutReader_Throws()
    {
        Action create = () => new NewExecutionContextResolver(_files, _projects, _profiles, _options, [_initializer],
            _host, _bundles, enableBundleBindingConstraints: true);

        create.Should().Throw<ArgumentNullException>().WithParameterName("bundleContextReader");
    }

    private NewExecutionContextResolver Resolver(bool enabled)
        => new(_files, _projects, _profiles, _options, [_initializer], _host, _bundles,
            new TemplateEngineBundleContextReader(_files), enabled);

    private NewExecutionRequest Request() => new(_root, null, null, null, false, true, true, false, false, [], new HashSet<string>());
}
