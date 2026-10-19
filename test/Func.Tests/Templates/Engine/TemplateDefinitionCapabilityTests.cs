// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateDefinitionCapabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-definition-capabilities-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null, false, "hello")]
    [InlineData("MiXeD", true, "mixed")]
    public async Task InvokeAsync_NativeDefinition_OwnsDefaultsDerivedContentPlacementAndConditionalFiles(
        string? greeting, bool includeExtra, string expected)
    {
        string package = Path.Combine(_root, "package");
        string configuration = Path.Combine(package, ".template.config");
        Directory.CreateDirectory(configuration);
        File.WriteAllText(Path.Combine(configuration, "template.json"), """
            {
              "identity": "EngineGate.Native.Item.JavaScript",
              "name": "Engine-only capability probe",
              "shortName": "engine-gate-item",
              "tags": {"type":"item","language":"JavaScript"},
              "sourceName": "engineGateName",
              "defaultName": "EngineDefault",
              "preferDefaultName": true,
              "preferNameDirectory": false,
              "symbols": {
                "Greeting": {"type":"parameter","datatype":"string","defaultValue":"HELLO"},
                "LowerGreeting": {"type":"derived","valueSource":"Greeting","valueTransform":"lowerCase","replaces":"__LOWER_GREETING__"},
                "IncludeExtra": {"type":"parameter","datatype":"bool","defaultValue":"false"}
              },
              "sources": [{
                "source":"./","target":"native-placement/",
                "modifiers":[{"condition":"(!IncludeExtra)","exclude":["optional.txt"]}]
              }]
            }
            """);
        File.WriteAllText(Path.Combine(package, "engineGateName.js"), """module.exports = "__LOWER_GREETING__"; // engineGateName""");
        File.WriteAllText(Path.Combine(package, "optional.txt"), "explicit conditional output");
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive, CancellationToken.None);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), hive));
        var selected = (await templater.ResolveGroupAsync("engine-gate-item", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        Dictionary<string, string?> parameters = [];
        if (greeting is not null)
        {
            parameters.Add("Greeting", greeting);
        }
        if (includeExtra)
        {
            parameters.Add("IncludeExtra", "true");
        }
        TemplateInvocationRequest request = new(output, parameters, DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        File.ReadAllText(Path.Combine(output.Info.FullName, "native-placement", "EngineDefault.js"))
            .Should().Be($"""module.exports = "{expected}"; // EngineDefault""");
        File.Exists(Path.Combine(output.Info.FullName, "native-placement", "optional.txt")).Should().Be(includeExtra);
        Directory.GetFiles(output.Info.FullName, "*", SearchOption.AllDirectories).Should().HaveCount(includeExtra ? 2 : 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
