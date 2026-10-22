// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads.Node.Native;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new NodeWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        initializer.Should().BeOfType<NodeProjectInitializer>();
        initializer.Stack.Should().Be("node");
        initializer.SupportedLanguages.Should().Equal(["JavaScript", "TypeScript"]);

        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeOfType<NodeProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<NodeProjectStack>());
        stack.Should().NotBeAssignableTo<IProjectInitializer>();
        stack.Stack.Should().Be(initializer.Stack);
        stack.DisplayName.Should().Be("Node.js").And.Be(initializer.DisplayName);
        stack.WorkerRuntimeAliases.Should().BeEmpty().And.Equal(initializer.WorkerRuntimeAliases);
        stack.SupportedLanguages.Should().Equal(initializer.SupportedLanguages);
        stack.SupportedLanguageAliases.Should().BeSameAs(initializer.SupportedLanguageAliases);
        stack.SupportedLanguageAliases["JavaScript"].Should().Equal("js");
        stack.SupportedLanguageAliases["TypeScript"].Should().Equal("ts");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new NodeWorkload().Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactoryAndNativeItemPolicy()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new NodeWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<NodeProjectFactory>());
        builder.Received(1).AddNativeTemplatePolicy<NodeNativeTemplatePolicy>();
    }
}
