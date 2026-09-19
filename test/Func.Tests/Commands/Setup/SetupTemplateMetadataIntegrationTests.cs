// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Workloads.Catalog;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupTemplateMetadataIntegrationTests
{
    private const string TemplatesId = "contoso.templates.shared";
    private const string PreviewVersion = "2.0.0-preview.1";
    private const string ExperimentalVersion = "3.0.0-experimental.1";
    private static readonly PackageSource _source = new("https://static-template-feed.test/v3/index.json");

    [Theory]
    [InlineData(BundleHelpers.PreviewBundleId, false, false)]
    [InlineData(BundleHelpers.PreviewBundleId, false, true)]
    [InlineData(BundleHelpers.PreviewBundleId, true, false)]
    [InlineData(BundleHelpers.PreviewBundleId, true, true)]
    [InlineData(BundleHelpers.ExperimentalBundleId, false, false)]
    [InlineData(BundleHelpers.ExperimentalBundleId, false, true)]
    public async Task Setup_StaticFeedSelectsOlderChannel_ValidatesTheActualInstalledEntry(
        string bundleId, bool previewClaimsNode, bool alreadyInstalled)
    {
        using CancellationTokenSource cancellation = new();
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(TemplatesId, Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), cancellation.Token)
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>([new(PreviewVersion), new(ExperimentalVersion)]));
        ServiceIndexResourceV3 serviceIndex = new(JObject.Parse("""
            { "version": "3.0.0", "resources": [
                { "@id": "https://static-template-feed.test/query", "@type": "SearchQueryService" }
            ] }
            """), DateTime.UnixEpoch);
        StaticFeedClient client = new(TestRepository.Build(_source, serviceIndex, find));
        var sourceProvider = Substitute.For<Azure.Functions.Cli.Workloads.Catalog.IPackageSourceProvider>();
        sourceProvider.GetSource(_source.Source).Returns(_source);
        WorkloadCatalog catalog = new(Options.Create(new WorkloadCatalogOptions()), sourceProvider, _ => client);
        SetupStackCatalog stacks = new(catalog);
        var store = Substitute.For<IWorkloadStore>();
        WorkloadRegistry registry = new();
        store.GetWorkloadsAsync(cancellation.Token).Returns(_ => registry.Workloads.ToArray());
        var bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), cancellation.Token).Returns(new HostJsonBundleSection(bundleId, "[4.0.0, 5.0.0)"));
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), ["node"], [], _source.Source,
            SetupInstallPolicy.LatestCompatible, IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Json);
        SetupFeatureResolver features = new(new TestInteractionService(), store, Substitute.For<ICliConfigurationProvider>(), stacks);
        SetupDependencyPlanBuilder planner = new(bundleReader, stacks);
        SetupFeaturePlan? featurePlan = await features.ResolveFeaturesAsync(options, cancellation.Token);
        SetupDependencyPlan plan = await planner.BuildDependencyPlanAsync(options, featurePlan!, SetupProfileScope.Unconstrained, cancellation.Token);
        plan.Failures.Should().BeEmpty();
        SetupDependency dependency = plan.Dependencies.Should().ContainSingle(row => row.Kind == SetupDependencyKind.Templates).Subject;
        dependency.Name.Should().Be("node");
        dependency.PackageId.Should().Be(TemplatesId);
        dependency.Channel.Should().Be(BundleHelpers.GetBundleChannel(bundleId));
        string selectedVersion = bundleId == BundleHelpers.PreviewBundleId ? PreviewVersion : ExperimentalVersion;
        bool accepted = bundleId == BundleHelpers.ExperimentalBundleId || previewClaimsNode;
        WorkloadEntry installed = new()
        {
            PackageId = TemplatesId,
            PackageVersion = selectedVersion,
            Kind = WorkloadKind.Content,
            Aliases = accepted ? ["node-templates"] : ["java-templates"],
            RuntimeIdentifier = "any",
        };
        var workloadInstaller = Substitute.For<IWorkloadInstaller>();
        workloadInstaller.InstallFromCatalogAsync(TemplatesId, Arg.Is<NuGetVersion?>(v => v != null && v.ToNormalizedString() == selectedVersion),
                _source.Source, false, true, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), cancellation.Token)
            .Returns(_ =>
            {
                registry.Workloads.Add(installed);
                return new WorkloadInstallResult(installed, alreadyInstalled);
            });
        SetupDependencyInstaller installer = new(new TestInteractionService(), store, catalog, workloadInstaller);

        SetupDependencyResult result = await installer.EnsureDependencyAsync(options, dependency, cancellation.Token);

        result.Status.Should().Be(accepted
            ? alreadyInstalled ? SetupDependencyStatus.Satisfied : SetupDependencyStatus.Installed
            : SetupDependencyStatus.Failed);
        result.PackageId.Should().Be(TemplatesId);
        result.Version.Should().Be(selectedVersion);
        if (!accepted)
        {
            result.Message.Should().Contain("java-templates").And.Contain("does not claim 'node-templates'")
                .And.Contain("Failure detected after install").And.Contain("No rollback was performed")
                .And.Contain($"func workload uninstall {TemplatesId} --version {PreviewVersion} --exact");
        }

        client.PrereleaseQueries.Should().Equal(false, true);
        await find.Received(1).GetAllVersionsAsync(TemplatesId, Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), cancellation.Token);
        workloadInstaller.ReceivedCalls().Should().ContainSingle("setup must leave the installed version in place on a mismatch");
        registry.Workloads.Should().ContainSingle().Which.Should().BeSameAs(installed);
        var adapter = new InstalledTemplatesWorkloads(store, Substitute.For<IWorkloadPaths>());
        (await adapter.ListInstalledAsync("node", cancellation.Token)).Should().HaveCount(accepted ? 1 : 0);
    }

    private sealed class StaticFeedClient(SourceRepository repository) : NuGetProtocolSourceClient(repository)
    {
        public List<bool> PrereleaseQueries { get; } = [];

        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool includePrerelease = searchUri.Query.Contains("prerelease=true", StringComparison.Ordinal);
            PrereleaseQueries.Add(includePrerelease);
            JArray rows = new(Row("contoso.stack.node", "1.0.0", "kind:workload alias:node stack:node"));
            if (includePrerelease)
            {
                // Search advertises only the latest version's aliases, not the
                // metadata of the older version selected from the preview channel.
                rows.Add(Row(TemplatesId, ExperimentalVersion, "kind:content alias:node-templates"));
            }

            return Task.FromResult<JObject?>(new JObject { ["data"] = rows, ["totalHits"] = rows.Count });
        }

        private static JObject Row(string id, string version, string tags)
            => new()
            {
                ["id"] = id,
                ["version"] = version,
                ["tags"] = tags,
                ["packageTypes"] = new JArray(new JObject { ["name"] = "FuncCliWorkload" }),
            };
    }
}