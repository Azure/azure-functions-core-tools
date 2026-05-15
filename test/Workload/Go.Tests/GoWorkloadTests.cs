// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workload.Go.Tests;

public class GoWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new GoWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        Assert.IsType<GoProjectInitializer>(initializer);
        Assert.Equal("go", initializer.Stack);
        Assert.Equal("Go", Assert.Single(initializer.SupportedLanguages));
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GoWorkload().Configure(null!));
    }
}
