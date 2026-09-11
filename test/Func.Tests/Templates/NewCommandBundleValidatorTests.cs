// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandBundleValidatorTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IHostJsonBundleSectionReader _hostJsonReader = Substitute.For<IHostJsonBundleSectionReader>();
    private readonly IExtensionBundleResolver _bundleResolver = Substitute.For<IExtensionBundleResolver>();
    private readonly ITemplatesWorkloadManifestReader _manifestReader = Substitute.For<ITemplatesWorkloadManifestReader>();

    public static IEnumerable<object[]> ResolutionOutcomes()
    {
        yield return [new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", "bundle", null), 0];
        yield return [new ExtensionBundleResolution.WorkloadMissing("missing"), 1];
        yield return [new ExtensionBundleResolution.EmptyIntersection("[3.0.0,4.0.0)", "[4.0.0,5.0.0)", null, "empty"), 1];
        yield return [new ExtensionBundleResolution.NoCompatibleInstall("[9.0.0,10.0.0)", ["4.35.0"], "specific install hint"), 1];
        yield return [new ExtensionBundleResolution.NotResolved("No bundle resolution context provided"), -1];
        yield return [new FutureResolution(), -1];
    }

    [Fact]
    public void ResolutionOutcomes_CoverEveryDeclaredVariant()
    {
        var resolutionType = typeof(ExtensionBundleResolution);
        var declaredVariants = resolutionType.Assembly.GetTypes()
            .Where(type => !type.IsAbstract && resolutionType.IsAssignableFrom(type));
        var testedVariants = ResolutionOutcomes().Select(row => row[0].GetType())
            .Where(type => type.Assembly == resolutionType.Assembly);

        testedVariants.Should().BeEquivalentTo(declaredVariants);
    }

    [Theory]
    [MemberData(nameof(ResolutionOutcomes))]
    public async Task ValidateAsync_ResolutionOutcome_OnlyResolvedCanSucceed(ExtensionBundleResolution resolution, int expectedExit)
    {
        var context = CreateContext("node", BundleHelpers.StableBundleId);
        using CancellationTokenSource cancellation = new();
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, cancellation.Token)
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0,5.0.0)"));
        _bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), cancellation.Token).Returns(resolution);
        var validator = CreateValidator();

        if (expectedExit == -1)
        {
            Func<Task> act = () => validator.ValidateAsync(context, cancellation.Token);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Unknown resolution variant: {resolution.GetType().Name}");
        }
        else
        {
            int result = await validator.ValidateAsync(context, cancellation.Token);
            result.Should().Be(expectedExit);
        }

        if (resolution is ExtensionBundleResolution.NoCompatibleInstall none)
        {
            _interaction.Lines.Should().Equal($"ERROR: {none.Hint}");
        }

        if (resolution is not ExtensionBundleResolution.Resolved)
        {
            _manifestReader.DidNotReceiveWithAnyArgs().GetMinBundleVersion(default!);
        }
    }

    [Theory]
    [InlineData("DoTnEt", BundleHelpers.StableBundleId, true)]
    [InlineData("node", null, true)]
    [InlineData("node", BundleHelpers.StableBundleId, false)]
    public async Task ValidateAsync_NoBundleRequiredOrSectionAbsent_SkipsResolver(string stack, string? bundleId, bool hasSection)
    {
        var context = CreateContext(stack, bundleId);
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(hasSection ? new HostJsonBundleSection(BundleHelpers.StableBundleId, "[9.0.0,10.0.0)") : null);

        int result = await CreateValidator().ValidateAsync(context, CancellationToken.None);

        result.Should().Be(0);
        await _bundleResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        _manifestReader.DidNotReceiveWithAnyArgs().GetMinBundleVersion(default!);
        _interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[4.0.0,)")]
    public async Task ValidateAsync_ResolvedBundleWithOptionalMinimum_Succeeds(string? minimum)
    {
        var context = CreateContext("python", BundleHelpers.StableBundleId);
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0,5.0.0)"));
        _bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.0.0", "bundle", null));
        _manifestReader.GetMinBundleVersion(context.Workload.InstallDirectory).Returns(minimum);

        int result = await CreateValidator().ValidateAsync(context, CancellationToken.None);

        result.Should().Be(0);
        _interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_NullContext_ThrowsArgumentNullException()
    {
        Func<Task> act = () => CreateValidator().ValidateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task ValidateAsync_DotNetStack_SkipsBundleValidation()
    {
        NewCommandBundleValidator validator = CreateValidator();
        NewCommandResolvedContext context = CreateContext("dotnet", bundleId: null);

        int result = await validator.ValidateAsync(context, CancellationToken.None);

        result.Should().Be(0);
        await _hostJsonReader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
        await _bundleResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        _manifestReader.DidNotReceiveWithAnyArgs().GetMinBundleVersion(default!);
    }

    [Fact]
    public async Task ValidateAsync_MissingBundleWorkload_RendersInstallHint()
    {
        NewCommandBundleValidator validator = CreateValidator();
        NewCommandResolvedContext context = CreateContext("node", BundleHelpers.StableBundleId);
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        _bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.WorkloadMissing("missing"));

        int result = await validator.ValidateAsync(context, CancellationToken.None);

        result.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("none is resolvable", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("func setup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_ResolvedBundleBelowMinimum_RendersVersionError()
    {
        NewCommandBundleValidator validator = CreateValidator();
        NewCommandResolvedContext context = CreateContext("python", BundleHelpers.StableBundleId);
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[3.0.0, 5.0.0)"));
        _bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "3.9.0", "bundle", null));
        _manifestReader.GetMinBundleVersion(context.Workload.InstallDirectory).Returns("[4.0.0, )");

        int result = await validator.ValidateAsync(context, CancellationToken.None);

        result.Should().Be(1);
        _interaction.Lines.Should().Contain(line =>
            line.Contains("requires extension bundle in range '[4.0.0, )'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_ResolvedBundleInRange_Succeeds()
    {
        NewCommandBundleValidator validator = CreateValidator();
        NewCommandResolvedContext context = CreateContext("python", BundleHelpers.StableBundleId);
        _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        _bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.1.0", "bundle", null));
        _manifestReader.GetMinBundleVersion(context.Workload.InstallDirectory).Returns("[4.0.0, )");

        int result = await validator.ValidateAsync(context, CancellationToken.None);

        result.Should().Be(0);
        _interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[4.0.0, )", "4.0.0", true)]
    [InlineData("[4.0.0, )", "3.9.0", false)]
    [InlineData("4.0.0", "4.1.0-preview.1", true)]
    public void VersionRangeContains_ReturnsExpectedResult(string range, string version, bool expected)
    {
        NewCommandBundleValidator.VersionRangeContains(range, version).Should().Be(expected);
    }

    private NewCommandBundleValidator CreateValidator()
    {
        return new NewCommandBundleValidator(_interaction, _hostJsonReader, _bundleResolver, _manifestReader);
    }

    // The record's protected copy constructor permits future variants outside its declaring type.
    private sealed record FutureResolution() : ExtensionBundleResolution(new ExtensionBundleResolution.NotResolved("test"));

    private static NewCommandResolvedContext CreateContext(string stack, string? bundleId)
    {
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        InstalledTemplatesWorkload workload = new(stack, "1.0.0", "install");
        return new NewCommandResolvedContext(
            workingDirectory,
            stack,
            "language",
            workload,
            bundleId,
            BundleChannel.Stable,
            UsedStableFallback: false);
    }
}