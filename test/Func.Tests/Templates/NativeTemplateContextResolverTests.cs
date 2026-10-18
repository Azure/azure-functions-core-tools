// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateContextResolverTests
{
    private readonly IFileSystem _files = Substitute.For<IFileSystem>();
    private readonly IFunctionsProjectResolver _projects = Substitute.For<IFunctionsProjectResolver>();
    private readonly IOptionsMonitor<StackOptions> _options = Substitute.For<IOptionsMonitor<StackOptions>>();
    private readonly IHostJsonBundleSectionReader _host = Substitute.For<IHostJsonBundleSectionReader>();
    private readonly IInstalledBundleWorkloads _installed = Substitute.For<IInstalledBundleWorkloads>();
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfiles = Substitute.For<IOptionsMonitor<ProjectProfileOptions>>();
    private readonly IOptionsMonitor<UserProfilePreferenceOptions> _userProfiles = Substitute.For<IOptionsMonitor<UserProfilePreferenceOptions>>();
    private readonly IProfileCatalog _profiles = Substitute.For<IProfileCatalog>();
    private readonly WorkingDirectory _root = new(new DirectoryInfo(Path.GetFullPath("native-context-project")), true);
    private readonly InstalledProjectStackCatalog _stacks;
    private readonly string _selected = Path.GetFullPath("profile-selected-bundle");

    public NativeTemplateContextResolverTests()
    {
        _files.DirectoryExists(_root.Info.FullName).Returns(true);
        _files.FileExists(Path.Combine(_root.Info.FullName, "host.json")).Returns(true);
        var project = Substitute.For<FunctionsProject>();
        project.WorkingDirectory.Returns(_root);
        project.StackName.Returns("node-alias");
        project.Language.Returns("js");
        _projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectResolutionResult.Resolved(project, "project at containing root"));
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("node");
        stack.DisplayName.Returns("Node");
        stack.WorkerRuntimeAliases.Returns(["node-alias"]);
        stack.SupportedLanguages.Returns(["JavaScript", "TypeScript"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["JavaScript"] = ["js"],
            ["TypeScript"] = ["ts"],
        });
        _stacks = new([stack]);
        _options.Get(_root.Info.FullName).Returns(new StackOptions { Language = "ts" });
        _host.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0,5.0)"));
        _installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns(
            [new("4.20.0", _selected), new("4.35.0", Path.GetFullPath("latest-not-selected")), new("4.99.0-preview", Path.GetFullPath("other-channel"))]);
        _projectProfiles.Get(_root.Info.FullName).Returns(new ProjectProfileOptions { DefaultProfile = "project-default" });
        _userProfiles.CurrentValue.Returns(new UserProfilePreferenceOptions { DefaultProfile = "user-default" });
        _profiles.LoadAsync(Arg.Any<ProfileSourceContext>(), Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ResolveProfile("project-default", Arg.Any<IReadOnlyList<ProfileSourceSnapshot>>()).Returns(new ResolvedProfile(
            "project-default", new(ProfileSourceKind.Project, "project"), null, ProfileStatus.Stable, null,
            VersionRange.All, new Dictionary<string, VersionRange>(), VersionRange.Parse("[4.0,4.30)"), null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ActualProfileAndBundleResolvers_UseSingleCatalogAndReadSelectedSourceOnly(bool enabled)
    {
        using CancellationTokenSource cancellation = new();
        var path = Path.Combine(_selected, "tools", "any");
        var metadata = Path.Combine(path, "4.20.0", "bin", "extensions.json");
        _files.ReadAllTextIfExistsAsync(metadata, cancellation.Token).Returns("""{"extensions":[{"bindings":["blob"]}]}""");
        var nested = new WorkingDirectory(new DirectoryInfo(Path.Combine(_root.Info.FullName, "functions", "nested")), true);
        var resolver = Create(enabled);

        var result = await resolver.ResolveAsync(Request(nested), false, cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.CommandDirectory.Should().BeSameAs(nested);
        result.Context.Project!.RootDirectory.Should().BeSameAs(_root);
        result.Context.Project.Stack.Should().Be("node");
        result.Context.Project.Language.Should().Be("TypeScript");
        result.Context.Bundle!.Id.Should().Be(BundleHelpers.StableBundleId);
        result.Context.Bundle.Version.Should().Be("4.20.0");
        result.Context.Bundle.Path.Should().Be(path);
        result.Context.EnableBundleBindingConstraints.Should().Be(enabled);
        _profiles.Received(1).ResolveProfile("project-default", Arg.Any<IReadOnlyList<ProfileSourceSnapshot>>());
        _profiles.DidNotReceive().ResolveProfile("user-default", Arg.Any<IReadOnlyList<ProfileSourceSnapshot>>());
        await _installed.Received(1).ListInstalledAsync(cancellation.Token);
        if (enabled)
        {
            result.Context.Bundle.Bindings!.Contains("blob").Should().BeTrue();
            await _files.Received(1).ReadAllTextIfExistsAsync(metadata, cancellation.Token);
        }
        else
        {
            result.Context.Bundle.Bindings.Should().BeNull();
            await _files.DidNotReceive().ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task ResolveAsync_ExplicitLanguageAlias_OverridesConfigUsingCatalogMetadataWithoutInitializer()
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync(Request() with { ExplicitLanguage = "js" }, false, CancellationToken.None);

        result.Context!.Project!.Language.Should().Be("JavaScript");
    }

    [Fact]
    public async Task ResolveAsync_NoBundle_LeavesContextAbsentWithoutScanningInventoryOrMetadata()
    {
        _host.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        var resolver = Create(enabled: true);

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Context!.Bundle.Should().BeNull();
        await _installed.DidNotReceive().ListInstalledAsync(Arg.Any<CancellationToken>());
        await _files.DidNotReceive().ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_UnknownCatalogStack_RejectsBeforeBundleResolution()
    {
        var resolver = Create(catalog: new([]));

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Failure!.Kind.Should().Be("languageMetadataUnavailable");
        result.Context.Should().BeNull();
        await _installed.DidNotReceive().ListInstalledAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_UnresolvableProfileIntersection_NeverReadsAnotherBundle()
    {
        _host.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.35,5.0)"));
        var resolver = Create(enabled: true);

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Failure!.Kind.Should().Be("bundleUnavailable");
        result.Context.Should().BeNull();
        await _files.DidNotReceive().ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MalformedOptions_RetainsExistingFailureCategory()
    {
        _options.Get(_root.Info.FullName).Throws(new InvalidDataException("invalid stack options"));
        var resolver = Create();

        var result = await resolver.ResolveAsync(Request(), false, CancellationToken.None);

        result.Failure!.Kind.Should().Be("invalidConfiguration");
        result.Failure.Diagnostics.Should().ContainSingle().Which.Should().Contain("invalid stack options");
    }

    [Fact]
    public async Task ResolveAsync_UnexpectedProjectProviderFault_Propagates()
    {
        _projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider bug"));
        var resolver = Create();

        Func<Task> resolve = () => resolver.ResolveAsync(Request(), false, CancellationToken.None);

        await resolve.Should().ThrowAsync<InvalidOperationException>().WithMessage("provider bug");
    }

    [Fact]
    public async Task ResolveAsync_Canceled_DoesNotAccessProjectOrBundleProviders()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        var resolver = Create(enabled: true);

        Func<Task> resolve = () => resolver.ResolveAsync(Request(), false, cancellation.Token);

        await resolve.Should().ThrowAsync<OperationCanceledException>();
        _projects.ReceivedCalls().Should().BeEmpty();
        _installed.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullCatalogOrReader_Throws()
    {
        var profiles = Substitute.For<IProfileResolver>();
        var bundles = Substitute.For<IExtensionBundleResolver>();
        Action noCatalog = () => new NativeTemplateContextResolver(_files, _projects, profiles, _options, null!, _host, bundles, new(_files));
        Action noReader = () => new NativeTemplateContextResolver(_files, _projects, profiles, _options, _stacks, _host, bundles, null!);

        noCatalog.Should().Throw<ArgumentNullException>().WithParameterName("stackCatalog");
        noReader.Should().Throw<ArgumentNullException>().WithParameterName("bundleContextReader");
    }

    private NativeTemplateContextResolver Create(bool enabled = false, InstalledProjectStackCatalog? catalog = null)
        => new(_files, _projects, new ProfileResolver(_profiles, _projectProfiles, _userProfiles, Substitute.For<IInteractionService>()),
            _options, catalog ?? _stacks, _host,
            new ExtensionBundleResolver(new(_installed), NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance),
            new(_files), enabled);

    private NewExecutionRequest Request(WorkingDirectory? directory = null)
        => new(directory ?? _root, null, null, null, false, true, true, false, false, [], new HashSet<string>());
}
