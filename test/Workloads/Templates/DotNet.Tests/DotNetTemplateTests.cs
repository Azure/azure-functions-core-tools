// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Xml.Linq;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.DotNet.Tests;

public sealed class DotNetTemplateTests : IAsyncLifetime
{
    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Project_Options_GenerateConsistentStandaloneProject(bool noHttp, bool noTelemetry)
    {
        ITemplateInfo template = await GetTemplateAsync("func-dotnet", "project");
        Dictionary<string, string?> parameters = [];
        if (noHttp)
        {
            parameters["NoHttp"] = "true";
        }

        if (noTelemetry)
        {
            parameters["NoTelemetry"] = "true";
        }

        ITemplateCreationResult result = await GenerateAsync(template, "Sample.App", parameters);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        var project = XDocument.Load(Path.Combine(output, "Sample.App.csproj"));
        project.Root!.Element("PropertyGroup")!.Element("TargetFramework")!.Value.Should().Be("net10.0");
        string[] references = [.. project.Descendants("PackageReference").Select(item => item.Attribute("Include")!.Value)];
        references.Should().Contain("Microsoft.Azure.Functions.Worker");
        references.Should().NotContain("Microsoft.Azure.Functions.Worker.Sdk");
        references.Contains("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore").Should().Be(!noHttp);
        references.Contains("Microsoft.Azure.Functions.Worker.OpenTelemetry").Should().Be(!noTelemetry);
        references.Contains("Azure.Monitor.OpenTelemetry.Exporter").Should().Be(!noTelemetry);
        project.Descendants("FrameworkReference").Any().Should().Be(!noHttp);
        File.Exists(Path.Combine(output, "HelloWorld.cs")).Should().Be(!noHttp);

        string program = File.ReadAllText(Path.Combine(output, "Program.cs"));
        program.Contains("ConfigureFunctionsWebApplication").Should().Be(!noHttp);
        program.Contains("UseFunctionsWorkerDefaults").Should().Be(!noTelemetry);
        program.Contains("UseAzureMonitorExporter").Should().Be(!noTelemetry);
        program.Contains("APPLICATIONINSIGHTS_CONNECTION_STRING").Should().Be(!noTelemetry);
        if (!noHttp)
        {
            File.ReadAllText(Path.Combine(output, "HelloWorld.cs")).Should().ContainAll("namespace Sample.App;", "Hello, world!");
        }

        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.TryGetProperty("telemetryMode", out JsonElement mode).Should().Be(!noTelemetry);
        if (!noTelemetry)
        {
            mode.GetString().Should().Be("OpenTelemetry");
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.TryGetProperty("AzureWebJobsStorage", out _).Should().BeFalse();
        values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out _).Should().BeFalse();
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeFalse();
        Directory.Exists(Path.Combine(output, "local-settings")).Should().BeFalse();
        Directory.Exists(Path.Combine(output, ".template.config")).Should().BeFalse();
        foreach (string file in Directory.EnumerateFiles(output))
        {
            File.ReadAllText(file).Should().NotContainAny("#if", "#endif", "FunctionsProject", "NoHttp", "NoTelemetry");
        }
    }

    [Theory]
    [InlineData("net11.0", CreationResultStatus.Success)]
    [InlineData("net9.0", CreationResultStatus.InvalidParamValues)]
    public async Task Project_Framework_ValidatesAndSubstitutesChoice(string framework, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("func-dotnet", "project");

        ITemplateCreationResult result = await GenerateAsync(template, "FrameworkApp", new() { ["Framework"] = framework });

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            var project = XDocument.Load(Path.Combine(result.OutputBaseDirectory!, "FrameworkApp.csproj"));
            project.Descendants("TargetFramework").Single().Value.Should().Be(framework);
        }
    }

    [Theory]
    [InlineData("httptrigger", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore")]
    [InlineData("timertrigger", "Microsoft.Azure.Functions.Worker.Extensions.Timer")]
    public async Task Item_Generation_ReplacesNameAndNamespaceAndDeclaresPackageAction(string shortName, string package)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName, "item");

        ITemplateCreationResult result = await GenerateAsync(template, "MyFunction", new() { ["namespace"] = "Example.Functions" });

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string source = File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "MyFunction.cs"));
        source.Should().ContainAll("namespace Example.Functions;", "public sealed class MyFunction", "[Function(nameof(MyFunction))]");
        source.Should().NotContain("FunctionNamespace");
        if (shortName == "httptrigger")
        {
            source.Should().ContainAll("HttpRequest", "IActionResult", "Hello, world!");
        }
        else
        {
            source.Should().ContainAll("[TimerTrigger(\"0 */5 * * * *\")]", "ILogger<MyFunction>");
        }

        IPostAction action = result.CreationResult!.PostActions.Should().ContainSingle().Subject;
        action.ActionId.Should().Be(new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814"));
        action.Args["referenceType"].Should().Be("package");
        action.Args["reference"].Should().Be(package);
        action.Args["projectFileExtensions"].Should().Be(".csproj");
        action.Args["version"].Should().NotBeNullOrWhiteSpace();
        action.ManualInstructions.Should().Contain(package);
        action.ContinueOnError.Should().BeFalse();
    }

    private async Task<ITemplateInfo> GetTemplateAsync(string shortName, string type)
    {
        ITemplateInfo template = await _host.GetTemplateAsync(shortName);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be("C#");
        return template;
    }

    private Task<ITemplateCreationResult> GenerateAsync(ITemplateInfo template, string name, Dictionary<string, string?> parameters)
        => _host.InstantiateAsync(template, name, parameters);
}
