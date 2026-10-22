// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        initializer.Should().BeOfType<PythonProjectInitializer>();
        initializer.Stack.Should().Be("python");
        initializer.SupportedLanguages.Should().ContainSingle().Subject.Should().Be("Python");

        IProjectStack stack = provider.GetRequiredService<IProjectStack>();
        stack.Should().BeOfType<PythonProjectStack>();
        stack.Should().BeSameAs(provider.GetRequiredService<PythonProjectStack>());
        stack.Should().NotBeAssignableTo<IProjectInitializer>();
        stack.Stack.Should().Be(initializer.Stack);
        stack.DisplayName.Should().Be("Python").And.Be(initializer.DisplayName);
        stack.WorkerRuntimeAliases.Should().BeEmpty().And.Equal(initializer.WorkerRuntimeAliases);
        stack.SupportedLanguages.Should().Equal(initializer.SupportedLanguages);
        stack.SupportedLanguageAliases.Should().BeSameAs(initializer.SupportedLanguageAliases);
        stack.SupportedLanguageAliases["Python"].Should().Equal("py");
        provider.GetRequiredService<PythonTemplateApplicationPlanner>().Should().NotBeNull();
        provider.GetRequiredService<IPythonTemplateAppendExecutor>().Should().BeOfType<PythonTemplateAppendExecutor>();
        provider.GetRequiredService<PythonTemplateSourceReader>().Should().NotBeNull();
        provider.GetRequiredService<PythonAppendTargetGuard>().Should().NotBeNull();
        builder.Received(1).AddNativeTemplatePolicy<PythonTemplatePolicy>();
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new PythonWorkload().Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Configure_AddsProjectFactory()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PythonWorkload().Configure(builder);

        builder.Received(1).AddProjectFactory(Arg.Any<PythonProjectFactory>());
    }
}
