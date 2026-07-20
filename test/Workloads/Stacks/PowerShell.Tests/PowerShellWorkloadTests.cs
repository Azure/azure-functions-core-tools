// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellWorkloadTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        ServiceCollection services = new();
        FunctionsCliBuilder builder = Substitute.For<FunctionsCliBuilder>();
        builder.Services.Returns(services);

        new PowerShellWorkload().Configure(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        IProjectInitializer initializer = provider.GetRequiredService<IProjectInitializer>();
        initializer.Should().BeOfType<PowerShellProjectInitializer>();
        initializer.Stack.Should().Be("powershell");
        initializer.SupportedLanguages.Should().ContainSingle().Subject.Should().Be("PowerShell");
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        FluentActions.Invoking(() => new PowerShellWorkload().Configure(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void DisplayNameAndDescription_AreSet()
    {
        PowerShellWorkload workload = new();

        workload.DisplayName.Should().Be("PowerShell Stack");
        workload.Description.Should().Be("Azure Functions CLI tooling for PowerShell projects.");
    }
}
