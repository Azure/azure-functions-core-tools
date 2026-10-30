// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRunnerTests : IDisposable
{
    private static readonly string _hostPackageId = HostWorkloadPackage.PackageId;
    private static readonly string _dotNetStackPackageId = "Azure.Functions.Cli.Workloads.DotNet";

    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IProfileCatalog _profileCatalog = Substitute.For<IProfileCatalog>();
    private readonly IHostJsonBundleSectionReader _bundleReader = Substitute.For<IHostJsonBundleSectionReader>();

    public SetupRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-setup-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns((HostJsonBundleSection?)null);
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string packageId = (string)callInfo[0]!;
                var version = (NuGetVersion)callInfo[1]!;
                return new WorkloadInstallResult(Entry(packageId, version.ToNormalizedString()), AlreadyInstalled: false);
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DefaultsToRuntime_InstallsHostAndBundle()
    {
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.10.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task RunAsync_ConfiguredDotnetRuntime_InstallsHostAndStackAndSkipsWorkerAndBundle(string configuredRuntime)
    {
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(_dotNetStackPackageId, "1.0.0");
        SetupRunner runner = CreateRunner(catalog, projectConfig: new Dictionary<string, string?>
        {
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = configuredRuntime,
        });

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(0);

        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _dotNetStackPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.0.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains(".Workers.", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains("ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ConfiguredNativeRuntime_UsesGoProfileWorkerConstraint(bool check)
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(WorkerPackage("go"), "2.0.0", kind: WorkloadKind.Content)]);
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest("Azure.Functions.Cli.Workloads.Go", "1.0.0")
            .WithLatest(WorkerPackage("go"), "1.5.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = CreateRunner(
            catalog,
            projectConfig: new Dictionary<string, string?>
            {
                [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = "native",
            },
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("go-profile", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["go"],
                        Workers = new() { ["go"] = new ProfileWorkerConstraint { Version = "[1.0.0, 2.0.0)" } },
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(Options(profiles: ["go-profile"], check: check), CancellationToken.None);

        result.ExitCode.Should().Be(check ? 1 : 0);
        catalog.RangeResolutions.Where(resolution => resolution.PackageId == WorkerPackage("go")).Should().ContainSingle()
            .Which.Should().Be((WorkerPackage("go"), VersionRange.Parse("[1.0.0, 2.0.0)")));
        string expectedMessage = check ? "go worker 1.5.0 is not installed." : "Installed go worker 1.5.0.";
        _interaction.Lines.Should().Contain(line => line.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("native", false)]
    [InlineData(" NaTiVe ", false)]
    [InlineData("go", false)]
    [InlineData("native", true)]
    public async Task RunAsync_GoProject_InstallsGoStackAndWorker(string configuredRuntime, bool explicitGoFeature)
    {
        const string goStack = "Azure.Functions.Cli.Workloads.Go";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(goStack, "1.0.0")
            .WithLatest(WorkerPackage("go"), "1.0.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        var projectConfig = new Dictionary<string, string?>
        {
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = configuredRuntime,
        };
        SetupRunner runner = CreateRunner(catalog, projectConfig);

        SetupRunResult result = await runner.RunAsync(
            Options(features: explicitGoFeature ? ["go"] : [], outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        _installer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorkloadInstaller.InstallFromCatalogAsync))
            .Select(call => (string)call.GetArguments()[0]!)
            .Should().BeEquivalentTo(
                _hostPackageId.ToLowerInvariant(),
                goStack.ToLowerInvariant(),
                WorkerPackage("go").ToLowerInvariant(),
                IInstalledBundleWorkloads.BundleWorkloadPackageId.ToLowerInvariant());
        using var started = JsonDocument.Parse(_interaction.Lines[0]);
        started.RootElement.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).Should().Equal("go");
        started.RootElement.GetProperty("worker_runtimes").EnumerateArray().Select(runtime => runtime.GetString()).Should().Equal("go");
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task RunAsync_ConfiguredNativeRuntime_CheckRequiresGoStackAndWorker(
        bool stackInstalled,
        bool workerInstalled,
        bool json)
    {
        const string goStack = "Azure.Functions.Cli.Workloads.Go";
        List<WorkloadEntry> installed =
        [
            Entry(_hostPackageId, "4.1.0", kind: WorkloadKind.Content),
            Entry(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0", kind: WorkloadKind.Content),
        ];
        if (stackInstalled)
        {
            installed.Add(Entry(goStack, "1.0.0"));
        }

        if (workerInstalled)
        {
            installed.Add(Entry(WorkerPackage("go"), "1.0.0", kind: WorkloadKind.Content));
        }

        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(installed);
        SetupRunner runner = CreateRunner(
            Catalog()
                .WithLatest(_hostPackageId, "4.1.0")
                .WithLatest(goStack, "1.0.0")
                .WithLatest(WorkerPackage("go"), "1.0.0")
                .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0"),
            projectConfig: new Dictionary<string, string?>
            {
                [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = "native",
            });

        SetupRunResult result = await runner.RunAsync(
            Options(check: true, outputMode: json ? SetupOutputMode.Json : SetupOutputMode.Plain),
            CancellationToken.None);

        result.ExitCode.Should().Be(stackInstalled && workerInstalled ? 0 : 1);
        if (!stackInstalled)
        {
            _interaction.Lines.Should().Contain(line => line.Contains("go stack 1.0.0 is not installed.", StringComparison.Ordinal));
        }

        if (!workerInstalled)
        {
            _interaction.Lines.Should().Contain(line => line.Contains("go worker 1.0.0 is not installed.", StringComparison.Ordinal));
        }

        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ConfiguredNativeRuntime_UnsupportedProfileReportsGo(bool check)
    {
        SetupRunner runner = CreateRunner(
            Catalog()
                .WithLatest(_hostPackageId, "4.1.0")
                .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0"),
            projectConfig: new Dictionary<string, string?>
            {
                [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = "native",
            },
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("no-go", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["python"],
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(Options(profiles: ["no-go"], check: check), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("does not support runtime 'go'", StringComparison.Ordinal));
        _interaction.Lines.Should().NotContain(line => line.Contains("native", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_DotnetFeature_InstallsDotnetStackAndSkipsWorkerAndBundle()
    {
        const string source = "http://localhost:5555/v3/index.json";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(_dotNetStackPackageId, "1.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(
            Options(features: ["dotnet"], source: source, includePrerelease: true),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _dotNetStackPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.0.0"),
            Arg.Is<string?>(actual => actual == source),
            Arg.Is(true),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains(".Workers.", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains("ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DotnetFeature_AllowsProfileWithDotnetIsolatedRuntime()
    {
        SetupRunner runner = CreateRunner(
            Catalog()
                .WithLatest(_hostPackageId, "4.1.0")
                .WithLatest(_dotNetStackPackageId, "1.0.0"),
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("flex", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["dotnet-isolated"],
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(Options(features: ["dotnet"], profiles: ["flex"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        _interaction.Lines.Should().NotContain(line => line.Contains("does not support runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task RunAsync_JsonOutput_DotnetRuntimeUsesDotnetFeatureName(string requestedFeature)
    {
        SetupRunner runner = CreateRunner(
            Catalog()
                .WithLatest(_hostPackageId, "4.1.0")
                .WithLatest(_dotNetStackPackageId, "1.0.0"));

        SetupRunResult result = await runner.RunAsync(
            Options(features: [requestedFeature], outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        using var started = JsonDocument.Parse(_interaction.Lines[0]);
        JsonElement root = started.RootElement;
        string[] features = [.. root.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()!)];
        string[] workerRuntimes = [.. root.GetProperty("worker_runtimes").EnumerateArray().Select(runtime => runtime.GetString()!)];

        features.Should().Equal(["dotnet"]);
        workerRuntimes.Should().BeEmpty();
        _interaction.Lines.Should().NotContain(line => line.Contains("dotnet-isolated", StringComparison.OrdinalIgnoreCase));
        string stackLine = _interaction.Lines.Should().ContainSingle(line => line.Contains("\"type\":\"dependency.detected\"", StringComparison.OrdinalIgnoreCase)
                && line.Contains("\"dependency_type\":\"stack\"", StringComparison.OrdinalIgnoreCase)).Which;
        using var stackEvent = JsonDocument.Parse(stackLine);
        JsonElement stackRoot = stackEvent.RootElement;

        stackRoot.GetProperty("name").GetString().Should().Be("dotnet");
        stackRoot.GetProperty("package_id").GetString().Should().BeEquivalentTo(_dotNetStackPackageId);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task RunAsync_DotnetRuntimeUnsupportedProfile_ReportsDotnetFeatureName(string requestedFeature)
    {
        SetupRunner runner = CreateRunner(
            Catalog().WithLatest(_hostPackageId, "4.1.0"),
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("flex", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["python"],
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(Options(features: [requestedFeature], profiles: ["flex"]), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("does not support runtime 'dotnet'", StringComparison.OrdinalIgnoreCase));
        _interaction.Lines.Should().NotContain(line => line.Contains("dotnet-isolated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_JsonOutput_DotnetUnsupportedProfileReportsRuntimeDiagnostic()
    {
        SetupRunner runner = CreateRunner(
            Catalog().WithLatest(_hostPackageId, "4.1.0"),
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("flex", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["python"],
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(
            Options(features: ["dotnet"], profiles: ["flex"], outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        result.ExitCode.Should().Be(1);
        string failureLine = _interaction.Lines.Should().ContainSingle(line => line.Contains("\"status\":\"failed\"", StringComparison.OrdinalIgnoreCase)).Which;
        using var failure = JsonDocument.Parse(failureLine);
        JsonElement root = failure.RootElement;

        root.GetProperty("dependency_type").GetString().Should().Be("runtime");
        root.GetProperty("name").GetString().Should().Be("dotnet");
        root.TryGetProperty("package_id", out _).Should().BeFalse();
        failureLine.Should().NotContainEquivalentOf("dotnet-isolated");
    }

    [Fact]
    public async Task RunAsync_Source_PassesSourceToCatalogAndInstaller()
    {
        const string source = "https://example.test/custom/index.json";
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], source: source), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        catalog.SearchSources.Should().BeEmpty();
        catalog.ResolveSources.Should().NotBeEmpty();
        catalog.ResolveSources.Should().AllSatisfy(actual => actual.Should().Be(source));
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Is<string?>(actual => actual == source),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HostFeature_UsesRidPackageIdWithoutAliasSearch()
    {
        FakeCatalog catalog = Catalog()
            .WithAlias("host", "rogue.host.package", "9.9.9")
            .WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        catalog.SearchSources.Should().BeEmpty();
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_IfNeeded_SkipsCatalogWhenCompatibleVersionInstalled()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content)]);
        FakeCatalog catalog = Catalog();
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], installPolicy: SetupInstallPolicy.IfNeeded), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        catalog.ResolveLatestCallCount.Should().Be(0);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_IfNeeded_RecognizesInstalledRidPointerWorkload()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([LogicalEntry(_hostPackageId, "4.0.0", "win-x64")]);
        FakeCatalog catalog = Catalog();
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(
            Options(features: ["host"], installPolicy: SetupInstallPolicy.IfNeeded),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        catalog.ResolveLatestCallCount.Should().Be(0);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Check_RecognizesExactRidPointerWorkloadVersion()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([LogicalEntry(_hostPackageId, "4.0.0", "win-x64")]);
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(
            Options(features: ["host"], check: true),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        _interaction.Lines.Should().NotContain(line => line.Contains("not installed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_LatestCompatibleCheck_FailsWhenLatestCompatibleIsMissing()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content)]);
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], check: true), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("4.1.0") && line.Contains("not installed"));
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_JsonOutput_WritesNdjsonEvents()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content)]);
        SetupRunner runner = CreateRunner(Catalog());

        SetupRunResult result = await runner.RunAsync(
            Options(
                features: ["host"],
                installPolicy: SetupInstallPolicy.IfNeeded,
                check: true,
                outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        string[] eventTypes = [.. _interaction.Lines
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()!)
        ];

        eventTypes.Should().Equal(["setup.started", "profile.started", "dependency.detected", "dependency.result", "profile.completed", "setup.completed"]);
    }

    [Fact]
    public async Task RunAsync_ProfileUnsupportedWorker_Fails()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content),
                Entry(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.0.0", kind: WorkloadKind.Content),
            ]);
        SetupRunner runner = CreateRunner(
            Catalog(),
            profileCatalog: new ProfileCatalog([
                new FakeProfileSource(new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"), [
                    new("flex", new ProfileDefinition
                    {
                        Host = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        ExtensionBundle = new ProfileVersionConstraint { Version = "[4.0.0, 5.0.0)" },
                        SupportedRuntimes = ["python"],
                    }),
                ]),
            ], NullLogger<ProfileCatalog>.Instance));

        SetupRunResult result = await runner.RunAsync(
            Options(
                features: ["node"],
                profiles: ["flex"],
                installPolicy: SetupInstallPolicy.IfNeeded,
                check: true),
            CancellationToken.None);

        result.ExitCode.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("does not support runtime 'node'"));
    }

    [Fact]
    public async Task RunAsync_FeatureForRuntime_InstallsStackWorkload()
    {
        string workerPackageId = WorkerPackage("node");
        const string nodeStack = "Azure.Functions.Cli.Workloads.Node";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(workerPackageId, "1.0.0")
            .WithLatest(nodeStack, "1.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["node"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeStack, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FeatureForRuntime_InstallsTemplatesWorkload()
    {
        string workerPackageId = WorkerPackage("node");
        const string nodeStack = "Azure.Functions.Cli.Workloads.Node";
        const string nodeTemplates = "Azure.Functions.Cli.Workloads.Templates.Node";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(workerPackageId, "1.0.0")
            .WithLatest(nodeStack, "1.0.0")
            .WithLatest(nodeTemplates, "1.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["node"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeTemplates, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StableProject_IgnoresPrereleaseBundle_EvenWithIncludePrerelease()
    {
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithVersions(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0", "4.20.0-preview.1");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(includePrerelease: true), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.10.0"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.20.0-preview.1"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HostJsonPreviewBundle_InstallsPreviewBundleAndTemplates()
    {
        const string nodeStack = "Azure.Functions.Cli.Workloads.Node";
        const string nodeTemplates = "Azure.Functions.Cli.Workloads.Templates.Node";
        _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithVersions(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0", "4.11.0-preview.1")
            .WithLatest(WorkerPackage("node"), "1.0.0")
            .WithLatest(nodeStack, "1.0.0")
            .WithVersions(nodeTemplates, "1.0.0", "1.1.0-preview.1");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["node"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.11.0-preview.1"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeTemplates, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.1.0-preview.1"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HostJsonPreviewBundle_NoPreviewPublished_FallsBackToStableWithWarning()
    {
        _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithVersions(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.10.0"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
        _interaction.Lines.Should().Contain(line => line.Contains("func workload search bundles --prerelease", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_HostJsonExperimentalBundle_InstallsExperimentalBundle()
    {
        _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.ExperimentalBundleId, "[4.0.0, 5.0.0)"));
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithVersions(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0", "4.12.0-experimental.3");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.12.0-experimental.3"),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PreviewBundleFallbackAlreadyInstalled_CheckPasses()
    {
        _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Entry(_hostPackageId, "4.1.0", aliases: ["host"], kind: WorkloadKind.Content),
                Entry(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0", kind: WorkloadKind.Content),
            ]);
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithVersions(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(check: true), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        _interaction.Lines.Should().NotContain(line => line.Contains("extension bundle", StringComparison.Ordinal)
            && line.Contains("not installed", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("func workload search bundles --prerelease", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FeatureWithoutStackMapping_DoesNotInstallStack()
    {
        string workerPackageId = WorkerPackage("custom");
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(workerPackageId, "1.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["custom"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.StartsWith("Azure.Functions.Cli.Workloads.", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("Azure.Functions.Cli.Workloads.Workers.", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith(HostWorkloadPackage.PackageId, StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("Azure.Functions.Cli.Workloads.ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("java", "Java", false)]
    [InlineData("java", "Java", true)]
    [InlineData("powershell", "PowerShell", false)]
    [InlineData("powershell", "PowerShell", true)]
    public async Task RunAsync_JavaOrPowerShell_InstallsWorkerStackAndBundle(string runtime, string packageSuffix, bool explicitFeature)
    {
        string stackPackageId = $"Azure.Functions.Cli.Workloads.{packageSuffix}";
        string workerPackageId = WorkerPackage(runtime);
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(workerPackageId, "1.0.0")
            .WithLatest(stackPackageId, "1.0.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = CreateRunner(catalog, projectConfig: new Dictionary<string, string?>
        {
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = runtime,
        });

        SetupRunResult result = await runner.RunAsync(
            Options(features: explicitFeature ? [runtime] : [], installPolicy: SetupInstallPolicy.IfNeeded),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        _installer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorkloadInstaller.InstallFromCatalogAsync))
            .Select(call => (string)call.GetArguments()[0]!)
            .Should().Equal(
                _hostPackageId.ToLowerInvariant(),
                workerPackageId.ToLowerInvariant(),
                stackPackageId.ToLowerInvariant(),
                IInstalledBundleWorkloads.BundleWorkloadPackageId.ToLowerInvariant());
        _interaction.Lines.Should().Contain(line => line.Contains($"Installed {runtime} stack", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("java", false, "missing")]
    [InlineData("java", true, "missing")]
    [InlineData("java", false, "installed")]
    [InlineData("java", true, "installed")]
    [InlineData("java", false, "unpublished")]
    [InlineData("java", true, "unpublished")]
    [InlineData("powershell", false, "missing")]
    [InlineData("powershell", true, "missing")]
    [InlineData("powershell", false, "installed")]
    [InlineData("powershell", true, "installed")]
    [InlineData("powershell", false, "unpublished")]
    [InlineData("powershell", true, "unpublished")]
    public async Task RunAsync_JavaOrPowerShellCheck_RequiresStack(string runtime, bool explicitFeature, string stackState)
    {
        string stackPackageId = $"Azure.Functions.Cli.Workloads.{runtime}";
        List<WorkloadEntry> installed = [
            Entry(_hostPackageId, "4.1.0"),
            Entry(WorkerPackage(runtime), "1.0.0"),
            Entry(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0"),
        ];
        if (stackState == "installed")
        {
            installed.Add(Entry(stackPackageId, "1.0.0"));
        }

        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(installed);
        FakeCatalog catalog = Catalog();
        if (stackState != "unpublished")
        {
            catalog.WithLatest(stackPackageId, "1.0.0");
        }

        SetupRunner runner = CreateRunner(catalog, projectConfig: new Dictionary<string, string?>
        {
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = runtime,
        });

        SetupRunResult result = await runner.RunAsync(
            Options(
                features: explicitFeature ? [runtime] : [],
                installPolicy: SetupInstallPolicy.IfNeeded,
                check: true,
                outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        bool satisfied = stackState == "installed";
        result.ExitCode.Should().Be(satisfied ? 0 : 1);
        JsonElement[] events = [.. _interaction.Lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        })];
        JsonElement stackResult = events.Should().ContainSingle(item =>
            item.GetProperty("type").GetString() == "dependency.result"
            && item.GetProperty("dependency_type").GetString() == "stack").Which;
        stackResult.GetProperty("package_id").GetString().Should().BeEquivalentTo(stackPackageId);
        stackResult.GetProperty("status").GetString().Should().Be(satisfied ? "satisfied" : "failed");
        events[^1].GetProperty("type").GetString().Should().Be(satisfied ? "setup.completed" : "setup.failed");
        _installer.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_InteractiveEmptyFolder_PromptsForStackAndInstalls()
    {
        string workerPackageId = WorkerPackage("node");
        const string nodeStack = "Azure.Functions.Cli.Workloads.Node";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(workerPackageId, "1.0.0")
            .WithLatest(nodeStack, "1.0.0");

        InteractiveTestInteractionService interactive = new();
        SetupRunner runner = new(
            interactive,
            new SetupFeatureResolver(interactive, _store, new FakeCliConfigurationProvider(new Dictionary<string, string?>())),
            new SetupProfileScopeResolver(
                _profileCatalog,
                new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
                new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions())),
            new SetupDependencyPlanBuilder(_bundleReader),
            new SetupDependencyInstaller(interactive, _store, catalog, _installer));

        SetupRunResult result = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: false,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        interactive.Lines.Should().Contain(line => line.StartsWith("MULTISELECT:", StringComparison.Ordinal));
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeStack, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoProfile_DoesNotInstallStackWorkloads()
    {
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.StartsWith("Azure.Functions.Cli.Workloads.Node", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Azure.Functions.Cli.Workloads.Python", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Azure.Functions.Cli.Workloads.Go", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Azure.Functions.Cli.Workloads.DotNet", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InteractiveEmptyFolder_RendersInstalledStacksAsStaticLines_AboveThePrompt()
    {
        const string nodeStack = "Azure.Functions.Cli.Workloads.Node";
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(WorkerPackage("node"), "1.0.0")
            .WithLatest(nodeStack, "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(nodeStack, "1.0.0")]);

        InteractiveTestInteractionService interactive = new();
        SetupRunner runner = new(
            interactive,
            new SetupFeatureResolver(interactive, _store, new FakeCliConfigurationProvider(new Dictionary<string, string?>())),
            new SetupProfileScopeResolver(
                _profileCatalog,
                new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
                new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions())),
            new SetupDependencyPlanBuilder(_bundleReader),
            new SetupDependencyInstaller(interactive, _store, catalog, _installer));

        _ = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: false,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        // Installed stacks render as static "fake checkbox" lines above the
        // prompt with the uninstall hint, and are kept out of the multi-select
        // entirely so the user can't toggle them.
        interactive.Lines.Should().Contain(line => line.Contains("Already installed", StringComparison.Ordinal)
            && line.Contains("func workload uninstall", StringComparison.Ordinal));
        interactive.Lines.Should().Contain(line => line.Contains("[✓] node", StringComparison.Ordinal));
        interactive.Lines.Should().NotContain(line => line.StartsWith("MULTISELECT:", StringComparison.Ordinal) && line.Contains("node", StringComparison.Ordinal));
        interactive.MultiSelectionChoices.SelectMany(g => g).Should().NotContain(c => c.Value == "node");
    }

    [Fact]
    public async Task RunAsync_MarksFirstRunComplete_OnSuccess()
    {
        IFirstRunStateStore firstRunStore = Substitute.For<IFirstRunStateStore>();
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0");
        SetupRunner runner = new(
            _interaction,
            new SetupFeatureResolver(_interaction, _store, new FakeCliConfigurationProvider(new Dictionary<string, string?>())),
            new SetupProfileScopeResolver(
                _profileCatalog,
                new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
                new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions())),
            new SetupDependencyPlanBuilder(_bundleReader),
            new SetupDependencyInstaller(_interaction, _store, catalog, _installer),
            firstRunStore);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        await firstRunStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    private SetupRunner CreateRunner(
        IWorkloadCatalog catalog,
        IReadOnlyDictionary<string, string?>? projectConfig = null,
        IProfileCatalog? profileCatalog = null)
        => new(
            _interaction,
            new SetupFeatureResolver(_interaction, _store, new FakeCliConfigurationProvider(projectConfig ?? new Dictionary<string, string?>())),
            new SetupProfileScopeResolver(
                profileCatalog ?? _profileCatalog,
                new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
                new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions())),
            new SetupDependencyPlanBuilder(_bundleReader),
            new SetupDependencyInstaller(_interaction, _store, catalog, _installer));

    private SetupCommandOptions Options(
        IReadOnlyList<string>? features = null,
        IReadOnlyList<string>? profiles = null,
        string? source = null,
        SetupInstallPolicy installPolicy = SetupInstallPolicy.LatestCompatible,
        bool includePrerelease = false,
        bool check = false,
        SetupOutputMode outputMode = SetupOutputMode.Plain)
        => new(
            new DirectoryInfo(_tempDir),
            features ?? [],
            profiles ?? [],
            source,
            installPolicy,
            IncludePrerelease: includePrerelease,
            NonInteractive: true,
            AssumeYes: true,
            check,
            outputMode);

    private static FakeCatalog Catalog() => new();

    private static string WorkerPackage(string runtime) => $"Azure.Functions.Cli.Workloads.Workers.{runtime}";

    private static WorkloadEntry Entry(
        string packageId,
        string version,
        IReadOnlyList<string>? aliases = null,
        WorkloadKind kind = WorkloadKind.Workload)
        => new()
        {
            PackageId = packageId.ToLowerInvariant(),
            PackageVersion = version,
            Aliases = aliases ?? [],
            Kind = kind,
        };

    private static WorkloadEntry LogicalEntry(string pointerPackageId, string version, string runtimeIdentifier)
        => new()
        {
            PackageId = $"{pointerPackageId}.{runtimeIdentifier}",
            PackageVersion = version,
            RuntimeIdentifier = runtimeIdentifier,
            IsImplicitlyInstalled = true,
            LogicalPackage = new LogicalPackage
            {
                PackageId = pointerPackageId,
                PackageVersion = version,
            },
            Kind = WorkloadKind.Content,
        };

    private sealed class FakeCatalog : IWorkloadCatalog
    {
        private readonly PackageSource _source = new("https://example.test/v3/index.json");
        private readonly Dictionary<string, CatalogSearchResult> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NuGetVersion> _latest = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NuGetVersion>> _versions = new(StringComparer.OrdinalIgnoreCase);

        public int ResolveLatestCallCount { get; private set; }

        public List<string?> SearchSources { get; } = [];

        public List<string?> ResolveSources { get; } = [];

        public List<(string PackageId, VersionRange Range)> RangeResolutions { get; } = [];

        public FakeCatalog WithAlias(string alias, string packageId, string version)
        {
            var parsed = NuGetVersion.Parse(version);
            var result = new CatalogSearchResult(
                packageId.ToLowerInvariant(),
                parsed,
                Title: packageId,
                Description: null,
                Aliases: [alias],
                _source);
            _aliases[alias] = result;
            _latest[packageId] = parsed;
            AddVersion(packageId, parsed);
            return this;
        }

        public FakeCatalog WithLatest(string packageId, string version)
        {
            var parsed = NuGetVersion.Parse(version);
            _latest[packageId] = parsed;
            AddVersion(packageId, parsed);
            return this;
        }

        public FakeCatalog WithVersions(string packageId, params string[] versions)
        {
            foreach (string version in versions)
            {
                AddVersion(packageId, NuGetVersion.Parse(version));
            }

            return this;
        }

        public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(CatalogSearchQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchSources.Add(query.Source);
            if (!string.IsNullOrWhiteSpace(query.Filter) && _aliases.TryGetValue(query.Filter, out CatalogSearchResult? result))
            {
                return Task.FromResult<IReadOnlyList<CatalogSearchResult>>([result]);
            }

            return Task.FromResult<IReadOnlyList<CatalogSearchResult>>([.. _aliases.Values]);
        }

        public Task<ResolvedPackage?> ResolveLatestVersionAsync(
            string packageId,
            bool? includePrerelease,
            NuGetVersion? currentVersion = null,
            bool allowMajor = true,
            string? source = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveLatestCallCount++;
            ResolveSources.Add(source);
            return Task.FromResult(CreateResolved(packageId, _latest.TryGetValue(packageId, out NuGetVersion? version) ? version : null));
        }

        public Task<ResolvedPackage?> ResolveLatestVersionInRangeAsync(
            string packageId,
            VersionRange versionRange,
            bool? includePrerelease,
            string? source = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveLatestCallCount++;
            ResolveSources.Add(source);
            RangeResolutions.Add((packageId, versionRange));
            NuGetVersion? version = _latest.TryGetValue(packageId, out NuGetVersion? candidate) && versionRange.Satisfies(candidate)
                ? candidate
                : null;
            return Task.FromResult(CreateResolved(packageId, version));
        }

        public Task<ResolvedPackage?> ResolveLatestVersionOnChannelAsync(
            string packageId,
            string? prereleaseLabel,
            VersionRange? versionRange = null,
            string? source = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveLatestCallCount++;
            ResolveSources.Add(source);

            NuGetVersion? selected = null;
            if (_versions.TryGetValue(packageId, out List<NuGetVersion>? candidates))
            {
                selected = candidates
                    .Where(v => MatchesLabel(v, prereleaseLabel))
                    .Where(v => versionRange is null || WorkloadVersionRanges.SatisfiesRange(versionRange, v, prereleaseLabel is not null))
                    .OrderByDescending(v => v)
                    .FirstOrDefault();
            }

            return Task.FromResult(CreateResolved(packageId, selected));
        }

        public Task<ResolvedPackage?> ResolveVersionAsync(
            string packageId,
            NuGetVersion version,
            string? source = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateResolved(packageId, version));
        }

        public Task<Stream> DownloadAsync(ResolvedPackage package, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(string packageId, string? source = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NuGetVersion>>(
                _latest.TryGetValue(packageId, out NuGetVersion? version) ? [version] : []);
        }

        private static bool MatchesLabel(NuGetVersion version, string? prereleaseLabel)
            => prereleaseLabel is null
                ? !version.IsPrerelease
                : version.IsPrerelease && string.Equals(version.ReleaseLabels.FirstOrDefault(), prereleaseLabel, StringComparison.OrdinalIgnoreCase);

        private void AddVersion(string packageId, NuGetVersion version)
        {
            if (!_versions.TryGetValue(packageId, out List<NuGetVersion>? list))
            {
                list = [];
                _versions[packageId] = list;
            }

            if (!list.Contains(version))
            {
                list.Add(version);
            }
        }

        private ResolvedPackage? CreateResolved(string packageId, NuGetVersion? version)
            => version is null ? null : new ResolvedPackage(packageId.ToLowerInvariant(), version, _source);
    }

    private sealed class FakeCliConfigurationProvider(IReadOnlyDictionary<string, string?> projectValues) : ICliConfigurationProvider
    {
        private readonly IConfiguration _projectConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(projectValues)
            .Build();

        private readonly IConfiguration _userConfiguration = new ConfigurationBuilder().Build();

        public IConfiguration GetUserConfiguration() => _userConfiguration;

        public IConfiguration GetProjectConfiguration(DirectoryInfo projectDirectory) => _projectConfiguration;

        public IConfiguration GetEffectiveConfiguration(DirectoryInfo projectDirectory) => _projectConfiguration;
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;

        public override Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
        {
            // Record the prompt then mimic an interactive user picking the
            // first listed stack (preferring 'node' for stable test asserts).
            var picks = choices.ToList();
            base.PromptForMultiSelectionAsync(title, picks, cancellationToken).GetAwaiter().GetResult();
            string? first = picks.FirstOrDefault(c => string.Equals(c, "node", StringComparison.OrdinalIgnoreCase))
                ?? picks.FirstOrDefault();
            return Task.FromResult<IReadOnlyList<string>>(first is null ? [] : [first]);
        }

        public override Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
        {
            var picks = choices.ToList();
            base.PromptForMultiSelectionAsync(title, picks, cancellationToken).GetAwaiter().GetResult();
            MultiSelectionChoice? first = picks.FirstOrDefault(c => string.Equals(c.Value, "node", StringComparison.OrdinalIgnoreCase))
                ?? picks.FirstOrDefault();
            return Task.FromResult<IReadOnlyList<string>>(first is null ? [] : [first.Value]);
        }
    }

    private sealed class FakeProfileSource(
        ProfileSourceInfo source,
        IReadOnlyList<KeyValuePair<string, ProfileDefinition>> profiles) : IProfileSource
    {
        public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, ProfileDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, ProfileDefinition definition) in profiles)
            {
                definitions.Add(name, definition);
            }

            return Task.FromResult(new ProfileSourceSnapshot(source, definitions));
        }
    }
}
