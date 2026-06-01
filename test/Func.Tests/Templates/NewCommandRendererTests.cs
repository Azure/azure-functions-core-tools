// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Xunit;

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

        Assert.Contains("TABLE: [NAME, TEMPLATE ID, DESCRIPTION]", interaction.Lines);
        Assert.Contains("  ROW: [BlobTrigger, blob, blob desc]", interaction.Lines);
        Assert.Contains("  ROW: [DurableFunctionsEntityClass, durableentityclass, entity desc]", interaction.Lines);
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

        Assert.Contains("  ROW: [anonymous, anonymous, no display]", interaction.Lines);
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

        Assert.Contains(
            interaction.Lines,
            l => l.Contains("func new --template <TEMPLATE_ID> --name <function-name>", System.StringComparison.Ordinal));
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
