// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class HostConfigurationBuilderTests
{
    [Fact]
    public void AllowedKeys_IncludesWorkloadsHome()
    {
        // Locks in the contract: every key that participates in host
        // configuration must appear on this list. If a future change tries
        // to add a JSON file or another env-var pattern, this test fails
        // and forces a re-review of the trust boundary.
        Assert.Contains(Constants.WorkloadsHomeEnvironmentVariable, HostConfigurationBuilder.AllowedKeys);
    }

    [Fact]
    public void Build_OnlyExposesAllowlistedKeys()
    {
        // The root children of the returned IHostConfiguration must be the
        // exact allowlist; nothing else can ever leak in through a stray
        // config provider.
        IHostConfiguration host = HostConfigurationBuilder.Build();

        var keys = host.GetChildren().Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new HashSet<string>(HostConfigurationBuilder.AllowedKeys, StringComparer.Ordinal), keys);
    }
}
