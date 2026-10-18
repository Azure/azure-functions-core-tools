// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupFeatureValidationTests
{
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();

    [Theory]
    [InlineData("node")]
    [InlineData("python")]
    [InlineData("go")]
    [InlineData("dotnet")]
    [InlineData("java")]
    [InlineData("powershell")]
    [InlineData("custom")]
    public async Task ValidateFeaturesAsync_CuratedRuntime_DoesNotRequireInstalledStackOrCatalog(string runtime)
    {
        await Resolver().ValidateFeaturesAsync(Options(), Plan(runtime), [SetupProfileScope.Unconstrained], CancellationToken.None);

        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("custom-provider")]
    [InlineData("custom-worker")]
    public async Task ValidateFeaturesAsync_RegisteredProvider_RecognizesStackAndWorkerAlias(string runtime)
    {
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("custom-provider");
        stack.WorkerRuntimeAliases.Returns(["custom-worker"]);

        await Resolver([stack]).ValidateFeaturesAsync(Options(), Plan(runtime), [SetupProfileScope.Unconstrained], CancellationToken.None);

        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateFeaturesAsync_SelectedProfileCustomRuntime_DoesNotRequirePublishedWorker(bool workerConstraint)
    {
        Dictionary<string, VersionRange> workers = workerConstraint
            ? new(StringComparer.OrdinalIgnoreCase) { ["profile-runtime"] = VersionRange.Parse("[1.0.0,2.0.0)") }
            : new(StringComparer.OrdinalIgnoreCase);
        ResolvedProfile profile = new("private-profile", new ProfileSourceInfo(ProfileSourceKind.Project, "project"),
            null, ProfileStatus.Stable, null, VersionRange.All, workers, null, workerConstraint ? null : ["profile-runtime"], null);

        await Resolver().ValidateFeaturesAsync(Options(), Plan("profile-runtime"), [new(profile)], CancellationToken.None);

        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateFeaturesAsync_FreshPublishedCustomWorker_ForwardsSourceAndPrerelease()
    {
        const string source = "https://private.test/v3/index.json";
        const string packageId = "Azure.Functions.Cli.Workloads.Workers.source-runtime";
        _catalog.ResolveLatestVersionAsync(packageId, true, null, true, source, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(packageId, NuGetVersion.Parse("1.0.0-preview.1"), new PackageSource(source)));

        await Resolver().ValidateFeaturesAsync(Options() with { Source = source }, Plan("source-runtime"),
            [SetupProfileScope.Unconstrained], CancellationToken.None);

        await _catalog.Received(1).ResolveLatestVersionAsync(packageId, true, null, true, source, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateFeaturesAsync_CompletelyUnknownRuntime_RejectsInsteadOfTreatingMissingWorkerAsOptional()
    {
        Func<Task> validate = () => Resolver().ValidateFeaturesAsync(
            Options(), Plan("n0de"), [SetupProfileScope.Unconstrained], CancellationToken.None);

        var error = (await validate.Should().ThrowAsync<SetupConfigurationException>()).Which;

        error.Message.Should().Contain("Unknown setup feature 'n0de'");
    }

    [Fact]
    public async Task ValidateFeaturesAsync_CatalogUnavailable_PreservesFailureCause()
    {
        IOException failure = new("Private catalog is unavailable.");
        _catalog.ResolveLatestVersionAsync(Arg.Any<string>(), Arg.Any<bool?>(), null, true, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResolvedPackage?>(failure));

        Func<Task> validate = () => Resolver().ValidateFeaturesAsync(
            Options(), Plan("source-runtime"), [SetupProfileScope.Unconstrained], CancellationToken.None);
        var error = (await validate.Should().ThrowAsync<SetupConfigurationException>()).Which;

        error.Message.Should().Contain("Cannot validate").And.Contain(failure.Message);
        error.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ValidateFeaturesAsync_Cancellation_DoesNotConsultCatalog()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> validate = () => Resolver().ValidateFeaturesAsync(
            Options(), Plan("source-runtime"), [SetupProfileScope.Unconstrained], cancellation.Token);

        (await validate.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    private SetupFeatureResolver Resolver(IEnumerable<IProjectStack>? stacks = null)
        => new(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), _catalog, stacks ?? []);

    private static SetupFeaturePlan Plan(string runtime) => new([runtime], [new(runtime, runtime, true)], [runtime], true);

    private static SetupCommandOptions Options() => new(new DirectoryInfo("setup-feature-validation"), [], [], null,
        SetupInstallPolicy.IfNeeded, true, true, true, false, SetupOutputMode.Json);
}
