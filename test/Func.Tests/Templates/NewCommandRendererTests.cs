// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;

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
    public void RenderCreated_Prints_PostAction_Messages_After_File_Lists()
    {
        var interaction = new TestInteractionService();
        var renderer = new NewCommandRenderer(interaction);

        renderer.RenderCreated(
            MakeTemplate(id: "http", displayName: "HttpTrigger", description: ""),
            "GetOrders",
            created: ["api.py"],
            modified: [],
            messages:
            [
                "Register the 'api' blueprint in your function_app.py:",
                "    from api import bp",
                "    app.register_functions(bp)",
            ]);

        interaction.Lines.Should().Contain(l => l.Contains("Register the 'api' blueprint", System.StringComparison.Ordinal));
        interaction.Lines.Should().Contain("    from api import bp");
        interaction.Lines.Should().Contain("    app.register_functions(bp)");
    }

    [Fact]
    public void RenderCreatedJson_Includes_Messages_Array()
    {
        var interaction = new TestInteractionService();
        var renderer = new NewCommandRenderer(interaction);

        renderer.RenderCreatedJson(
            MakeTemplate(id: "http", displayName: "HttpTrigger", description: ""),
            "GetOrders",
            created: ["api.py"],
            modified: [],
            messages: ["    from api import bp"]);

        string json = interaction.Lines.Single(l => l.StartsWith("JSON:", System.StringComparison.Ordinal));
        json.Should().Contain("\"messages\":[\"    from api import bp\"]");
    }

    private static FunctionTemplateInfo MakeTemplate(string id, string displayName, string? description) =>
        new(
            Id: id,
            Stack: "dotnet",
            DisplayName: displayName,
            Description: description,
            DefaultFunctionName: null,
            Languages: [],
            Metadata: new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
}
