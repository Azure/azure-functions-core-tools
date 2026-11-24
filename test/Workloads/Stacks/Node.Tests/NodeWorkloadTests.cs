// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeWorkloadTests
{
    internal static NodeProjectStack Stack { get; } = new(new("node", "node", "Node.js"));

    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new NodeWorkload(Stack.Descriptor).Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<NodeProjectStack>());
        initializer.Stack.Should().BeSameAs(stack);
        stack.Descriptor.Should().BeSameAs(Stack.Descriptor);
        stack.SupportedLanguages.Select(l => l.LanguageIdentifier).Should().Equal("javascript", "typescript");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new NodeWorkload(Stack.Descriptor).Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new NodeWorkload(Stack.Descriptor).Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<NodeProjectFactory>());
    }
}
