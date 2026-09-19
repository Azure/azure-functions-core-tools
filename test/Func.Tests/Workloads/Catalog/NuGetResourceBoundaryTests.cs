// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Projects;
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

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class NuGetResourceBoundaryTests
{
    private static readonly PackageSource _source = new("https://resource-boundary.test/v3/index.json") { ProtocolVersion = 3 };

    [Theory]
    [InlineData(false, "http")]
    [InlineData(true, "http")]
    [InlineData(false, "fatal-http")]
    [InlineData(true, "fatal-http")]
    [InlineData(false, "fatal")]
    [InlineData(true, "fatal")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "programmer")]
    [InlineData(true, "programmer")]
    public async Task ResourceAcquisition_NonSchemaFailure_PreservesOriginalException(bool download, string failure)
    {
        using CancellationTokenSource cancellation = new();
        Exception expected = failure switch
        {
            "http" => new HttpRequestException("Offline"),
            "fatal-http" => new FatalProtocolException("Offline", new HttpRequestException("Offline")),
            "fatal" => new FatalProtocolException("Protocol failure"),
            "cancel" => new OperationCanceledException(cancellation.Token),
            _ => new InvalidOperationException("Provider bug"),
        };
        IndexProvider index = new(() => throw expected);
        NuGetProtocolSourceClient client = Client(index);

        Exception? actual = await Record.ExceptionAsync(() => InvokeAsync(client, download, cancellation.Token));

        actual.Should().BeSameAs(expected);
        index.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceAcquisition_RealIndexSchemaFailure_PreservesWrappedCause(bool download)
    {
        Exception? cause = null;
        FatalProtocolException? protocol = null;
        IndexProvider index = new(() =>
        {
            try
            {
                return new ServiceIndexResourceV3(JObject.Parse("""
                    {"version":"3.0.0","resources":[{"@id":["https://resource-boundary.test/a","https://resource-boundary.test/b"],"@type":"PackageBaseAddress/3.0.0"}]}
                    """), DateTime.UnixEpoch);
            }
            catch (InvalidOperationException ex)
            {
                // NuGet's index provider wraps this token conversion failure.
                cause = ex;
                protocol = new FatalProtocolException("Unable to load service index", ex);
                throw protocol;
            }
        });

        Exception? failure = await Record.ExceptionAsync(() => InvokeAsync(Client(index), download, CancellationToken.None));

        index.Calls.Should().Be(1);
        cause.Should().NotBeNull();
        failure.Should().BeOfType<InvalidWorkloadSourceException>().Which.InnerException.Should().BeSameAs(protocol);
        failure!.GetBaseException().Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ResourceOperation_AfterSuccessfulAcquisition_DoesNotReclassifyFailures(bool download, bool cancellationFailure)
    {
        using CancellationTokenSource cancellation = new();
        Exception expected = cancellationFailure ? new OperationCanceledException(cancellation.Token)
            : new FatalProtocolException("Package operation failed", new InvalidOperationException("Not an index failure"));
        var find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<NuGetVersion>>(expected));
        find.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(expected));
        NuGetProtocolSourceClient client = new(TestRepository.Build(_source, find));

        Exception? actual = await Record.ExceptionAsync(() => InvokeAsync(client, download, cancellation.Token));

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ExactRetrieval_WithoutSearchService_CanResolveAndDownload()
    {
        var find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>([new NuGetVersion(1, 0, 0)]));
        find.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    await call.ArgAt<Stream>(2).WriteAsync(new byte[] { 1, 2, 3 }, call.ArgAt<CancellationToken>(5));
                    return true;
                });
        ServiceIndexResourceV3 index = new(JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":"https://resource-boundary.test/packages/","@type":"PackageBaseAddress/3.0.0"}]}
            """), DateTime.UnixEpoch);
        NuGetProtocolSourceClient client = new(TestRepository.Build(_source, index, find));
        var options = Options.Create(new WorkloadCatalogOptions { Source = _source.Source });
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ => client);

        ResolvedPackage? package = await catalog.ResolveVersionAsync("example", new NuGetVersion(1, 0, 0));
        package.Should().NotBeNull();
        await using Stream bytes = await catalog.DownloadAsync(package!);
        using MemoryStream copy = new();
        await bytes.CopyToAsync(copy);

        copy.ToArray().Should().Equal([1, 2, 3]);
    }

    [Theory]
    [InlineData("latest", "malformed")]
    [InlineData("range", "malformed")]
    [InlineData("channel", "malformed")]
    [InlineData("latest", "offline")]
    [InlineData("range", "offline")]
    [InlineData("channel", "offline")]
    [InlineData("latest", "installed-first")]
    [InlineData("range", "installed-first")]
    [InlineData("channel", "installed-first")]
    public async Task Setup_InstalledDependency_DistinguishesInvalidSourceOfflineAndUnusedSource(string resolution, string state)
    {
        Exception cause = state == "offline" ? new HttpRequestException("Offline") : new InvalidDataException("Invalid index");
        FatalProtocolException protocol = new("Unable to load service index", cause);
        IndexProvider index = new(() => throw protocol);
        var options = Options.Create(new WorkloadCatalogOptions { Source = _source.Source });
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ => Client(index));
        SetupDependency dependency = resolution == "channel"
            ? SetupDependency.Bundle(BundleHelpers.StableBundleId, null, null, BundleChannel.Stable)
            : SetupDependency.Host(resolution == "range" ? VersionRange.Parse("[1.0.0, 2.0.0)") : null);
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([new WorkloadEntry
        {
            PackageId = dependency.PackageId, PackageVersion = "1.0.0", Kind = WorkloadKind.Content,
        }]);
        var installer = Substitute.For<IWorkloadInstaller>();
        SetupDependencyInstaller sut = new(new TestInteractionService(), store, catalog, installer);
        SetupCommandOptions request = new(new DirectoryInfo(Path.GetTempPath()), ["host"], [], _source.Source,
            state == "installed-first" ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible,
            false, true, true, false, SetupOutputMode.Json);
        SetupDependencyResult? result = null;

        Exception? failure = await Record.ExceptionAsync(async () => result = await sut.EnsureDependencyAsync(request, dependency, CancellationToken.None));

        if (state == "malformed")
        {
            failure.Should().BeOfType<SetupConfigurationException>().Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>()
                .Which.InnerException.Should().BeSameAs(protocol);
            result.Should().BeNull();
        }
        else
        {
            failure.Should().BeNull();
            result!.Status.Should().Be(state == "offline" ? SetupDependencyStatus.SatisfiedFallback : SetupDependencyStatus.Satisfied);
        }
        index.Calls.Should().Be(state == "installed-first" ? 0 : 1);
        installer.ReceivedCalls().Should().BeEmpty();
    }

    private static NuGetProtocolSourceClient Client(IndexProvider index)
        => new(new SourceRepository(_source,
        [
            new Lazy<INuGetResourceProvider>(() => index),
            new Lazy<INuGetResourceProvider>(() => new HttpFileSystemBasedFindPackageByIdResourceProvider()),
        ]));

    private static async Task InvokeAsync(NuGetProtocolSourceClient client, bool download, CancellationToken cancellationToken)
    {
        if (download)
        {
            await using Stream stream = await client.OpenPackageAsync("example", new NuGetVersion(1, 0, 0), cancellationToken);
        }
        else
        {
            await client.ListVersionsAsync("example", cancellationToken);
        }
    }

    private sealed class IndexProvider(Func<ServiceIndexResourceV3> resolve) : INuGetResourceProvider
    {
        public int Calls { get; private set; }
        public Type ResourceType => typeof(ServiceIndexResourceV3);
        public string Name => nameof(IndexProvider);
        public IEnumerable<string> Before => [];
        public IEnumerable<string> After => [];
        public Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(Tuple.Create<bool, INuGetResource?>(true, resolve()));
        }
    }
}