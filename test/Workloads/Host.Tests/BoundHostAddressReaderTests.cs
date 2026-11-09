// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class BoundHostAddressReaderTests
{
    [Fact]
    public void Constructor_NullServer_ThrowsArgumentNullException()
    {
        Func<BoundHostAddressReader> act = () => new BoundHostAddressReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("server");
    }

    [Fact]
    public void ReadListeningUris_WithoutAddressFeature_ReturnsEmpty()
    {
        IServer server = Substitute.For<IServer>();
        server.Features.Returns(new FeatureCollection());
        var reader = new BoundHostAddressReader(server);

        IReadOnlyList<Uri> addresses = reader.ReadListeningUris();

        addresses.Should().BeEmpty();
    }
}
