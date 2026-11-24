// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.Node.Tests;

public sealed class NodeTemplateTests : IAsyncLifetime
{
    private const string JavaScript = "JavaScript";
    private const string TypeScript = "TypeScript";
    private const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";

    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("func-node", "project")]
    [InlineData("httptrigger", "item")]
    [InlineData("timertrigger", "item")]
    public async Task Discovery_SharedShortName_ExposesDistinctVariantPerLanguage(string shortName, string type)
    {
        ITemplateInfo javaScript = await GetTemplateAsync(shortName, JavaScript, type);
        ITemplateInfo typeScript = await GetTemplateAsync(shortName, TypeScript, type);

        javaScript.Identity.Should().EndWith(".JavaScript");
        typeScript.Identity.Should().EndWith(".TypeScript");
        javaScript.GroupIdentity.Should().NotBeNullOrWhiteSpace().And.Be(typeScript.GroupIdentity);
    }

    [Theory]
    [InlineData(JavaScript, false, false)]
    [InlineData(JavaScript, true, true)]
    [InlineData(TypeScript, false, false)]
    [InlineData(TypeScript, true, true)]
    public async Task Project_Options_GenerateConsistentStandaloneProject(string language, bool noHttp, bool noBundle)
    {
        ITemplateInfo template = await GetTemplateAsync("func-node", language, "project");
        Dictionary<string, string?> parameters = [];
        if (noHttp)
        {
            parameters["NoHttp"] = "true";
        }

        if (noBundle)
        {
            parameters["NoBundle"] = "true";
        }

        ITemplateCreationResult result = await _host.InstantiateAsync(template, "My Sample.App", parameters, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        bool isTypeScript = language == TypeScript;
        string extension = ExtensionFor(language);

        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "package.json")));
        JsonElement root = package.RootElement;
        root.GetProperty("name").GetString().Should().Be("my-sample.app");
        root.GetProperty("main").GetString().Should().Be(isTypeScript ? "dist/src/functions/*.js" : "src/functions/*.js");
        root.GetProperty("dependencies").TryGetProperty("@azure/functions", out _).Should().BeTrue();
        root.GetProperty("scripts").TryGetProperty("build", out _).Should().Be(isTypeScript);
        root.TryGetProperty("devDependencies", out JsonElement devDependencies).Should().Be(isTypeScript);
        if (isTypeScript)
        {
            devDependencies.TryGetProperty("typescript", out _).Should().BeTrue();
            devDependencies.TryGetProperty("@types/node", out _).Should().BeTrue();
            JsonElement scripts = root.GetProperty("scripts");
            scripts.GetProperty("build").GetString().Should().Be("npm run clean && tsc");
            scripts.GetProperty("clean").GetString().Should().Be("rimraf dist");
            scripts.TryGetProperty("prestart", out _).Should().BeFalse();
        }

        File.Exists(Path.Combine(output, "tsconfig.json")).Should().Be(isTypeScript);
        if (isTypeScript)
        {
            File.ReadAllText(Path.Combine(output, "src", "NoFunctions.ts")).Should().Contain("export {};");
        }

        File.Exists(Path.Combine(output, ".funcignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, "src", "functions", $"HelloWorld.{extension}")).Should().Be(!noHttp);

        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.TryGetProperty("extensionBundle", out JsonElement bundle).Should().Be(!noBundle);
        if (!noBundle)
        {
            bundle.GetProperty("id").GetString().Should().Be(StableBundleId);
            bundle.GetProperty("version").GetString().Should().Be("[4.*, 5.0.0)");
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.TryGetProperty("AzureWebJobsStorage", out _).Should().BeFalse();
        values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out _).Should().BeFalse();
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeFalse();
        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData(JavaScript, "Stable", CreationResultStatus.Success, StableBundleId)]
    [InlineData(JavaScript, "Preview", CreationResultStatus.Success, StableBundleId + ".Preview")]
    [InlineData(TypeScript, "Experimental", CreationResultStatus.Success, StableBundleId + ".Experimental")]
    [InlineData(TypeScript, "Nightly", CreationResultStatus.InvalidParamValues, null)]
    public async Task Project_BundlesChannel_ValidatesAndSubstitutesBundleId(
        string language, string channel, CreationResultStatus expectedStatus, string? expectedId)
    {
        ITemplateInfo template = await GetTemplateAsync("func-node", language, "project");

        ITemplateCreationResult result = await _host.InstantiateAsync(
            template, "bundleapp", new Dictionary<string, string?> { ["BundlesChannel"] = channel }, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "host.json")));
            host.RootElement.GetProperty("extensionBundle").GetProperty("id").GetString().Should().Be(expectedId);
        }
    }

    [Theory]
    [InlineData(JavaScript, "httptrigger")]
    [InlineData(TypeScript, "httptrigger")]
    [InlineData(JavaScript, "timertrigger")]
    [InlineData(TypeScript, "timertrigger")]
    public async Task Item_Generation_ReplacesFunctionNameInFileAndRegistration(string language, string shortName)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName, language, "item");

        ITemplateCreationResult result = await _host.InstantiateAsync(template, "MyFunction", cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        string relativePath = $"src/functions/MyFunction.{ExtensionFor(language)}";
        result.CreationResult!.PrimaryOutputs.Should().ContainSingle().Which.Path.Replace('\\', '/').Should().Be(relativePath);
        Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Should().ContainSingle()
            .Which.Should().Be(Path.GetFullPath(Path.Combine(output, relativePath)));

        string source = File.ReadAllText(Path.Combine(output, relativePath));
        source.Should().Contain(language == TypeScript ? "from '@azure/functions'" : "require('@azure/functions')");
        if (shortName == "httptrigger")
        {
            source.Should().ContainAll("app.http('MyFunction'", "Hello, ${name}!");
        }
        else
        {
            source.Should().ContainAll("app.timer('MyFunction'", "schedule: '0 */5 * * * *'");
        }

        result.CreationResult.PostActions.Should().BeEmpty();
        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData(JavaScript)]
    [InlineData(TypeScript)]
    public async Task Items_AddedToGeneratedProject_DoNotOverwriteExistingFunctions(string language)
    {
        string extension = ExtensionFor(language);
        string project = Path.Combine(_host.OutputDirectory, "app");
        string functions = Path.Combine(project, "src", "functions");
        ITemplateInfo projectTemplate = await GetTemplateAsync("func-node", language, "project");
        ITemplateInfo http = await GetTemplateAsync("httptrigger", language, "item");
        ITemplateInfo timer = await GetTemplateAsync("timertrigger", language, "item");
        ITemplateCreationResult created = await _host.InstantiateAsync(projectTemplate, "app", outputDirectory: project, cancellationToken: TestContext.Current.CancellationToken);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        string helloWorld = File.ReadAllText(Path.Combine(functions, $"HelloWorld.{extension}"));

        ITemplateCreationResult httpResult = await _host.InstantiateAsync(http, "Greeting", outputDirectory: project, cancellationToken: TestContext.Current.CancellationToken);
        ITemplateCreationResult timerResult = await _host.InstantiateAsync(timer, "Cleanup", outputDirectory: project, cancellationToken: TestContext.Current.CancellationToken);

        httpResult.Status.Should().Be(CreationResultStatus.Success, httpResult.ErrorMessage);
        timerResult.Status.Should().Be(CreationResultStatus.Success, timerResult.ErrorMessage);
        httpResult.CreationEffects!.FileChanges.Should().OnlyContain(change => change.ChangeKind == ChangeKind.Create);
        timerResult.CreationEffects!.FileChanges.Should().OnlyContain(change => change.ChangeKind == ChangeKind.Create);
        File.ReadAllText(Path.Combine(functions, $"HelloWorld.{extension}")).Should().Be(helloWorld);
        Directory.EnumerateFiles(functions).Select(Path.GetFileName).Should().BeEquivalentTo(
            [$"HelloWorld.{extension}", $"Greeting.{extension}", $"Cleanup.{extension}"]);
        File.ReadAllText(Path.Combine(functions, $"Greeting.{extension}")).Should().Contain("app.http('Greeting'");
        File.ReadAllText(Path.Combine(functions, $"Cleanup.{extension}")).Should().Contain("app.timer('Cleanup'");
    }

    private static string ExtensionFor(string language) => language == TypeScript ? "ts" : "js";

    private static void AssertNoTemplateArtifacts(string output)
    {
        Directory.EnumerateDirectories(output, ".template.config", SearchOption.AllDirectories).Should().BeEmpty();
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotContainAny(
                "#if", "#endif", "functions-project", "HttpFunction", "TimerFunction", "NoHttp", "NoBundle", "BundlesChannel");
        }
    }

    private async Task<ITemplateInfo> GetTemplateAsync(string shortName, string language, string type)
    {
        ITemplateInfo template = await _host.GetTemplateAsync(shortName, language, TestContext.Current.CancellationToken);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be(language);
        template.TagsCollection["stack"].Should().Be("node");
        return template;
    }
}
