// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Dotnet;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

/// <summary>
/// Tests the DotnetWorkload as a standalone workload package — verifying
/// it correctly implements the IWorkload contract and provides initializers/providers.
/// </summary>
public class DotnetWorkloadContractTests
{
    private readonly DotnetWorkload _workload;

    public DotnetWorkloadContractTests()
    {
        _workload = new DotnetWorkload(new FakeDotnetCliRunner());
    }

    [Fact]
    public void Id_IsDotnet()
    {
        Assert.Equal("dotnet", _workload.Id);
    }

    [Fact]
    public void GetProjectInitializer_ReturnsInitializerForDotnetRuntime()
    {
        var initializer = _workload.GetProjectInitializer();
        Assert.NotNull(initializer);
        Assert.Equal("dotnet", initializer.WorkerRuntime);
        Assert.True(initializer.CanHandle("dotnet"));
        Assert.True(initializer.CanHandle("dotnet-isolated"));
    }

    [Fact]
    public void GetTemplateProviders_ReturnsSingleDotnetProvider()
    {
        var providers = _workload.GetTemplateProviders();
        Assert.Single(providers);
        Assert.Equal("dotnet", providers[0].WorkerRuntime);
    }

    [Fact]
    public void ProjectInitializer_SupportsCSharpAndFSharp()
    {
        var initializer = _workload.GetProjectInitializer()!;
        Assert.Contains("C#", initializer.SupportedLanguages);
        Assert.Contains("F#", initializer.SupportedLanguages);
    }

    [Fact]
    public void ProjectInitializer_ContributesTargetFrameworkOption()
    {
        var initializer = _workload.GetProjectInitializer()!;
        var options = initializer.GetInitOptions();
        Assert.Single(options);
        Assert.Equal("--target-framework", options[0].Name);
    }

    [Fact]
    public async Task TemplateProvider_ListsKnownTriggerTemplates()
    {
        var provider = _workload.GetTemplateProviders()[0];
        var templates = await provider.GetTemplatesAsync();

        Assert.NotEmpty(templates);
        Assert.Contains(templates, t => t.Name == "HttpTrigger");
        Assert.Contains(templates, t => t.Name == "TimerTrigger");
        Assert.Contains(templates, t => t.Name == "DurableFunctionsOrchestration");
        Assert.All(templates, t => Assert.Equal("dotnet", t.WorkerRuntime));
    }
}
