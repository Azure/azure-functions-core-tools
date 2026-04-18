// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workload.Dotnet;
using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class DotnetTemplateProviderTests : IDisposable
{
    private readonly FakeDotnetCliRunner _runner;
    private readonly DotnetTemplateProvider _provider;
    private readonly string _tempDir;

    public DotnetTemplateProviderTests()
    {
        _runner = new FakeDotnetCliRunner();
        _provider = new DotnetTemplateProvider(_runner);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-new-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Create a fake .csproj so namespace detection works
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project />");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void WorkerRuntime_IsDotnet()
    {
        Assert.Equal("dotnet", _provider.WorkerRuntime);
    }

    [Fact]
    public async Task GetTemplatesAsync_ReturnsKnownTemplates()
    {
        var templates = await _provider.GetTemplatesAsync();

        Assert.NotEmpty(templates);
        Assert.Contains(templates, t => t.Name == "HttpTrigger");
        Assert.Contains(templates, t => t.Name == "TimerTrigger");
        Assert.Contains(templates, t => t.Name == "QueueTrigger");
        Assert.Contains(templates, t => t.Name == "BlobTrigger");
        Assert.Contains(templates, t => t.Name == "CosmosDBTrigger");
        Assert.Contains(templates, t => t.Name == "EventHubTrigger");
        Assert.Contains(templates, t => t.Name == "ServiceBusTrigger");
        Assert.Contains(templates, t => t.Name == "EventGridTrigger");
        Assert.Contains(templates, t => t.Name == "DurableFunctionsOrchestration");
    }

    [Fact]
    public async Task GetTemplatesAsync_AllTemplatesAreDotnetRuntime()
    {
        var templates = await _provider.GetTemplatesAsync();
        Assert.All(templates, t => Assert.Equal("dotnet", t.WorkerRuntime));
    }

    [Fact]
    public async Task ScaffoldAsync_InstallsItemTemplatePack()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Contains(DotnetTemplateProvider.ItemTemplatePackageId, _runner.Invocations[0].Arguments);
        Assert.Contains(BundledTemplateVersions.ItemTemplatesVersion, _runner.Invocations[0].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_HttpTrigger_UsesHttpShortName()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        var dotnetNewArgs = _runner.Invocations[1].Arguments;
        Assert.Contains("new http", dotnetNewArgs);
        Assert.Contains("--name \"MyFunction\"", dotnetNewArgs);
        Assert.Contains($"--output \"{_tempDir}\"", dotnetNewArgs);
    }

    [Fact]
    public async Task ScaffoldAsync_TimerTrigger_UsesTimerShortName()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("TimerTrigger", "MyTimer", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Contains("new timer", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_DetectsNamespaceFromCsproj()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Contains("--namespace \"MyApp\"", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_WithLanguage_PassesLanguageArg()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir, Language: "F#");
        await _provider.ScaffoldAsync(context);

        Assert.Contains("--language \"F#\"", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_UnknownTemplate_ThrowsGracefulException()
    {
        var context = new FunctionScaffoldContext("UnknownTrigger", "MyFunction", _tempDir);

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _provider.ScaffoldAsync(context));
        Assert.Contains("Unknown dotnet template", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_TemplateInstallFails_ThrowsGracefulException()
    {
        _runner.EnqueueFailure("install failed");

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _provider.ScaffoldAsync(context));
        Assert.Contains("template pack", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_DotnetNewFails_ThrowsGracefulException()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueFailure("scaffold failed");

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _provider.ScaffoldAsync(context));
        Assert.Contains("Failed to create function", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_WithForce_PassesForceFlag()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir, Force: true);
        await _provider.ScaffoldAsync(context);

        Assert.Contains("--force", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_DetectsNamespaceFromFsproj()
    {
        // Remove .csproj created in constructor, add .fsproj
        foreach (var f in Directory.GetFiles(_tempDir, "*.csproj")) File.Delete(f);
        File.WriteAllText(Path.Combine(_tempDir, "MyFSharpApp.fsproj"), "<Project />");

        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Contains("--namespace \"MyFSharpApp\"", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_NoProjectFile_UsesDirectoryName()
    {
        // Remove all project files
        foreach (var f in Directory.GetFiles(_tempDir, "*.csproj")) File.Delete(f);
        foreach (var f in Directory.GetFiles(_tempDir, "*.fsproj")) File.Delete(f);

        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        var dirName = Path.GetFileName(_tempDir);
        Assert.Contains($"--namespace \"{dirName}\"", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_TemplateInstallAlreadyInstalled_Succeeds()
    {
        // First install fails with "already installed"
        _runner.EnqueueResult(new DotnetCliResult(1, "", "Template pack is already installed"));
        _runner.EnqueueSuccess(); // dotnet new

        var context = new FunctionScaffoldContext("HttpTrigger", "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Equal(2, _runner.Invocations.Count);
        Assert.Contains("new http", _runner.Invocations[1].Arguments);
    }

    [Theory]
    [InlineData("McpToolTrigger", "mcptooltrigger")]
    [InlineData("QueueTrigger", "queue")]
    [InlineData("BlobTrigger", "blob")]
    [InlineData("CosmosDBTrigger", "cosmos")]
    [InlineData("EventHubTrigger", "eventhub")]
    [InlineData("ServiceBusTrigger", "servicebus")]
    [InlineData("EventGridTrigger", "eventgrid")]
    [InlineData("DurableFunctionsOrchestration", "durable")]
    public async Task ScaffoldAsync_AllTemplates_UseCorrectShortName(string templateName, string expectedShortName)
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new FunctionScaffoldContext(templateName, "MyFunction", _tempDir);
        await _provider.ScaffoldAsync(context);

        Assert.Contains($"new {expectedShortName}", _runner.Invocations[1].Arguments);
    }
}
