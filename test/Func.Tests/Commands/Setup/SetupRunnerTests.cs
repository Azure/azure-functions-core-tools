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
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRunnerTests : IDisposable
{
    private static readonly string _hostPackageId = HostWorkloadPackage.CurrentPackageId;
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
                Arg.Any<CancellationToken>())
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

        Assert.Equal(0, result.ExitCode);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.10.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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

        Assert.Equal(0, result.ExitCode);

        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _dotNetStackPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.0.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());

        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains(".Workers.", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains("ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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

        Assert.Equal(0, result.ExitCode);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _dotNetStackPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.0.0"),
            Arg.Is<string?>(actual => actual == source),
            Arg.Is(true),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains(".Workers.", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.Contains("ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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
            ]));

        SetupRunResult result = await runner.RunAsync(Options(features: ["dotnet"], profiles: ["flex"]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(_interaction.Lines, line => line.Contains("does not support runtime", StringComparison.OrdinalIgnoreCase));
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

        Assert.Equal(0, result.ExitCode);
        using var started = JsonDocument.Parse(_interaction.Lines[0]);
        JsonElement root = started.RootElement;
        string[] features = [.. root.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()!)];
        string[] workerRuntimes = [.. root.GetProperty("worker_runtimes").EnumerateArray().Select(runtime => runtime.GetString()!)];

        Assert.Equal(["dotnet"], features);
        Assert.Empty(workerRuntimes);
        Assert.DoesNotContain(_interaction.Lines, line => line.Contains("dotnet-isolated", StringComparison.OrdinalIgnoreCase));
        string stackLine = Assert.Single(
            _interaction.Lines,
            line => line.Contains("\"type\":\"dependency.detected\"", StringComparison.OrdinalIgnoreCase)
                && line.Contains("\"dependency_type\":\"stack\"", StringComparison.OrdinalIgnoreCase));
        using var stackEvent = JsonDocument.Parse(stackLine);
        JsonElement stackRoot = stackEvent.RootElement;

        Assert.Equal("dotnet", stackRoot.GetProperty("name").GetString());
        Assert.Equal(_dotNetStackPackageId, stackRoot.GetProperty("package_id").GetString(), ignoreCase: true);
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
            ]));

        SetupRunResult result = await runner.RunAsync(Options(features: [requestedFeature], profiles: ["flex"]), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_interaction.Lines, line => line.Contains("does not support runtime 'dotnet'", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_interaction.Lines, line => line.Contains("dotnet-isolated", StringComparison.OrdinalIgnoreCase));
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
            ]));

        SetupRunResult result = await runner.RunAsync(
            Options(features: ["dotnet"], profiles: ["flex"], outputMode: SetupOutputMode.Json),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        string failureLine = Assert.Single(_interaction.Lines, line => line.Contains("\"status\":\"failed\"", StringComparison.OrdinalIgnoreCase));
        using var failure = JsonDocument.Parse(failureLine);
        JsonElement root = failure.RootElement;

        Assert.Equal("runtime", root.GetProperty("dependency_type").GetString());
        Assert.Equal("dotnet", root.GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("package_id", out _));
        Assert.DoesNotContain("dotnet-isolated", failureLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_Source_PassesSourceToCatalogAndInstaller()
    {
        const string source = "https://example.test/custom/index.json";
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], source: source), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(catalog.SearchSources);
        Assert.NotEmpty(catalog.ResolveSources);
        Assert.All(catalog.ResolveSources, actual => Assert.Equal(source, actual));
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Is<string?>(actual => actual == source),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HostFeature_UsesRidPackageIdWithoutAliasSearch()
    {
        FakeCatalog catalog = Catalog()
            .WithAlias("host", "rogue.host.package", "9.9.9")
            .WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(catalog.SearchSources);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, _hostPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "4.1.0"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_IfNeeded_SkipsCatalogWhenCompatibleVersionInstalled()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content)]);
        FakeCatalog catalog = Catalog();
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], installPolicy: SetupInstallPolicy.IfNeeded), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, catalog.ResolveLatestCallCount);
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

    [Fact]
    public async Task RunAsync_LatestCompatibleCheck_FailsWhenLatestCompatibleIsMissing()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([Entry(_hostPackageId, "4.0.0", aliases: ["host"], kind: WorkloadKind.Content)]);
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"], check: true), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_interaction.Lines, line => line.Contains("4.1.0") && line.Contains("not installed"));
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

        Assert.Equal(0, result.ExitCode);
        string[] eventTypes = [.. _interaction.Lines
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()!)
        ];

        Assert.Equal(
            ["setup.started", "profile.started", "dependency.detected", "dependency.result", "profile.completed", "setup.completed"],
            eventTypes);
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
            ]));

        SetupRunResult result = await runner.RunAsync(
            Options(
                features: ["node"],
                profiles: ["flex"],
                installPolicy: SetupInstallPolicy.IfNeeded,
                check: true),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_interaction.Lines, line => line.Contains("does not support runtime 'node'"));
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

        Assert.Equal(0, result.ExitCode);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeStack, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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

        Assert.Equal(0, result.ExitCode);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeTemplates, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FeatureWithoutStackMapping_DoesNotInstallStack()
    {
        string workerPackageId = WorkerPackage("java");
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.10.0")
            .WithLatest(workerPackageId, "1.0.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["java"]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Is<string>(id => id.StartsWith("Azure.Functions.Cli.Workloads.", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("Azure.Functions.Cli.Workloads.Workers.", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("Azure.Functions.Cli.Workloads.Host.", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("Azure.Functions.Cli.Workloads.ExtensionBundles", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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
            _store,
            catalog,
            _installer,
            _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(new Dictionary<string, string?>()),
            _bundleReader);

        SetupRunResult result = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: true,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(interactive.Lines, line => line.StartsWith("MULTISELECT:", StringComparison.Ordinal));
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, nodeStack, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoProfile_DoesNotInstallStackWorkloads()
    {
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = CreateRunner(catalog);

        SetupRunResult result = await runner.RunAsync(Options(features: ["host"]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
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
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InteractiveEmptySelection_ExitsCleanlyWithSkippedHint()
    {
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        EmptyPickInteractionService interactive = new();
        SetupRunner runner = new(
            interactive,
            _store,
            catalog,
            _installer,
            _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(new Dictionary<string, string?>()),
            _bundleReader);

        SetupRunResult result = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: true,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(interactive.Lines, line => line.StartsWith("HINT:", StringComparison.Ordinal) && line.Contains("No stacks selected", StringComparison.Ordinal));
        await _installer.DidNotReceiveWithAnyArgs().InstallFromCatalogAsync(default!, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_InteractiveEmptyFolder_LabelsInstalledStacks()
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
            _store,
            catalog,
            _installer,
            _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(new Dictionary<string, string?>()),
            _bundleReader);

        _ = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: true,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        Assert.Contains(interactive.Lines, line => line.StartsWith("MULTISELECT:", StringComparison.Ordinal) && line.Contains("node  (installed)", StringComparison.Ordinal));
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
            _store,
            catalog,
            _installer,
            _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(new Dictionary<string, string?>()),
            _bundleReader,
            firstRunStore);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        await firstRunStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MarksFirstRunComplete_WhenInteractiveSelectionEmpty()
    {
        IFirstRunStateStore firstRunStore = Substitute.For<IFirstRunStateStore>();
        EmptyPickInteractionService interactive = new();
        FakeCatalog catalog = Catalog().WithLatest(_hostPackageId, "4.1.0");
        SetupRunner runner = new(
            interactive,
            _store,
            catalog,
            _installer,
            _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(new Dictionary<string, string?>()),
            _bundleReader,
            firstRunStore);

        SetupRunResult result = await runner.RunAsync(
            new SetupCommandOptions(
                new DirectoryInfo(_tempDir),
                Features: [],
                ProfileNames: [],
                Source: null,
                SetupInstallPolicy.LatestCompatible,
                IncludePrerelease: false,
                NonInteractive: false,
                AssumeYes: true,
                Check: false,
                SetupOutputMode.Plain),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        await firstRunStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    private SetupRunner CreateRunner(
        IWorkloadCatalog catalog,
        IReadOnlyDictionary<string, string?>? projectConfig = null,
        IProfileCatalog? profileCatalog = null)
        => new(
            _interaction,
            _store,
            catalog,
            _installer,
            profileCatalog ?? _profileCatalog,
            new TestOptionsMonitor<ProjectProfileOptions>(new ProjectProfileOptions()),
            new TestOptionsMonitor<UserProfilePreferenceOptions>(new UserProfilePreferenceOptions()),
            new FakeCliConfigurationProvider(projectConfig ?? new Dictionary<string, string?>()),
            _bundleReader);

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

    private sealed class FakeCatalog : IWorkloadCatalog
    {
        private readonly PackageSource _source = new("https://example.test/v3/index.json");
        private readonly Dictionary<string, CatalogSearchResult> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NuGetVersion> _latest = new(StringComparer.OrdinalIgnoreCase);

        public int ResolveLatestCallCount { get; private set; }

        public List<string?> SearchSources { get; } = [];

        public List<string?> ResolveSources { get; } = [];

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
            return this;
        }

        public FakeCatalog WithLatest(string packageId, string version)
        {
            _latest[packageId] = NuGetVersion.Parse(version);
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
            bool includePrerelease,
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
            bool includePrerelease,
            string? source = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveLatestCallCount++;
            ResolveSources.Add(source);
            NuGetVersion? version = _latest.TryGetValue(packageId, out NuGetVersion? candidate) && versionRange.Satisfies(candidate)
                ? candidate
                : null;
            return Task.FromResult(CreateResolved(packageId, version));
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

    private sealed class EmptyPickInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;

        public override Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
        {
            var picks = choices.ToList();
            base.PromptForMultiSelectionAsync(title, picks, cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult<IReadOnlyList<string>>([]);
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
