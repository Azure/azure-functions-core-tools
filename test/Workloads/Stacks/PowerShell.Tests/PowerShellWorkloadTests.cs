// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

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
        Assert.IsType<PowerShellProjectInitializer>(initializer);
        Assert.Equal("powershell", initializer.Stack);
        Assert.Equal("PowerShell", Assert.Single(initializer.SupportedLanguages));
    }

    [Fact]
    public void Configure_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PowerShellWorkload().Configure(null!));
    }

    [Fact]
    public void DisplayNameAndDescription_AreSet()
    {
        PowerShellWorkload workload = new();

        Assert.Equal("PowerShell Stack", workload.DisplayName);
        Assert.Equal("Azure Functions CLI tooling for PowerShell projects.", workload.Description);
    }
}
