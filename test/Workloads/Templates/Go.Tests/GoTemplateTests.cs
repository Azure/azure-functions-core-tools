// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.Go.Tests;

public sealed class GoTemplateTests : IAsyncLifetime
{
    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("func-go", "project", "Azure.Functions.Cli.Go.Project")]
    [InlineData("httptrigger", "item", "Azure.Functions.Cli.Go.HttpTrigger")]
    [InlineData("timertrigger", "item", "Azure.Functions.Cli.Go.TimerTrigger")]
    public async Task Templates_AreDiscoveredFromWorkload(string shortName, string type, string identity)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        template.Identity.Should().Be(identity);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be("Go");
        template.TagsCollection["stack"].Should().Be("go");
    }

    [Theory]
    [InlineData("project", "ModulePath", "module")]
    [InlineData("project", "NoHttp", "no-http")]
    [InlineData("httptrigger", "AuthLevel", "auth-level")]
    [InlineData("timertrigger", "Schedule", "schedule")]
    public void TemplateHostMetadata_ExposesOptionAliases(string template, string symbol, string alias)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "templates", template, ".template.config", "func.host.json");
        using var metadata = JsonDocument.Parse(File.ReadAllText(path));

        metadata.RootElement.GetProperty("symbolInfo").GetProperty(symbol).GetProperty("longName").GetString().Should().Be(alias);
    }

    [Theory]
    [InlineData(false, null, "sample-app")]
    [InlineData(true, "example.com/team/functions", "example.com/team/functions")]
    public async Task Project_Options_GenerateUsableProject(bool noHttp, string? modulePath, string expectedModule)
    {
        ITemplateInfo template = await GetTemplateAsync("func-go");
        Dictionary<string, string?> parameters = [];
        if (noHttp)
        {
            parameters["NoHttp"] = "true";
        }

        if (modulePath is not null)
        {
            parameters["ModulePath"] = modulePath;
        }

        ITemplateCreationResult result = await GenerateAsync(template, "Sample.App", parameters);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        File.ReadAllText(Path.Combine(output, "go.mod")).Should().ContainAll(
            $"module {expectedModule}", "go 1.24.0", "github.com/azure/azure-functions-golang-worker v1.0.3");
        File.ReadAllText(Path.Combine(output, "go.sum")).Should().Contain("github.com/azure/azure-functions-golang-worker v1.0.3 h1:");
        File.ReadAllText(Path.Combine(output, "main.go")).Should().ContainAll("var app = sdk.FunctionApp()", "worker.Start(app)");
        File.ReadAllText(Path.Combine(output, "README.md")).Should().Contain("Sample.App");
        File.Exists(Path.Combine(output, "HelloWorld_function.go")).Should().Be(!noHttp);
        File.Exists(Path.Combine(output, ".funcignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, "funcignore.txt")).Should().BeFalse();
        File.Exists(Path.Combine(output, "gitignore.txt")).Should().BeFalse();
        string[] expectedFiles = [".funcignore", ".gitignore", "go.mod", "go.sum", "host.json", "local.settings.json", "main.go", "README.md"];
        if (!noHttp)
        {
            expectedFiles = [.. expectedFiles, "HelloWorld_function.go"];
        }

        Directory.GetFiles(output).Select(Path.GetFileName).Should().BeEquivalentTo(expectedFiles);
        Directory.GetDirectories(output).Should().BeEmpty();
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("go.mod");

        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.GetProperty("extensionBundle").GetProperty("version").GetString().Should().Be("[4.*, 5.0.0)");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString().Should().Be("native");
        values.GetProperty("FUNCTIONS_CLI_NATIVE_LANGUAGE").GetString().Should().Be("go");
        values.GetProperty("AzureWebJobsStorage").GetString().Should().Be("UseDevelopmentStorage=true");
        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData(null, "function", CreationResultStatus.Success)]
    [InlineData("anonymous", "anonymous", CreationResultStatus.Success)]
    [InlineData("admin", "admin", CreationResultStatus.Success)]
    [InlineData("invalid", null, CreationResultStatus.InvalidParamValues)]
    public async Task HttpTrigger_Options_ValidateAndGenerateFunction(string? authLevel, string? expectedAuth, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("httptrigger");
        Dictionary<string, string?> parameters = [];
        if (authLevel is not null)
        {
            parameters["AuthLevel"] = authLevel;
        }

        ITemplateCreationResult result = await GenerateAsync(template, "Get-Orders", parameters);

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            string source = File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "Get_Orders_function.go"));
            source.Should().ContainAll("app.HTTP(\"Get_Orders\", handleGet_Orders", $"sdk.WithAuth(\"{expectedAuth}\")", "http.MethodGet");
            result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("Get_Orders_function.go");
            AssertNoTemplateArtifacts(result.OutputBaseDirectory!);
        }
    }

    [Theory]
    [InlineData(null, "0 */5 * * * *")]
    [InlineData("%CleanupSchedule%", "%CleanupSchedule%")]
    [InlineData("0 \"\\test\"\n", "0 \\\"\\\\test\\\"\\n")]
    public async Task TimerTrigger_Schedule_GeneratesEscapedFunction(string? schedule, string expectedSchedule)
    {
        ITemplateInfo template = await GetTemplateAsync("timertrigger");
        Dictionary<string, string?> parameters = [];
        if (schedule is not null)
        {
            parameters["Schedule"] = schedule;
        }

        ITemplateCreationResult result = await GenerateAsync(template, "1-Cleanup", parameters);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string source = File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "Function1_Cleanup_function.go"));
        source.Should().ContainAll(
            "app.Timer(\"Function1_Cleanup\", handleFunction1_Cleanup", $"sdk.WithSchedule(\"{expectedSchedule}\")", "bindings.TimerInfo");
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("Function1_Cleanup_function.go");
        AssertNoTemplateArtifacts(result.OutputBaseDirectory!);
    }

    [Fact]
    public async Task ProjectAndItems_CoexistWithoutOverwritingFunctions()
    {
        ITemplateInfo project = await GetTemplateAsync("func-go");
        ITemplateInfo http = await GetTemplateAsync("httptrigger");
        ITemplateInfo timer = await GetTemplateAsync("timertrigger");
        ITemplateCreationResult projectResult = await GenerateAsync(project, "App");
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        string output = projectResult.OutputBaseDirectory!;
        string main = File.ReadAllText(Path.Combine(output, "main.go"));
        string hello = File.ReadAllText(Path.Combine(output, "HelloWorld_function.go"));

        ITemplateCreationResult first = await GenerateAsync(http, "Orders", outputDirectory: output);
        ITemplateCreationResult second = await GenerateAsync(timer, "Cleanup", outputDirectory: output);
        ITemplateCreationResult third = await GenerateAsync(http, "Health", outputDirectory: output);
        ITemplateCreationResult duplicate = await GenerateAsync(timer, "Orders", outputDirectory: output);

        first.Status.Should().Be(CreationResultStatus.Success, first.ErrorMessage);
        second.Status.Should().Be(CreationResultStatus.Success, second.ErrorMessage);
        third.Status.Should().Be(CreationResultStatus.Success, third.ErrorMessage);
        duplicate.Status.Should().Be(CreationResultStatus.DestructiveChangesDetected);
        File.ReadAllText(Path.Combine(output, "Orders_function.go")).Should().Contain("app.HTTP(\"Orders\"");
        File.ReadAllText(Path.Combine(output, "Cleanup_function.go")).Should().Contain("app.Timer(\"Cleanup\"");
        File.ReadAllText(Path.Combine(output, "Health_function.go")).Should().Contain("app.HTTP(\"Health\"");
        File.ReadAllText(Path.Combine(output, "main.go")).Should().Be(main);
        File.ReadAllText(Path.Combine(output, "HelloWorld_function.go")).Should().Be(hello);
        AssertNoTemplateArtifacts(output);
    }

    [Fact]
    public async Task Item_Force_OverwritesExistingFunction()
    {
        ITemplateInfo http = await GetTemplateAsync("httptrigger");
        ITemplateInfo timer = await GetTemplateAsync("timertrigger");
        ITemplateCreationResult first = await GenerateAsync(http, "Orders");
        first.Status.Should().Be(CreationResultStatus.Success, first.ErrorMessage);

        ITemplateCreationResult forced = await _host.InstantiateAsync(
            timer, "Orders", outputDirectory: first.OutputBaseDirectory, forceCreation: true,
            cancellationToken: TestContext.Current.CancellationToken);

        forced.Status.Should().Be(CreationResultStatus.Success, forced.ErrorMessage);
        File.ReadAllText(Path.Combine(first.OutputBaseDirectory!, "Orders_function.go")).Should().Contain("app.Timer(\"Orders\"");
    }

    private Task<ITemplateInfo> GetTemplateAsync(string shortName)
        => _host.GetTemplateAsync(shortName, TestContext.Current.CancellationToken);

    private Task<ITemplateCreationResult> GenerateAsync(
        ITemplateInfo template, string name, IReadOnlyDictionary<string, string?>? parameters = null, string? outputDirectory = null)
        => _host.InstantiateAsync(template, name, parameters, outputDirectory, cancellationToken: TestContext.Current.CancellationToken);

    private static void AssertNoTemplateArtifacts(string output)
    {
        Directory.Exists(Path.Combine(output, ".template.config")).Should().BeFalse();
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotContainAny(
                "FunctionsProject", "FunctionsModule", "TemplateFunctionName", "TemplateFunctionFile", "__AUTH_LEVEL__", "__TIMER_SCHEDULE__");
        }
    }
}
