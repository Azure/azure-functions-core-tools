// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
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

    [Fact]
    public async Task RunAsync_ConfiguredDotnetIsolated_InstallsWorkerAndSkipsBundle()
    {
        string workerPackageId = WorkerPackage("dotnet-isolated");
        FakeCatalog catalog = Catalog()
            .WithLatest(_hostPackageId, "4.1.0")
            .WithLatest(workerPackageId, "1.2.3");
        SetupRunner runner = CreateRunner(catalog, projectConfig: new Dictionary<string, string?>
        {
            [$"{StackOptions.SectionName}:runtime"] = "dotnet-isolated",
        });

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        await _installer.Received(1).InstallFromCatalogAsync(
            Arg.Is<string>(id => string.Equals(id, workerPackageId, StringComparison.OrdinalIgnoreCase)),
            Arg.Is<NuGetVersion>(version => version.ToNormalizedString() == "1.2.3"),
            Arg.Any<string?>(),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Is(false),
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
        bool check = false,
        SetupOutputMode outputMode = SetupOutputMode.Plain)
        => new(
            new DirectoryInfo(_tempDir),
            features ?? [],
            profiles ?? [],
            source,
            installPolicy,
            IncludePrerelease: false,
            NonInteractive: true,
            AssumeYes: true,
            check,
            outputMode);

    private static FakeCatalog Catalog() => new();

    private static string WorkerPackage(string runtime)
        => "Azure.Functions.Cli.Workloads.Workers." + runtime;

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
