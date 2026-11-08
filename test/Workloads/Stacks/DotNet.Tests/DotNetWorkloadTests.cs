// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
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
    public void RequiredTemplatePackages_DeclaresPinnedProjectAndItemPackages()
    {
        WorkloadTemplatePackage[] packages = [.. new DotNetWorkload().RequiredTemplatePackages];

        packages.Should().BeEquivalentTo(
        [
            new WorkloadTemplatePackage(
                "Microsoft.Azure.Functions.Worker.ProjectTemplates",
                "4.0.5708",
                Path.Combine("templates", "microsoft.azure.functions.worker.projecttemplates.4.0.5708.nupkg")),
            new WorkloadTemplatePackage(
                "Microsoft.Azure.Functions.Worker.ItemTemplates",
                "4.0.5708",
                Path.Combine("templates", "microsoft.azure.functions.worker.itemtemplates.4.0.5708.nupkg")),
        ], options => options.WithStrictOrdering());
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

        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeOfType<DotNetProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<DotNetProjectStack>());
        stack.Should().NotBeAssignableTo<IProjectInitializer>();
        stack.Stack.Should().Be(initializer.Stack);
        stack.DisplayName.Should().Be(".NET").And.Be(initializer.DisplayName);
        stack.WorkerRuntimeAliases.Should().Equal("dotnet-isolated").And.Equal(initializer.WorkerRuntimeAliases);
        stack.SupportedLanguages.Should().Equal(initializer.SupportedLanguages);
        stack.SupportedLanguageAliases.Should().BeSameAs(initializer.SupportedLanguageAliases);
        stack.SupportedLanguageAliases["C#"].Should().Equal("csharp");
        stack.SupportedLanguageAliases["F#"].Should().Equal("fsharp");
    }

    [Fact]
    public void Configure_RegistersStartOptionContributor()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IStartHostOptionContributor contributor = provider.GetRequiredService<IStartHostOptionContributor>();
        contributor.Should().BeOfType<DotNetStartOptionContributor>();
        contributor.Stack.Should().Be("dotnet");
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
