// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetWorkloadTests
{
    internal static DotNetProjectStack Stack { get; } = new(new("dotnet", "dotnet-isolated", ".NET"));

    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload(Stack.Descriptor).Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<DotNetProjectStack>());
        initializer.Stack.Should().BeSameAs(stack);
        stack.Descriptor.Should().BeSameAs(Stack.Descriptor);
        stack.SupportedLanguages.Select(l => l.LanguageIdentifier).Should().Equal("csharp", "fsharp");
    }

    [Fact]
    public void Configure_RegistersStartOptionContributor()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload(Stack.Descriptor).Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IStartHostOptionContributor contributor = provider.GetRequiredService<IStartHostOptionContributor>();
        contributor.Should().BeOfType<DotNetStartOptionContributor>();
        contributor.Stack.Should().BeSameAs(provider.GetRequiredService<IProjectStack>());
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new DotNetWorkload(Stack.Descriptor).Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new DotNetWorkload(Stack.Descriptor).Configure(builder);

        builder.Received(1).AddProjectFactory<DotNetProjectFactory>();
    }
}
