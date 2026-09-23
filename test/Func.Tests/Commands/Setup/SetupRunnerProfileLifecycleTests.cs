// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRunnerProfileLifecycleTests
{
    private const string StackId = "Azure.Functions.Cli.Workloads.DotNet";
    private const string TemplatesId = "Azure.Functions.Cli.Workloads.Templates.DotNet";

    [Fact]
    public async Task RunAsync_SecondProfilePlanningFailure_InstallsValidDependenciesBeforeStopping()
    {
        using Fixture fixture = new(secondProfileFails: true);

        SetupRunResult result = await fixture.Runner().RunAsync(fixture.Options(), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        fixture.StartedProfiles.Should().Equal("p1", "p2");
        fixture.CompletedProfiles.Should().Equal("p1:0", "p2:1");
        fixture.BundleReader.ReceivedCalls().Should().HaveCount(2);
        fixture.InstallAttempts.Should().Equal(
            $"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
            $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0", $"{HostWorkloadPackage.PackageId}@4.2.0");
        fixture.ResultsFor("p2").Should().Equal("host:installed", "extension-bundle:satisfied", "runtime:failed");
        fixture.Interaction.FailureMessages.Should().ContainSingle().Which.Should().Contain("Profile 'p2' does not support runtime 'dotnet'");
        (await fixture.InstalledAsync()).Should().Equal(
            $"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
            $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0", $"{HostWorkloadPackage.PackageId}@4.2.0");
        fixture.Interaction.Events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "setup.failed")
            .Which.GetProperty("failure_count").GetInt32().Should().Be(1);
        fixture.Interaction.EventTypes.Should().NotContain("setup.completed");
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_CheckWithSecondProfilePlanningFailure_ContinuesWithoutInstallOrMarker()
    {
        using Fixture fixture = new(secondProfileFails: true);
        await fixture.SeedDependenciesAsync("4.1.0");
        byte[] before = await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath);

        SetupRunResult result = await fixture.Runner().RunAsync(fixture.Options(check: true), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        fixture.StartedProfiles.Should().Equal("p1", "p2", "p3");
        fixture.CompletedProfiles.Should().Equal("p1:0", "p2:2", "p3:1");
        fixture.BundleReader.ReceivedCalls().Should().HaveCount(3);
        fixture.ResultsFor("p2").Should().Equal("host:failed", "extension-bundle:satisfied", "runtime:failed");
        fixture.ResultsFor("p3").Should().Equal("host:failed", "stack:satisfied", "templates:satisfied", "extension-bundle:satisfied");
        fixture.Interaction.Events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "setup.failed")
            .Which.GetProperty("failure_count").GetInt32().Should().Be(3);
        fixture.Interaction.EventTypes.Should().NotContain("setup.completed");
        fixture.PackageInstaller.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath)).Should().Equal(before);
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SecondProfileDependencyFailure_PreservesEarlierInstallsAndStops()
    {
        using Fixture fixture = new();
        fixture.FailingHostVersion = "4.2.0";

        SetupRunResult result = await fixture.Runner().RunAsync(fixture.Options(), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        fixture.StartedProfiles.Should().Equal("p1", "p2");
        fixture.CompletedProfiles.Should().Equal("p1:0", "p2:1");
        fixture.BundleReader.ReceivedCalls().Should().HaveCount(2);
        fixture.InstallAttempts.Should().Equal(
            $"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
            $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0", $"{HostWorkloadPackage.PackageId}@4.2.0");
        fixture.ResultsFor("p2").Should().Equal("host:failed");
        fixture.Interaction.FailureMessages.Should().Equal("Host installation failed for 4.2.0.");
        (await fixture.InstalledAsync()).Should().Equal(
            $"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
            $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0");
        fixture.Interaction.EventTypes.Should().ContainSingle(type => type == "setup.failed").And.NotContain("setup.completed");
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunAsync_CanceledBetweenProfiles_DoesNotStartNextProfileOrEmitTerminalEvent(bool check, bool firstProfileFails)
    {
        using CancellationTokenSource cancellation = new();
        using Fixture fixture = new(firstProfileFails: firstProfileFails);
        if (check) await fixture.SeedDependenciesAsync("4.1.0", "4.2.0", "4.3.0");
        string[] before = await fixture.InstalledAsync();
        fixture.Interaction.AfterEvent = item =>
        {
            if (item.GetProperty("type").GetString() == "profile.completed" && item.GetProperty("profile").GetString() == "p1")
                cancellation.Cancel();
        };

        var failure = await FluentActions.Awaiting(() => fixture.Runner().RunAsync(fixture.Options(check), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        cancellation.IsCancellationRequested.Should().BeTrue();
        fixture.StartedProfiles.Should().Equal("p1");
        fixture.CompletedProfiles.Should().Equal(firstProfileFails ? "p1:1" : "p1:0");
        fixture.BundleReader.ReceivedCalls().Should().ContainSingle();
        fixture.Interaction.EventTypes.Should().NotContain("setup.failed").And.NotContain("setup.completed");
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
        if (check)
        {
            fixture.PackageInstaller.ReceivedCalls().Should().BeEmpty();
            (await fixture.InstalledAsync()).Should().Equal(before);
        }
        else
        {
            string[] expected = firstProfileFails
                ? [$"{HostWorkloadPackage.PackageId}@4.1.0", $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0"]
                : [$"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
                    $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0"];
            fixture.InstallAttempts.Should().Equal(expected);
            (await fixture.InstalledAsync()).Should().Equal(expected);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_AllProfilesSucceed_MarksOnlyAnInstallingRunOnce(bool check)
    {
        using Fixture fixture = new();
        if (check) await fixture.SeedDependenciesAsync("4.1.0", "4.2.0", "4.3.0");
        byte[]? before = check ? await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath) : null;

        SetupRunResult result = await fixture.Runner().RunAsync(fixture.Options(check), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.StartedProfiles.Should().Equal("p1", "p2", "p3");
        fixture.CompletedProfiles.Should().Equal("p1:0", "p2:0", "p3:0");
        fixture.BundleReader.ReceivedCalls().Should().HaveCount(3);
        fixture.ResultsFor("p2").Should().Equal(check ? "host:satisfied" : "host:installed",
            "stack:satisfied", "templates:satisfied", "extension-bundle:satisfied");
        fixture.ResultsFor("p3").Should().Equal(check ? "host:satisfied" : "host:installed",
            "stack:satisfied", "templates:satisfied", "extension-bundle:satisfied");
        fixture.Interaction.EventTypes.Should().ContainSingle(type => type == "setup.completed").And.NotContain("setup.failed");
        string[] expected =
        [
            $"{HostWorkloadPackage.PackageId}@4.1.0", $"{HostWorkloadPackage.PackageId}@4.2.0", $"{HostWorkloadPackage.PackageId}@4.3.0",
            $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0", $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0",
        ];
        (await fixture.InstalledAsync()).Should().BeEquivalentTo(expected);
        if (check)
        {
            fixture.PackageInstaller.ReceivedCalls().Should().BeEmpty();
            (await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath)).Should().Equal(before!);
            fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
            fixture.MarkerEvents.Should().BeEmpty();
        }
        else
        {
            fixture.InstallAttempts.Should().Equal(
                $"{HostWorkloadPackage.PackageId}@4.1.0", $"{StackId}@1.0.0", $"{TemplatesId}@1.0.0",
                $"{IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.0.0",
                $"{HostWorkloadPackage.PackageId}@4.2.0", $"{HostWorkloadPackage.PackageId}@4.3.0");
            await fixture.FirstRun.Received(1).MarkCompleteAsync(CancellationToken.None);
            fixture.MarkerEvents.Should().Equal("setup.completed");
        }
    }

    [Fact]
    public async Task RunAsync_LaterProfileResolutionThrows_DoesNotProcessAnyProfile()
    {
        using Fixture fixture = new();
        await fixture.SeedDependenciesAsync("4.1.0", "4.2.0", "4.3.0");
        byte[] before = await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath);
        var catalog = Substitute.For<IProfileCatalog>();
        catalog.LoadAsync(Arg.Any<ProfileSourceContext>(), Arg.Any<CancellationToken>()).Returns([]);
        List<string> resolvedNames = [];
        catalog.ResolveProfile(Arg.Any<string>(), Arg.Any<IReadOnlyList<ProfileSourceSnapshot>>()).Returns(call =>
        {
            string name = call.Arg<string>();
            resolvedNames.Add(name);
            if (name == "p2") throw new ProfileConfigurationException("Invalid second profile.");
            return fixture.Scopes.Single(scope => scope.Name == name).Profile!;
        });
        var projectOptions = Substitute.For<IOptionsMonitor<ProjectProfileOptions>>();
        projectOptions.Get(Arg.Any<string>()).Returns(new ProjectProfileOptions());
        var userOptions = Substitute.For<IOptionsMonitor<UserProfilePreferenceOptions>>();
        SetupProfileScopeResolver resolver = new(catalog, projectOptions, userOptions);

        SetupRunResult result = await fixture.Runner(resolver).RunAsync(fixture.Options(), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        resolvedNames.Should().Equal("p1", "p2");
        fixture.Interaction.EventTypes.Should().Equal("setup.failed");
        fixture.Interaction.Events.Single().GetProperty("message").GetString().Should().Be("Invalid second profile.");
        fixture.BundleReader.ReceivedCalls().Should().BeEmpty();
        fixture.Catalog.ReceivedCalls().Should().BeEmpty();
        fixture.PackageInstaller.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath)).Should().Equal(before);
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_FeatureResolutionThrows_DoesNotResolveOrProcessProfiles()
    {
        using Fixture fixture = new();

        SetupRunResult result = await fixture.Runner().RunAsync(fixture.Options(features: [".net"]), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        fixture.Interaction.EventTypes.Should().Equal("setup.failed");
        fixture.Interaction.Events.Single().GetProperty("message").GetString().Should().Contain("Use 'dotnet'");
        fixture.Profiles.ReceivedCalls().Should().BeEmpty();
        fixture.BundleReader.ReceivedCalls().Should().BeEmpty();
        fixture.Catalog.ReceivedCalls().Should().BeEmpty();
        fixture.PackageInstaller.ReceivedCalls().Should().BeEmpty();
        (await fixture.InstalledAsync()).Should().BeEmpty();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class Fixture : IDisposable
    {
        public SetupRecordingInteraction Interaction { get; } = new();
        public WorkloadPathsOptions Paths { get; } = new(Path.Combine(Path.GetTempPath(), $"func-setup-lifecycle-{Guid.NewGuid():N}"));
        public WorkloadStore Store { get; }
        public IWorkloadCatalog Catalog { get; } = Substitute.For<IWorkloadCatalog>();
        public IWorkloadInstaller PackageInstaller { get; } = Substitute.For<IWorkloadInstaller>();
        public ISetupProfileScopeResolver Profiles { get; } = Substitute.For<ISetupProfileScopeResolver>();
        public IHostJsonBundleSectionReader BundleReader { get; } = Substitute.For<IHostJsonBundleSectionReader>();
        public IFirstRunStateStore FirstRun { get; } = Substitute.For<IFirstRunStateStore>();
        public IReadOnlyList<SetupProfileScope> Scopes { get; }
        public List<string> InstallAttempts { get; } = [];
        public List<string?> MarkerEvents { get; } = [];
        public string? FailingHostVersion { get; set; }

        public IEnumerable<string?> StartedProfiles => Interaction.Events
            .Where(item => item.GetProperty("type").GetString() == "profile.started")
            .Select(item => item.GetProperty("profile").GetString());

        public IEnumerable<string> CompletedProfiles => Interaction.Events
            .Where(item => item.GetProperty("type").GetString() == "profile.completed")
            .Select(item => $"{item.GetProperty("profile").GetString()}:{item.GetProperty("failure_count").GetInt32()}");

        public Fixture(bool secondProfileFails = false, bool firstProfileFails = false)
        {
            Store = new WorkloadStore(Paths);
            Scopes = [Profile("p1", "4.1.0", !firstProfileFails), Profile("p2", "4.2.0", !secondProfileFails), Profile("p3", "4.3.0", true)];
            Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
                .Returns(Scopes);
            BundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
            FirstRun.MarkCompleteAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                MarkerEvents.Add(Interaction.EventTypes.LastOrDefault());
                return Task.CompletedTask;
            });
            Catalog.ResolveLatestVersionInRangeAsync(Arg.Any<string>(), Arg.Any<VersionRange>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    string[] versions = ["4.1.0", "4.2.0", "4.3.0"];
                    NuGetVersion version = versions.Select(NuGetVersion.Parse).Single(candidate => call.Arg<VersionRange>().Satisfies(candidate));
                    return Task.FromResult<ResolvedPackage?>(Resolved(call.ArgAt<string>(0), version));
                });
            Catalog.ResolveLatestVersionAsync(Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<NuGetVersion?>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    string id = call.ArgAt<string>(0);
                    return Task.FromResult<ResolvedPackage?>(id is StackId or TemplatesId ? Resolved(id, NuGetVersion.Parse("1.0.0")) : null);
                });
            Catalog.ResolveLatestVersionOnChannelAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<VersionRange?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    string id = call.ArgAt<string>(0);
                    return Task.FromResult<ResolvedPackage?>(id == IInstalledBundleWorkloads.BundleWorkloadPackageId
                        ? Resolved(id, NuGetVersion.Parse("4.0.0")) : null);
                });
            PackageInstaller.InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    string id = call.ArgAt<string>(0);
                    string version = call.ArgAt<NuGetVersion>(1).ToNormalizedString();
                    InstallAttempts.Add($"{id}@{version}");
                    if (id == HostWorkloadPackage.PackageId && version == FailingHostVersion)
                        throw new InvalidOperationException($"Host installation failed for {version}.");

                    WorkloadEntry entry = Entry(id, version);
                    await Store.SaveWorkloadAsync(entry, call.Arg<CancellationToken>());
                    return new WorkloadInstallResult(entry, AlreadyInstalled: false);
                });
        }

        public SetupRunner Runner(ISetupProfileScopeResolver? profiles = null)
            => new(Interaction, new SetupFeatureResolver(Interaction, Store, Substitute.For<ICliConfigurationProvider>()),
                profiles ?? Profiles, new SetupDependencyPlanBuilder(BundleReader),
                new SetupDependencyInstaller(Interaction, Store, Catalog, PackageInstaller), FirstRun);

        public SetupCommandOptions Options(bool check = false, IReadOnlyList<string>? features = null)
            => new(new DirectoryInfo(Paths.Home), features ?? ["dotnet", "runtime"], ["p1", "p2", "p3"], null,
                SetupInstallPolicy.IfNeeded, false, true, true, check, SetupOutputMode.Json);

        public IEnumerable<string> ResultsFor(string profile)
            => Interaction.Events.Where(item => item.GetProperty("type").GetString() == "dependency.result"
                    && item.GetProperty("profile").GetString() == profile)
                .Select(item => $"{item.GetProperty("dependency_type").GetString()}:{item.GetProperty("status").GetString()}");

        public async Task<string[]> InstalledAsync()
            => [.. (await Store.GetWorkloadsAsync(CancellationToken.None)).Select(entry => $"{entry.PackageId}@{entry.PackageVersion}")];

        public async Task SeedDependenciesAsync(params string[] hostVersions)
        {
            foreach (string version in hostVersions)
                await Store.SaveWorkloadAsync(Entry(HostWorkloadPackage.PackageId, version), CancellationToken.None);
            await Store.SaveWorkloadAsync(Entry(StackId, "1.0.0"), CancellationToken.None);
            await Store.SaveWorkloadAsync(Entry(TemplatesId, "1.0.0"), CancellationToken.None);
            await Store.SaveWorkloadAsync(Entry(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.0.0"), CancellationToken.None);
        }

        public void Dispose()
        {
            if (Directory.Exists(Paths.Home)) Directory.Delete(Paths.Home, recursive: true);
        }

        private static SetupProfileScope Profile(string name, string hostVersion, bool supportsDotnet)
            => new(new ResolvedProfile(name, new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), null,
                ProfileStatus.Stable, null, VersionRange.Parse($"[{hostVersion}]"), new Dictionary<string, VersionRange>(),
                VersionRange.Parse("[4.0.0, 5.0.0)"), supportsDotnet ? ["dotnet-isolated"] : ["python"], null));

        private static ResolvedPackage Resolved(string id, NuGetVersion version)
            => new(id, version, new PackageSource("https://example.test/v3/index.json"));

        private static WorkloadEntry Entry(string id, string version)
            => new() { PackageId = id, PackageVersion = version, Kind = id == StackId ? WorkloadKind.Workload : WorkloadKind.Content };
    }
}