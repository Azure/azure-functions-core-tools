// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public sealed class WorkloadSearchEnrichmentTests
{
    [Theory]
    [InlineData("malformed", false)]
    [InlineData("malformed", true)]
    [InlineData("offline", false)]
    [InlineData("offline", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("healthy", false)]
    [InlineData("healthy", true)]
    public async Task Search_SuccessfulPrimaryThenEnrichment_PreservesFailurePolicy(string state, bool json)
    {
        using CancellationTokenSource cancellation = new();
        InvalidDataException cause = new("Invalid service index schema");
        FatalProtocolException malformed = new("Unable to load service index", cause);
        Exception failure = state switch
        {
            "malformed" => malformed,
            "cancel" => new OperationCanceledException(cancellation.Token),
            _ => new HttpRequestException("Offline"),
        };
        FailingIndexProvider index = new(failure);
        var options = Options.Create(new WorkloadCatalogOptions { Source = "https://enrichment.test/v3/index.json", IncludePrerelease = false });
        SearchClient? primary = null;
        int clients = 0;
        var versions = Substitute.For<FindPackageByIdResource>();
        versions.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>([new NuGetVersion("1.0.0"), new NuGetVersion("2.0.0-preview.1")]));
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), source =>
        {
            if (++clients == 1)
            {
                ServiceIndexResourceV3 searchIndex = new(JObject.Parse("""
                    {"version":"3.0.0","resources":[{"@id":"https://enrichment.test/query","@type":"SearchQueryService"}]}
                    """), DateTime.UnixEpoch);
                return primary = new SearchClient(TestRepository.Build(source, searchIndex));
            }

            // A fresh repository isolates enrichment acquisition from the successful primary search.
            // This models the command boundary, not NuGet HTTP-cache refresh timing.
            return state == "healthy" ? new NuGetProtocolSourceClient(TestRepository.Build(source, versions))
                : new NuGetProtocolSourceClient(new SourceRepository(source,
                [
                    new Lazy<INuGetResourceProvider>(() => index),
                    new Lazy<INuGetResourceProvider>(() => new HttpFileSystemBasedFindPackageByIdResourceProvider()),
                ]));
        });
        TestInteractionService interaction = new();
        WorkloadSearchCommand command = new(interaction, catalog, options);
        string[] args = json ? ["--prerelease", "--json"] : ["--prerelease"];
        int? exit = null;

        Exception? error = await Record.ExceptionAsync(async () => exit = await command.Parse(args).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        primary.Should().NotBeNull();
        primary!.Searches.Should().Be(1);
        clients.Should().Be(2);
        index.Calls.Should().Be(state == "healthy" ? 0 : 1);
        if (state == "malformed")
        {
            GracefulException graceful = error.Should().BeOfType<GracefulException>().Which;
            graceful.IsUserError.Should().BeTrue();
            graceful.Message.Should().Contain("malformed V3 service index").And.Contain("--source");
            graceful.InnerException.Should().BeOfType<InvalidWorkloadSourceException>().Which.InnerException.Should().BeSameAs(malformed);
            graceful.GetBaseException().Should().BeSameAs(cause);
            exit.Should().BeNull();
        }
        else if (state == "cancel")
        {
            error.Should().BeSameAs(failure);
            exit.Should().BeNull();
        }
        else
        {
            error.Should().BeNull();
            exit.Should().Be(0);
            interaction.AllOutput.Should().Contain("2.0.0-preview.1");
            if (state == "healthy") interaction.AllOutput.Should().Contain("1.0.0");
            else interaction.AllOutput.Should().NotContain("1.0.0");
            interaction.Lines.Count(line => line.StartsWith("JSON:", StringComparison.Ordinal)).Should().Be(json ? 1 : 0);
        }

        if (state is "malformed" or "cancel")
        {
            interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal)
                || line.StartsWith("Version:", StringComparison.Ordinal) || line.StartsWith("SUCCESS:", StringComparison.Ordinal)
                || line.Contains("Showing", StringComparison.Ordinal));
        }
    }

    private sealed class SearchClient(SourceRepository repository) : NuGetProtocolSourceClient(repository)
    {
        public int Searches { get; private set; }
        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Searches++;
            return Task.FromResult<JObject?>(JObject.Parse("""
                {"data":[{"id":"contoso.node","version":"2.0.0-preview.1","tags":"kind:workload alias:node","packageTypes":[{"name":"FuncCliWorkload"}]}]}
                """));
        }
    }

    private sealed class FailingIndexProvider(Exception failure) : INuGetResourceProvider
    {
        public int Calls { get; private set; }
        public Type ResourceType => typeof(ServiceIndexResourceV3);
        public string Name => nameof(FailingIndexProvider);
        public IEnumerable<string> Before => [];
        public IEnumerable<string> After => [];
        public Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Calls++;
            return Task.FromException<Tuple<bool, INuGetResource?>>(failure);
        }
    }
}