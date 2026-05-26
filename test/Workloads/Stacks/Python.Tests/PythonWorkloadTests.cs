// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        Assert.IsType<PythonProjectInitializer>(initializer);
        Assert.Equal("python", initializer.Stack);
        Assert.Equal("Python", Assert.Single(initializer.SupportedLanguages));
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PythonWorkload().Configure(null!));
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<PythonProjectFactory>());
    }
}
