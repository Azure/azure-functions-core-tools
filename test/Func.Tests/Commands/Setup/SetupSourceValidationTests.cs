// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupSourceValidationTests
{
    [Theory]
    [InlineData("./feed", false, false)]
    [InlineData("file:///tmp/feed", false, true)]
    [InlineData("ftp://example.test/feed", false, false)]
    [InlineData("./feed", true, true)]
    public async Task RunAsync_InvalidSource_IfNeededFailsEvenWhenEverythingIsInstalled(
        string source, bool configured, bool json)
    {
        SetupOutputMode outputMode = json ? SetupOutputMode.Json : SetupOutputMode.Plain;
        var options = new WorkloadCatalogOptions { Source = configured ? source : null };
        var provider = new PackageSourceProvider(Options.Create(options));
        int clientCalls = 0;
        WorkloadCatalog catalog = new(Options.Create(options), provider, _ =>
        {
            clientCalls++;
            throw new InvalidOperationException("Invalid sources must not reach the client factory.");
        });
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([
            Installed(HostWorkloadPackage.PackageId),
            Installed("Azure.Functions.Cli.Workloads.Workers.node"),
            Installed(SetupDependency.BuiltInStackSnapshot.StackPackageId("node")!),
            Installed(SetupDependency.BuiltInStackSnapshot.TemplatesPackageId("node")!),
            Installed(IInstalledBundleWorkloads.BundleWorkloadPackageId),
        ]);
        var installer = Substitute.For<IWorkloadInstaller>();
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var interaction = new TestInteractionService();
        var discovery = new SetupStackCatalog(catalog);
        var features = new SetupFeatureResolver(interaction, store, Substitute.For<ICliConfigurationProvider>(), discovery);
        SetupRunner runner = new(interaction, features, profiles,
            new SetupDependencyPlanBuilder(Substitute.For<IHostJsonBundleSectionReader>(), discovery),
            new SetupDependencyInstaller(interaction, store, catalog, installer));
        SetupCommandOptions request = new(new DirectoryInfo(Path.GetTempPath()), ["node"], [], configured ? null : source,
            SetupInstallPolicy.IfNeeded, false, NonInteractive: true, AssumeYes: true, Check: false, outputMode);

        SetupRunResult result = await runner.RunAsync(request, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        interaction.AllOutput.Should().Contain("not a supported NuGet feed");
        if (outputMode == SetupOutputMode.Json) interaction.AllOutput.Should().Contain("setup.failed");
        clientCalls.Should().Be(0);
        await profiles.DidNotReceiveWithAnyArgs().ResolveProfileScopesAsync(default!, default!, default);
        installer.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Discovery_ValidOverride_IgnoresInvalidConfiguredSourceAndPreservesOfflineFallback()
    {
        var options = Options.Create(new WorkloadCatalogOptions { Source = "./invalid-configured-feed" });
        var provider = new PackageSourceProvider(options);
        int clientCalls = 0;
        WorkloadCatalog catalog = new(options, provider, source =>
        {
            source.Source.Should().Be("https://offline.test/v3/index.json");
            clientCalls++;
            throw new HttpRequestException("offline");
        });

        SetupStackSnapshot result = await new SetupStackCatalog(catalog).GetStacksAsync(
            "https://offline.test/v3/index.json", false, CancellationToken.None);

        result.Should().BeSameAs(SetupDependency.BuiltInStackSnapshot);
        clientCalls.Should().Be(1);
    }

    [Fact]
    public async Task Discovery_InvalidSourceFailure_IsNotCached()
    {
        var options = new WorkloadCatalogOptions { Source = "./invalid-configured-feed" };
        var wrapped = Options.Create(options);
        WorkloadCatalog catalog = new(wrapped, new PackageSourceProvider(wrapped), _ => throw new HttpRequestException("offline"));
        var discovery = new SetupStackCatalog(catalog);

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*not a supported NuGet feed*");
        options.Source = "https://offline.test/v3/index.json";
        (await discovery.GetStacksAsync(null, false, CancellationToken.None)).Should().BeSameAs(SetupDependency.BuiltInStackSnapshot);
    }

    private static WorkloadEntry Installed(string packageId)
        => new() { PackageId = packageId, PackageVersion = "1.0.0", Kind = WorkloadKind.Workload };
}