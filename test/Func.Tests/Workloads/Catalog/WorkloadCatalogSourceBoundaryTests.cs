// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class WorkloadCatalogSourceBoundaryTests
{
    [Theory]
    [InlineData("argument")]
    [InlineData("invalid-operation")]
    public async Task Discovery_ClientFactoryProgrammerFailure_PropagatesOriginalExceptionWithoutCaching(string failure)
    {
        Exception exception = failure switch
        {
            "argument" => new ArgumentException("client factory bug"),
            "invalid-operation" => new InvalidOperationException("client factory bug"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var options = Options.Create(new WorkloadCatalogOptions { Source = "https://example.test/v3/index.json" });
        int clientCalls = 0;
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ =>
        {
            clientCalls++;
            throw exception;
        });
        SetupStackCatalog discovery = new(catalog);

        Exception? first = await Record.ExceptionAsync(() => discovery.GetStacksAsync(null, false, CancellationToken.None));
        Exception? second = await Record.ExceptionAsync(() => discovery.GetStacksAsync(null, false, CancellationToken.None));

        first.Should().BeSameAs(exception);
        second.Should().BeSameAs(exception);
        clientCalls.Should().Be(2, "programmer errors must neither become cached fallback snapshots nor cached failures");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    [InlineData("protocol")]
    public async Task Discovery_ClientFactoryTransportFailure_CachesBuiltInFallback(string failure)
    {
        Exception exception = failure switch
        {
            "http" => new HttpRequestException("offline"),
            "io" => new IOException("offline"),
            "protocol" => new FatalProtocolException("invalid response"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var options = Options.Create(new WorkloadCatalogOptions { Source = "https://example.test/v3/index.json" });
        int clientCalls = 0;
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), _ =>
        {
            clientCalls++;
            throw exception;
        });
        SetupStackCatalog discovery = new(catalog);

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        SetupStackSnapshot cached = await discovery.GetStacksAsync(null, false, CancellationToken.None);

        snapshot.Should().BeSameAs(SetupDependency.BuiltInStackSnapshot);
        cached.Should().BeSameAs(snapshot);
        clientCalls.Should().Be(1);
    }
}