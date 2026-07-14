// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Java.Tests;

public class JavaWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new JavaWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        Assert.IsType<JavaProjectInitializer>(initializer);
        Assert.Equal("java", initializer.Stack);
        Assert.Equal("Java", Assert.Single(initializer.SupportedLanguages));
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JavaWorkload().Configure(null!));
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new JavaWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<JavaProjectFactory>());
    }

    [Fact]
    public void DisplayName_And_Description_AreSet()
    {
        var workload = new JavaWorkload();

        Assert.Equal("Java Stack", workload.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(workload.Description));
    }
}
