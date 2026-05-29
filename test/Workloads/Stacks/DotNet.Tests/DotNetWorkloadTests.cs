// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetWorkloadTests
{
    [Fact]
    public void DisplayName_ReturnsDotNetDevelopmentStack()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal(".NET Development Stack", workload.DisplayName);
    }

    [Fact]
    public void Description_ReturnsCorrectValue()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal("Azure Functions CLI tooling for .NET (C#) projects.", workload.Description);
    }

    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        Assert.IsType<DotNetProjectInitializer>(initializer);
        Assert.Equal("dotnet", initializer.Stack);
        Assert.Equal(["C#", "F#"], initializer.SupportedLanguages);
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DotNetWorkload().Configure(null!));
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory<DotNetProjectFactory>();
    }
}
