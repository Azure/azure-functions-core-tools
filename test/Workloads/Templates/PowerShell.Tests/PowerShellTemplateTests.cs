// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.PowerShell.Tests;

public sealed class PowerShellTemplateTests : IAsyncLifetime
{
    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("func-powershell", "project", "Azure.Functions.Cli.PowerShell.Project")]
    [InlineData("httptrigger", "item", "Azure.Functions.Cli.PowerShell.HttpTrigger")]
    [InlineData("timertrigger", "item", "Azure.Functions.Cli.PowerShell.TimerTrigger")]
    public async Task Templates_AreDiscoveredFromWorkload(string shortName, string type, string identity)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        template.Identity.Should().Be(identity);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be("PowerShell");
        template.TagsCollection["stack"].Should().Be("powershell");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task Project_Options_GenerateConsistentLayout(bool noHttp, bool noBundle, bool managedDependencies)
    {
        ITemplateInfo template = await GetTemplateAsync("func-powershell");
        Dictionary<string, string?> parameters = [];
        if (noHttp)
        {
            parameters["NoHttp"] = "true";
        }

        if (noBundle)
        {
            parameters["NoBundle"] = "true";
        }

        if (managedDependencies)
        {
            parameters["ManagedDependencies"] = "true";
        }

        ITemplateCreationResult result = await GenerateAsync(template, "Sample.App", parameters);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        string[] expectedFiles = [".funcignore", "host.json", "local.settings.json", "profile.ps1"];
        if (!noHttp)
        {
            expectedFiles = [.. expectedFiles, "HelloWorld/run.ps1", "HelloWorld/function.json"];
            AssertHttpFunction(output, "HelloWorld", "function");
        }

        if (managedDependencies)
        {
            expectedFiles = [.. expectedFiles, "requirements.psd1"];
            string requirements = File.ReadAllText(Path.Combine(output, "requirements.psd1"));
            requirements.Should().ContainAll(
                "automatically managed by the Functions service",
                "https://aka.ms/functionsmanageddependency",
                "https://www.powershellgallery.com/packages/Az");
            requirements.Should().MatchRegex(@"(?m)^\s*# 'Az' = '\d+\.\*'\s*$");
        }

        GetGeneratedFiles(output).Should().BeEquivalentTo(expectedFiles);
        File.ReadAllLines(Path.Combine(output, ".funcignore")).Should().Contain("local.settings.json");
        File.ReadAllText(Path.Combine(output, "profile.ps1")).Should().ContainAll(
            "$env:MSI_SECRET", "Disable-AzContextAutosave -Scope Process", "Connect-AzAccount -Identity");
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("host.json");
        result.CreationResult.PostActions.Should().BeEmpty();

        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.TryGetProperty("extensionBundle", out JsonElement bundle).Should().Be(!noBundle);
        if (!noBundle)
        {
            bundle.GetProperty("id").GetString().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
            bundle.GetProperty("version").GetString().Should().Be("[4.*, 5.0.0)");
        }

        host.RootElement.TryGetProperty("managedDependency", out JsonElement dependency).Should().Be(managedDependencies);
        if (managedDependencies)
        {
            dependency.GetProperty("enabled").GetBoolean().Should().BeTrue();
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        settings.RootElement.GetProperty("IsEncrypted").GetBoolean().Should().BeFalse();
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.GetProperty("FUNCTIONS_WORKER_RUNTIME_VERSION").GetString().Should().Be("7.6");
        values.GetProperty("AzureWebJobsStorage").GetString().Should().Be("UseDevelopmentStorage=true");
        values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out _).Should().BeFalse();
        AssertNoTemplateArtifacts(output);
    }

    [Fact]
    public async Task Project_RuntimeVersion_DefaultsToAndOffersOnly76()
    {
        ITemplateInfo template = await GetTemplateAsync("func-powershell");

        ITemplateParameter parameter = template.ParameterDefinitions["RuntimeVersion"];
        parameter.DefaultValue.Should().Be("7.6");
        parameter.Choices!.Keys.Should().Equal("7.6");
    }

    [Theory]
    [InlineData("7.6", CreationResultStatus.Success)]
    [InlineData("7.4", CreationResultStatus.InvalidParamValues)]
    [InlineData("7.3", CreationResultStatus.InvalidParamValues)]
    public async Task Project_RuntimeVersion_ValidatesAndSubstitutesChoice(string version, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("func-powershell");

        ITemplateCreationResult result = await GenerateAsync(template, "App", new Dictionary<string, string?> { ["RuntimeVersion"] = version });

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "local.settings.json")));
            settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME_VERSION").GetString().Should().Be(version);
            AssertNoTemplateArtifacts(result.OutputBaseDirectory!);
        }
    }

    [Theory]
    [InlineData("Preview", CreationResultStatus.Success)]
    [InlineData("Experimental", CreationResultStatus.Success)]
    [InlineData("Nightly", CreationResultStatus.InvalidParamValues)]
    public async Task Project_BundlesChannel_ValidatesAndSubstitutesChoice(string channel, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("func-powershell");

        ITemplateCreationResult result = await GenerateAsync(template, "App", new Dictionary<string, string?> { ["BundlesChannel"] = channel });

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "host.json")));
            host.RootElement.GetProperty("extensionBundle").GetProperty("id").GetString()
                .Should().Be($"Microsoft.Azure.Functions.ExtensionBundle.{channel}");
            AssertNoTemplateArtifacts(result.OutputBaseDirectory!);
        }
    }

    [Theory]
    [InlineData("project", "NoHttp", "no-http", null)]
    [InlineData("project", "RuntimeVersion", "runtime-version", null)]
    [InlineData("project", "ManagedDependencies", "managed-dependencies", null)]
    [InlineData("project", "NoBundle", "no-bundles", null)]
    [InlineData("project", "BundlesChannel", "bundles-channel", "c")]
    [InlineData("httptrigger", "AuthLevel", "auth-level", "a")]
    public void TemplateHostMetadata_ExposesOptionAliases(string template, string symbol, string alias, string? shortAlias)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "templates", template, ".template.config", "func.host.json");
        using var metadata = JsonDocument.Parse(File.ReadAllText(path));

        JsonElement symbolInfo = metadata.RootElement.GetProperty("symbolInfo").GetProperty(symbol);
        symbolInfo.GetProperty("longName").GetString().Should().Be(alias);
        if (shortAlias is not null)
        {
            symbolInfo.GetProperty("shortName").GetString().Should().Be(shortAlias);
        }
    }

    [Theory]
    [InlineData(null, "Get-Orders", "function", CreationResultStatus.Success)]
    [InlineData("anonymous", "function", "anonymous", CreationResultStatus.Success)]
    [InlineData("admin", "Get-Orders", "admin", CreationResultStatus.Success)]
    [InlineData("invalid", "Get-Orders", null, CreationResultStatus.InvalidParamValues)]
    public async Task HttpTrigger_Options_ValidateAndGenerateNamedFunction(
        string? authLevel, string name, string? expectedAuth, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("httptrigger");
        Dictionary<string, string?> parameters = [];
        if (authLevel is not null)
        {
            parameters["AuthLevel"] = authLevel;
        }

        ITemplateCreationResult result = await GenerateAsync(template, name, parameters);

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            AssertHttpFunction(result.OutputBaseDirectory!, name, expectedAuth!);
            AssertItemOutput(result, name);
        }
    }

    [Fact]
    public async Task TimerTrigger_GeneratesNamedFunctionWithDefaultSchedule()
    {
        ITemplateInfo template = await GetTemplateAsync("timertrigger");

        ITemplateCreationResult result = await GenerateAsync(template, "Daily-Cleanup");

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        using var function = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "Daily-Cleanup", "function.json")));
        JsonElement binding = function.RootElement.GetProperty("bindings").EnumerateArray().Should().ContainSingle().Subject;
        binding.GetProperty("name").GetString().Should().Be("Timer");
        binding.GetProperty("type").GetString().Should().Be("timerTrigger");
        binding.GetProperty("direction").GetString().Should().Be("in");
        binding.GetProperty("schedule").GetString().Should().Be("0 */5 * * * *");
        File.ReadAllText(Path.Combine(output, "Daily-Cleanup", "run.ps1")).Should().ContainAll(
            "param($Timer)", "$Timer.IsPastDue", "(Get-Date).ToUniversalTime()");
        AssertItemOutput(result, "Daily-Cleanup");
    }

    [Fact]
    public async Task ProjectAndItems_CoexistWithoutOverwritingExistingFiles()
    {
        ITemplateInfo project = await GetTemplateAsync("func-powershell");
        ITemplateInfo http = await GetTemplateAsync("httptrigger");
        ITemplateInfo timer = await GetTemplateAsync("timertrigger");
        ITemplateCreationResult projectResult = await GenerateAsync(project, "App");
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        string output = projectResult.OutputBaseDirectory!;
        Dictionary<string, string> existing = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);

        ITemplateCreationResult first = await GenerateAsync(http, "Orders", outputDirectory: output);
        ITemplateCreationResult second = await GenerateAsync(timer, "Cleanup", outputDirectory: output);
        ITemplateCreationResult third = await GenerateAsync(http, "Health", outputDirectory: output);

        first.Status.Should().Be(CreationResultStatus.Success, first.ErrorMessage);
        second.Status.Should().Be(CreationResultStatus.Success, second.ErrorMessage);
        third.Status.Should().Be(CreationResultStatus.Success, third.ErrorMessage);
        Directory.GetDirectories(output).Select(Path.GetFileName).Should().BeEquivalentTo("HelloWorld", "Orders", "Cleanup", "Health");
        AssertHttpFunction(output, "Orders", "function");
        AssertHttpFunction(output, "Health", "function");
        File.Exists(Path.Combine(output, "Cleanup", "run.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(output, "Cleanup", "function.json")).Should().BeTrue();
        foreach (KeyValuePair<string, string> file in existing)
        {
            File.ReadAllText(file.Key).Should().Be(file.Value);
        }

        AssertNoTemplateArtifacts(output);
    }

    private Task<ITemplateInfo> GetTemplateAsync(string shortName)
        => _host.GetTemplateAsync(shortName, cancellationToken: TestContext.Current.CancellationToken);

    private Task<ITemplateCreationResult> GenerateAsync(
        ITemplateInfo template, string name, IReadOnlyDictionary<string, string?>? parameters = null, string? outputDirectory = null)
        => _host.InstantiateAsync(template, name, parameters, outputDirectory, cancellationToken: TestContext.Current.CancellationToken);

    private void AssertHttpFunction(string output, string name, string authLevel)
    {
        using var function = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, name, "function.json")));
        JsonElement bindings = function.RootElement.GetProperty("bindings");
        bindings.GetArrayLength().Should().Be(2);
        JsonElement input = bindings[0];
        input.GetProperty("authLevel").GetString().Should().Be(authLevel);
        input.GetProperty("type").GetString().Should().Be("httpTrigger");
        input.GetProperty("direction").GetString().Should().Be("in");
        input.GetProperty("name").GetString().Should().Be("Request");
        input.GetProperty("methods").EnumerateArray().Select(method => method.GetString()).Should().Equal("get", "post");
        JsonElement response = bindings[1];
        response.GetProperty("type").GetString().Should().Be("http");
        response.GetProperty("direction").GetString().Should().Be("out");
        response.GetProperty("name").GetString().Should().Be("Response");
        File.ReadAllText(Path.Combine(output, name, "run.ps1")).Should().ContainAll(
            "param($Request, $TriggerMetadata)", "$Request.Query.name", "$Request.Body.name",
            "Push-OutputBinding -Name Response", "[HttpResponseContext]", "[HttpStatusCode]::OK");
    }

    private void AssertItemOutput(ITemplateCreationResult result, string name)
    {
        string[] expectedFiles = [$"{name}/run.ps1", $"{name}/function.json"];
        GetGeneratedFiles(result.OutputBaseDirectory!).Should().BeEquivalentTo(expectedFiles);
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/')).Should().Equal(expectedFiles);
        result.CreationResult.PostActions.Should().BeEmpty();
        AssertNoTemplateArtifacts(result.OutputBaseDirectory!);
    }

    private IEnumerable<string> GetGeneratedFiles(string output)
        => Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(output, path).Replace('\\', '/'));

    private void AssertNoTemplateArtifacts(string output)
    {
        Directory.EnumerateDirectories(output, ".template.config", SearchOption.AllDirectories).Should().BeEmpty();
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotContainAny(
                "HttpFunction", "TimerFunction", "FunctionsProject", "{RuntimeVersion}", "MAJOR_VERSION", "#if", "#endif");
        }
    }
}
