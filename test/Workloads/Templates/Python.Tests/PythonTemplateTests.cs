// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.Python.Tests;

public sealed class PythonTemplateTests : IAsyncLifetime
{
    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("func-python", "project", "Azure.Functions.Cli.Python.Project.Python")]
    [InlineData("httptrigger", "item", "Azure.Functions.Cli.Python.HttpTrigger.Python")]
    [InlineData("timertrigger", "item", "Azure.Functions.Cli.Python.TimerTrigger.Python")]
    public async Task Templates_AreDiscoveredFromWorkload(string shortName, string type, string identity)
    {
        ITemplateInfo template = await _host.GetTemplateAsync(shortName, cancellationToken: TestContext.Current.CancellationToken);

        template.Identity.Should().Be(identity);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be("Python");
        template.TagsCollection["stack"].Should().Be("python");
    }

    [Fact]
    public async Task Project_GeneratesUsableProjectWithoutTemplateMetadata()
    {
        ITemplateInfo template = await _host.GetTemplateAsync("func-python", cancellationToken: TestContext.Current.CancellationToken);

        ITemplateCreationResult result = await _host.InstantiateAsync(
            template, "SampleProject", cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("function_app.py");
        File.ReadAllText(Path.Combine(output, "function_app.py")).Should().Contain("app.register_functions(module.bp)");
        File.ReadAllText(Path.Combine(output, "requirements.txt")).Should().Contain("azure-functions");
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeFalse();
        File.Exists(Path.Combine(output, "functions", "__init__.py")).Should().BeTrue();
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.GetProperty("extensionBundle").GetProperty("id").GetString().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out _).Should().BeFalse();
        values.GetProperty("AzureWebJobsStorage").GetString().Should().Be("UseDevelopmentStorage=true");
        AssertCleanOutput(output);
    }

    [Fact]
    public async Task Project_DefaultExclusions_OmitBuildAndUserFiles()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "content", "templates", "project");
        string packageRoot = Path.Combine(_host.Root, "templates");
        string projectRoot = Path.Combine(packageRoot, "project");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(projectRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        string[] excludedFiles =
        [
            Path.Combine("bin", "artifact.dll"),
            Path.Combine("obj", "generated.txt"),
            "settings.user",
            "packages.lock.json",
        ];
        foreach (string relativePath in excludedFiles)
        {
            string path = Path.Combine(projectRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "excluded");
        }

        await using var host = new TemplateTestHost(packageRoot);
        ITemplateInfo template = await host.GetTemplateAsync("func-python", cancellationToken: TestContext.Current.CancellationToken);

        ITemplateCreationResult result = await host.InstantiateAsync(
            template, "SampleProject", cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        foreach (string relativePath in excludedFiles)
        {
            File.Exists(Path.Combine(result.OutputBaseDirectory!, relativePath)).Should().BeFalse();
        }

        Directory.Exists(Path.Combine(result.OutputBaseDirectory!, ".template.config")).Should().BeFalse();
    }

    [Theory]
    [InlineData("anonymous", "ANONYMOUS")]
    [InlineData("function", "FUNCTION")]
    [InlineData("admin", "ADMIN")]
    public async Task HttpTrigger_AuthorizationLevel_GeneratesNamedBlueprint(string authorization, string expected)
    {
        ITemplateInfo template = await _host.GetTemplateAsync("httptrigger", cancellationToken: TestContext.Current.CancellationToken);

        ITemplateCreationResult result = await _host.InstantiateAsync(
            template, "Order-API", new Dictionary<string, string?> { ["AuthorizationLevel"] = authorization },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/')).Should().Equal("functions/function_Order_API.py");
        string source = File.ReadAllText(Path.Combine(output, "functions", "function_Order_API.py"));
        source.Should().ContainAll("name=\"function_Order_API\"", "route=\"function_Order_API\"", $"func.AuthLevel.{expected}");
        AssertCleanOutput(output);
    }

    [Fact]
    public async Task HttpTrigger_InvalidAuthorizationLevel_IsRejected()
    {
        ITemplateInfo template = await _host.GetTemplateAsync("httptrigger", cancellationToken: TestContext.Current.CancellationToken);

        ITemplateCreationResult result = await _host.InstantiateAsync(
            template, "Example", new Dictionary<string, string?> { ["AuthorizationLevel"] = "invalid" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.InvalidParamValues);
    }

    [Fact]
    public async Task HttpTrigger_AuthorizationLevel_DeclaresHostAliases()
    {
        ITemplateInfo template = await _host.GetTemplateAsync("httptrigger", cancellationToken: TestContext.Current.CancellationToken);

        template.HostConfigPlace.Should().EndWith("func.host.json");
        string hostPath = Path.Combine(AppContext.BaseDirectory, "content", "templates", "httptrigger", ".template.config", "func.host.json");
        using var host = JsonDocument.Parse(File.ReadAllText(hostPath));
        JsonElement symbol = host.RootElement.GetProperty("symbolInfo").GetProperty("AuthorizationLevel");
        symbol.GetProperty("longName").GetString().Should().Be("auth-level");
        symbol.GetProperty("shortName").GetString().Should().Be("a");
    }

    [Fact]
    public async Task Items_CoexistInGeneratedProjectWithoutReplacingApplication()
    {
        ITemplateInfo project = await _host.GetTemplateAsync("func-python", cancellationToken: TestContext.Current.CancellationToken);
        ITemplateInfo http = await _host.GetTemplateAsync("httptrigger", cancellationToken: TestContext.Current.CancellationToken);
        ITemplateInfo timer = await _host.GetTemplateAsync("timertrigger", cancellationToken: TestContext.Current.CancellationToken);
        ITemplateCreationResult projectResult = await _host.InstantiateAsync(
            project, "SampleProject", cancellationToken: TestContext.Current.CancellationToken);
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        string output = projectResult.OutputBaseDirectory!;
        string app = File.ReadAllText(Path.Combine(output, "function_app.py"));

        ITemplateCreationResult httpResult = await _host.InstantiateAsync(
            http, "GetOrders", outputDirectory: output, cancellationToken: TestContext.Current.CancellationToken);
        ITemplateCreationResult timerResult = await _host.InstantiateAsync(
            timer, "Cleanup", outputDirectory: output, cancellationToken: TestContext.Current.CancellationToken);

        httpResult.Status.Should().Be(CreationResultStatus.Success, httpResult.ErrorMessage);
        timerResult.Status.Should().Be(CreationResultStatus.Success, timerResult.ErrorMessage);
        timerResult.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/')).Should().Equal("functions/function_Cleanup.py");
        File.ReadAllText(Path.Combine(output, "function_app.py")).Should().Be(app);
        File.ReadAllText(Path.Combine(output, "functions", "function_GetOrders.py"))
            .Should().ContainAll("name=\"function_GetOrders\"", "func.AuthLevel.FUNCTION");
        File.ReadAllText(Path.Combine(output, "functions", "function_Cleanup.py"))
            .Should().ContainAll("name=\"function_Cleanup\"", "schedule=\"0 */5 * * * *\"", "func.Blueprint()");
        AssertCleanOutput(output);
    }

    private static void AssertCleanOutput(string output)
    {
        Directory.GetDirectories(output, ".template.config", SearchOption.AllDirectories).Should().BeEmpty();
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotContain("FunctionName");
        }
    }
}
