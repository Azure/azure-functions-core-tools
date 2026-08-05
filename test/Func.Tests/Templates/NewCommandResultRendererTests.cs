// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandResultRendererTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void RenderResolutionFailure_MissingLanguage_RendersSetupHint()
    {
        NewCommandResultRenderer renderer = CreateRenderer();
        NewCommandResolutionFailure failure = new(
            NewCommandResolutionFailureKind.MissingLanguage,
            Stack: "dotnet",
            ProjectPath: "project");

        renderer.RenderResolutionFailure(failure);

        _interaction.Lines.Should().Contain(line =>
            line.Contains("Cannot determine language for stack 'dotnet'", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("func init", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderApplyResult_Created_RendersFilesAndReturnsSuccess()
    {
        NewCommandResultRenderer renderer = CreateRenderer();
        FunctionTemplateInfo template = CreateTemplate();

        int result = renderer.RenderApplyResult(
            template,
            new TemplateApplicationResult.Created(["Function.cs", "function.json"]));

        result.Should().Be(0);
        _interaction.Lines.Should().Contain(line => line.Contains("Created function 'HttpTrigger'", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("Function.cs", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("function.json", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderApplyResult_AlreadyExists_RendersForceHintAndReturnsFailure()
    {
        NewCommandResultRenderer renderer = CreateRenderer();

        int result = renderer.RenderApplyResult(
            CreateTemplate(),
            new TemplateApplicationResult.AlreadyExists(["Function.cs"]));

        result.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("Function.cs", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(line => line.Contains("--force", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderApplyResult_ProviderFailure_RendersMessageAndReturnsFailure()
    {
        NewCommandResultRenderer renderer = CreateRenderer();
        TemplateApplicationFailure failure = new TemplateApplicationFailure.ProviderError("engine failed", null);

        int result = renderer.RenderApplyResult(CreateTemplate(), new TemplateApplicationResult.Failed(failure));

        result.Should().Be(1);
        _interaction.Lines.Should().Contain(line => line.Contains("engine failed", StringComparison.Ordinal));
    }

    private NewCommandResultRenderer CreateRenderer()
    {
        return new NewCommandResultRenderer(_interaction, new NewCommandRenderer(_interaction));
    }

    private static FunctionTemplateInfo CreateTemplate()
    {
        return new FunctionTemplateInfo(
            "HttpTrigger",
            "dotnet",
            "dotnet",
            "HTTP trigger",
            Description: null,
            DefaultFunctionName: null,
            Languages: ["csharp"],
            new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
    }
}