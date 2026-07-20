// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetWorkloadTests
{
    [Fact]
    public void DisplayName_ReturnsDotNetStack()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        workload.DisplayName.Should().Be(".NET Stack");
    }

    [Fact]
    public void Description_ReturnsCorrectValue()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        workload.Description.Should().Be("Azure Functions CLI tooling for .NET (C#) projects.");
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
        initializer.Should().BeOfType<DotNetProjectInitializer>();
        initializer.Stack.Should().Be("dotnet");
        initializer.SupportedLanguages.Should().Equal(["C#", "F#"]);
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new DotNetWorkload().Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
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
