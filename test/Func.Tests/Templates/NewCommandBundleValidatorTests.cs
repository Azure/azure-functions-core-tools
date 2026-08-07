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