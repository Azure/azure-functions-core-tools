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
    internal static JavaProjectStack Stack { get; } = new(new("java", "java", "Java"));

    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new JavaWorkload(Stack.Descriptor).Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<JavaProjectStack>());
        initializer.Stack.Should().BeSameAs(stack);
        stack.Descriptor.Should().BeSameAs(Stack.Descriptor);
        stack.SupportedLanguages.Select(l => l.LanguageIdentifier).Should().Equal("java");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JavaWorkload(Stack.Descriptor).Configure(null!));
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new JavaWorkload(Stack.Descriptor).Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<JavaProjectFactory>());
    }
}
