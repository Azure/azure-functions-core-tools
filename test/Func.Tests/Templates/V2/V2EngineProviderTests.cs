// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.V2;

public class V2EngineProviderTests : IDisposable
{
    private readonly string _workloadHome;
    private readonly string _installDir;

    public V2EngineProviderTests()
    {
        _workloadHome = Path.Combine(Path.GetTempPath(), "func-new-pr2-provider-" + Guid.NewGuid().ToString("N"));
        _installDir = Path.Combine(_workloadHome, "node-templates-1.0.0");
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workloadHome, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ListTemplatesAsync_Empty_Registry_Returns_Empty()
    {
        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InstalledTemplatesWorkload>());

        var provider = new V2EngineProvider(installed);
        IReadOnlyList<FunctionTemplateInfo> templates = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_workloadHome), false), "node", "javascript"),
            CancellationToken.None);

        Assert.Empty(templates);
    }

    [Fact]
    public async Task ListTemplatesAsync_Reads_Fixture_Payload_And_Projects_Templates()
    {
        WriteFixturePayload(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("node", "1.0.0", _installDir)]);

        var provider = new V2EngineProvider(installed);
        IReadOnlyList<FunctionTemplateInfo> templates = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_workloadHome), false), "node", null),
            CancellationToken.None);

        var http = Assert.Single(templates);
        Assert.Equal("HttpTrigger-JavaScript", http.Id);
        Assert.Equal(EngineIds.V2, http.EngineId);
        Assert.Equal("node", http.Stack);
        Assert.Equal("HTTP trigger", http.DisplayName);
        Assert.Equal("HttpTrigger", http.DefaultFunctionName);
    }

    [Fact]
    public async Task ListTemplatesAsync_Filters_By_Language()
    {
        WriteFixturePayload(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("node", "1.0.0", _installDir)]);

        var provider = new V2EngineProvider(installed);
        IReadOnlyList<FunctionTemplateInfo> typescriptOnly = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_workloadHome), false), "node", "typescript"),
            CancellationToken.None);

        // The fixture template has language="javascript"; "typescript" filter drops it.
        Assert.Empty(typescriptOnly);
    }

    [Fact]
    public async Task ListTemplatesAsync_Omits_Hidden_Templates()
    {
        WriteHiddenFixturePayload(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("node", "1.0.0", _installDir)]);

        var provider = new V2EngineProvider(installed);
        IReadOnlyList<FunctionTemplateInfo> templates = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_workloadHome), false), "node", null),
            CancellationToken.None);

        // BlobTrigger-TypeScript and DurableFunctionsOrchestrator-JavaScript
        // are on the hidden list; only the HTTP trigger should remain.
        Assert.Single(templates);
        Assert.Equal("HttpTrigger-JavaScript", templates[0].Id);
    }

    private static void WriteFixturePayload(string installDir)
    {
        string v2 = Path.Combine(installDir, "tools", "any", "content", "v2");
        Directory.CreateDirectory(Path.Combine(v2, "templates"));
        Directory.CreateDirectory(Path.Combine(v2, "bindings"));
        Directory.CreateDirectory(Path.Combine(v2, "resources"));

        File.WriteAllText(Path.Combine(v2, "templates", "templates.json"), """
        [
          {
            "id": "HttpTrigger-JavaScript",
            "name": "HTTP trigger",
            "description": "An HTTP-triggered function.",
            "language": "javascript",
            "triggerType": "http",
            "jobs": [
              {
                "type": "CreateNewApp",
                "inputs": [
                  { "paramId": "trigger-functionName", "assignTo": "$(FUNCTION_NAME_INPUT)", "defaultValue": "HttpTrigger" }
                ]
              }
            ],
            "actions": [
              { "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).js", "fileContent": "// generated" }
            ]
          }
        ]
        """);

        File.WriteAllText(Path.Combine(v2, "bindings", "userPrompts.json"), """
        [
          { "id": "trigger-functionName", "label": "Function name", "defaultValue": "HttpTrigger" }
        ]
        """);

        File.WriteAllText(Path.Combine(v2, "resources", "Resources.json"), """
        { "HttpTrigger_description": "An HTTP-triggered function." }
        """);
    }

    private static void WriteHiddenFixturePayload(string installDir)
    {
        string v2 = Path.Combine(installDir, "tools", "any", "content", "v2");
        Directory.CreateDirectory(Path.Combine(v2, "templates"));
        Directory.CreateDirectory(Path.Combine(v2, "bindings"));
        Directory.CreateDirectory(Path.Combine(v2, "resources"));

        File.WriteAllText(Path.Combine(v2, "templates", "templates.json"), """
        [
          {
            "id": "HttpTrigger-JavaScript",
            "name": "HTTP trigger",
            "language": "javascript",
            "jobs": [ { "type": "CreateNewApp", "inputs": [ { "paramId": "trigger-functionName", "assignTo": "$(FUNCTION_NAME_INPUT)", "defaultValue": "HttpTrigger" } ] } ],
            "actions": [ { "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).js", "fileContent": "// generated" } ]
          },
          {
            "id": "BlobTrigger-TypeScript",
            "name": "Blob trigger",
            "language": "typescript",
            "jobs": [ { "type": "CreateNewApp", "inputs": [ { "paramId": "trigger-functionName", "assignTo": "$(FUNCTION_NAME_INPUT)", "defaultValue": "BlobTrigger" } ] } ],
            "actions": [ { "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).ts", "fileContent": "// generated" } ]
          },
          {
            "id": "DurableFunctionsOrchestrator-JavaScript",
            "name": "Durable Functions orchestrator",
            "language": "javascript",
            "jobs": [ { "type": "CreateNewApp", "inputs": [ { "paramId": "trigger-functionName", "assignTo": "$(FUNCTION_NAME_INPUT)", "defaultValue": "DurableFunctionsOrchestrator" } ] } ],
            "actions": [ { "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).js", "fileContent": "// generated" } ]
          }
        ]
        """);

        File.WriteAllText(Path.Combine(v2, "bindings", "userPrompts.json"), """
        [ { "id": "trigger-functionName", "label": "Function name", "defaultValue": "HttpTrigger" } ]
        """);

        File.WriteAllText(Path.Combine(v2, "resources", "Resources.json"), "{}");
    }
}
