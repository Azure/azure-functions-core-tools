// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Dotnet;
using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class DotnetWorkloadTests
{
    [Fact]
    public void Id_IsDotnet()
    {
        var runner = new FakeDotnetCliRunner();
        var workload = new DotnetWorkload(runner);
        Assert.Equal("dotnet", workload.Id);
    }

    [Fact]
    public void GetProjectInitializer_ReturnsInitializer()
    {
        var runner = new FakeDotnetCliRunner();
        var workload = new DotnetWorkload(runner);

        var initializer = workload.GetProjectInitializer();
        Assert.NotNull(initializer);
        Assert.Equal("dotnet", initializer.WorkerRuntime);
    }

    [Fact]
    public void GetTemplateProviders_ReturnsSingleProvider()
    {
        var runner = new FakeDotnetCliRunner();
        var workload = new DotnetWorkload(runner);

        var providers = workload.GetTemplateProviders();
        Assert.Single(providers);
        Assert.Equal("dotnet", providers[0].WorkerRuntime);
    }

    [Fact]
    public void SupportedLanguages_ContainsCSharpAndFSharp()
    {
        var runner = new FakeDotnetCliRunner();
        var workload = new DotnetWorkload(runner);

        var initializer = workload.GetProjectInitializer()!;
        Assert.Contains("C#", initializer.SupportedLanguages);
        Assert.Contains("F#", initializer.SupportedLanguages);
    }

    [Fact]
    public void GetPackProvider_ReturnsProvider()
    {
        var runner = new FakeDotnetCliRunner();
        var workload = new DotnetWorkload(runner);

        var packProvider = workload.GetPackProvider();
        Assert.NotNull(packProvider);
        Assert.Equal("dotnet", packProvider.WorkerRuntime);
    }

    [Fact]
    public void Name_IsHumanReadable()
    {
        var workload = new DotnetWorkload(new FakeDotnetCliRunner());
        Assert.Equal(".NET (Isolated Worker)", workload.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var workload = new DotnetWorkload(new FakeDotnetCliRunner());
        Assert.NotEmpty(workload.Description);
        Assert.Contains(".NET", workload.Description);
    }

    [Fact]
    public void RegisterCommands_DoesNotThrow()
    {
        var workload = new DotnetWorkload(new FakeDotnetCliRunner());
        var command = new System.CommandLine.Command("root");

        // Should be a no-op but must not throw
        workload.RegisterCommands(command);
    }

    [Fact]
    public void DefaultConstructor_CreatesWorkingInstance()
    {
        // Exercises the parameterless ctor that creates a real DotnetCliRunner
        var workload = new DotnetWorkload();
        Assert.Equal("dotnet", workload.Id);
        Assert.NotNull(workload.GetProjectInitializer());
        Assert.NotNull(workload.GetPackProvider());
        Assert.Single(workload.GetTemplateProviders());
    }
}
