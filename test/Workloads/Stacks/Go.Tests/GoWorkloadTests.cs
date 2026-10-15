// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Go.Tests;

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
        initializer.Should().BeOfType<GoProjectInitializer>();
        initializer.Stack.Should().Be("go");
        initializer.SupportedLanguages.Should().ContainSingle().Subject.Should().Be("Go");

        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeOfType<GoProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<GoProjectStack>());
        stack.Should().NotBeAssignableTo<IProjectInitializer>();
        stack.Stack.Should().Be(initializer.Stack);
        stack.DisplayName.Should().Be("Go").And.Be(initializer.DisplayName);
        stack.WorkerRuntimeAliases.Should().Equal("native").And.Equal(initializer.WorkerRuntimeAliases);
        stack.SupportedLanguages.Should().Equal(initializer.SupportedLanguages);
        stack.SupportedLanguageAliases.Should().BeSameAs(initializer.SupportedLanguageAliases);
        stack.SupportedLanguageAliases["Go"].Should().Equal("golang");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new GoWorkload().Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new GoWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<GoProjectFactory>());
    }
}
