// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandRendererTests
{
    [Fact]
    public void RenderCatalogue_Emits_Three_Columns_Name_TemplateId_Description()
    {
        var interaction = new TestInteractionService();
        var renderer = new NewCommandRenderer(interaction);

        IReadOnlyList<FunctionTemplateInfo> templates =
        [
            MakeTemplate(id: "blob", displayName: "BlobTrigger", description: "blob desc"),
            MakeTemplate(id: "durableentityclass", displayName: "DurableFunctionsEntityClass", description: "entity desc"),
        ];

        renderer.RenderCatalogue("dotnet", "c#", templates);

        interaction.Lines.Should().Contain("TABLE: [NAME, TEMPLATE ID, DESCRIPTION]");
        interaction.Lines.Should().Contain("  ROW: [BlobTrigger, blob, blob desc]");
        interaction.Lines.Should().Contain("  ROW: [DurableFunctionsEntityClass, durableentityclass, entity desc]");
    }

    [Fact]
    public void RenderCatalogue_Falls_Back_To_Id_When_DisplayName_Missing()
    {
        var interaction = new TestInteractionService();
        var renderer = new NewCommandRenderer(interaction);

        IReadOnlyList<FunctionTemplateInfo> templates =
        [
            MakeTemplate(id: "anonymous", displayName: string.Empty, description: "no display"),
        ];

        renderer.RenderCatalogue("dotnet", null, templates);

        interaction.Lines.Should().Contain("  ROW: [anonymous, anonymous, no display]");
    }

    [Fact]
    public void RenderCatalogue_Footer_Uses_TemplateId_Placeholder()
    {
        var interaction = new TestInteractionService();
        var renderer = new NewCommandRenderer(interaction);

        renderer.RenderCatalogue(
            "dotnet",
            "c#",
            [MakeTemplate(id: "http", displayName: "HttpTrigger", description: "")]);

        interaction.Lines.Should().Contain(l => l.Contains("func new --template <TEMPLATE_ID> --name <function-name>", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RenderCatalogueJson_UsesUnwrappedOutputAndPreservesLongAuthoredText()
    {
        var interaction = Substitute.For<IInteractionService>();
        var renderer = new NewCommandRenderer(interaction);
        string description = new('x', 240);

        renderer.RenderCatalogueJson("node", "javascript", [MakeTemplate("http", "HTTP trigger", description)]);

        interaction.Received(1).WriteRawLine(Arg.Any<string>());
        interaction.DidNotReceive().WriteLine(Arg.Any<string>());
        string json = (string)interaction.ReceivedCalls().Single().GetArguments()[0]!;
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("templates")[0].GetProperty("description").GetString().Should().Be(description);
    }

    private static FunctionTemplateInfo MakeTemplate(string id, string displayName, string? description) =>
        new(
            Id: id,
            Stack: "dotnet",
            EngineId: EngineIds.V2,
            DisplayName: displayName,
            Description: description,
            DefaultFunctionName: null,
            Languages: [],
            Metadata: new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
}
