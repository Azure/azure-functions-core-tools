// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonWorkloadTests
{
    internal static PythonProjectStack Stack { get; } = new(new("python", "python", "Python"));

    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload(Stack.Descriptor).Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<PythonProjectStack>());
        initializer.Stack.Should().BeSameAs(stack);
        stack.Descriptor.Should().BeSameAs(Stack.Descriptor);
        stack.SupportedLanguages.Select(l => l.LanguageIdentifier).Should().Equal("python");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new PythonWorkload(Stack.Descriptor).Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload(Stack.Descriptor).Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<PythonProjectFactory>());
    }
}
