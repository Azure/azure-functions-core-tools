// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.ExtensionBundles.Tests;

public class ExtensionBundlesWorkloadTests
{
    [Fact]
    public void DisplayName_IsSet()
    {
        Assert.False(string.IsNullOrWhiteSpace(new ExtensionBundlesWorkload().DisplayName));
    }

    [Fact]
    public void Description_IsSet()
    {
        Assert.False(string.IsNullOrWhiteSpace(new ExtensionBundlesWorkload().Description));
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ExtensionBundlesWorkload().Configure(null!));
    }

    [Fact]
    public void Configure_RegistersNoServicesYet()
    {
        // Scaffolding-only: Configure is intentionally a no-op until the
        // IExtensionBundleProvider abstraction lands in the follow-up PR.
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new ExtensionBundlesWorkload().Configure(builder);

        Assert.Empty(services);
    }
}
