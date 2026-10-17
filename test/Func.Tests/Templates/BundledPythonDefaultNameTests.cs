// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("preview", "OpenAI-Assistant-Python", null, "OpenAI-Assistant-Python")]
    [InlineData("preview", "OpenAI-Chat-Python", null, "OpenAI-Chat-Python")]
    [InlineData("preview", "OpenAI-RAG-Python", null, "OpenAI-RAG-Python")]
    [InlineData("experimental", "OpenAI-Assistant-Python", null, "OpenAI-Assistant-Python")]
    [InlineData("experimental", "OpenAI-Chat-Python", null, "OpenAI-Chat-Python")]
    [InlineData("experimental", "OpenAI-RAG-Python", null, "OpenAI-RAG-Python")]
    [InlineData("preview", "OpenAI-Assistant-Python", "CustomName", "CustomName")]
    [InlineData("preview", "OpenAI-Chat-Python", "CustomName", "CustomName")]
    [InlineData("preview", "OpenAI-RAG-Python", "CustomName", "CustomName")]
    [InlineData("experimental", "OpenAI-Assistant-Python", "CustomName", "CustomName")]
    [InlineData("experimental", "OpenAI-Chat-Python", "CustomName", "CustomName")]
    [InlineData("experimental", "OpenAI-RAG-Python", "CustomName", "CustomName")]
    [InlineData("stable", "HttpTrigger-Python", null, "http_trigger")]
    [InlineData("stable", "HttpTrigger-Python", "CustomName", "CustomName")]
    public async Task PythonDefaultName_ActualComposerPreservesLegacyFallbackAndExplicitNamesWithoutChangingFunctionBytes(
        string channel, string id, string? requestedName, string expectedName)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "name-compatibility"));
        NewExecutionRequest request = new(output, id, requestedName, null, false, true, false, false, false, [], new HashSet<string>());

        var created = await RunPythonWorkflowAsync(request, channel: channel);

        created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        created.ItemName!.Name.Should().Be(expectedName);
        created.ItemName.Origin.Should().Be(requestedName is null ? "authoredDefault" : "explicit");
        var authoredDefault = id == "HttpTrigger-Python" ? "http_trigger" : id;
        created.SelectedTemplate!.DefaultName.Should().Be(authoredDefault);
        var mapping = Applications.EnumerateArray().Single(item =>
            item.GetProperty("templateIdentity").GetString() == created.SelectedTemplate.Identity);
        using (var archive = new PackageArchiveReader(_archive))
        using (var helper = JsonDocument.Parse(Read(archive, mapping.GetProperty("appendHelper").GetProperty("configuration").GetString()!)))
        {
            helper.RootElement.GetProperty("defaultName").GetString().Should().Be(authoredDefault);
        }
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle().Which.Should().Be(app);
        var source = _sources.Single(template => template.Id == id);
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy-name-control"));
        var values = new Dictionary<string, string?>(created.ArgumentResults.Single().ValuesByCanonicalName)
        {
            ["trigger-functionName"] = expectedName,
        };
        new V2TemplateEngine().Apply(source, expectedName, values, legacy, false).Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Path.Combine(legacy.FullName, "function_app.py")));
        File.AppendAllText(app, "\n# user-owned name regression control\n");
        File.AppendAllText(Path.Combine(legacy.FullName, "function_app.py"), "\n# user-owned name regression control\n");
        var beforeAppend = File.ReadAllBytes(app);

        var appended = await RunPythonWorkflowAsync(request, channel: channel);

        appended.Succeeded.Should().BeTrue(string.Join(" ", appended.Diagnostics));
        appended.ItemName!.Name.Should().Be(expectedName);
        appended.Invocation!.Effects!.Kind.Should().Be("applied");
        File.ReadAllBytes(app).Take(beforeAppend.Length).Should().Equal(beforeAppend);
        new V2TemplateEngine().Apply(source, expectedName, values, legacy, false).Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Path.Combine(legacy.FullName, "function_app.py")));
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle().Which.Should().Be(app);
    }
}
