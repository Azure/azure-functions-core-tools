// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandTemplateSelectorTests
{
    [Fact]
    public async Task SelectAsync_RequestedTemplate_MatchesCaseInsensitively()
    {
        TestInteractionService interaction = new();
        NewCommandTemplateSelector selector = CreateSelector(interaction);
        FunctionTemplateInfo template = CreateTemplate("HttpTrigger");
        NewInvocation invocation = CreateInvocation(requestedTemplate: "httptrigger", nonInteractive: true);

        FunctionTemplateInfo? result = await selector.SelectAsync(invocation, [template], CancellationToken.None);

        result.Should().BeSameAs(template);
        interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_UnknownRequestedTemplate_RendersListHint()
    {
        TestInteractionService interaction = new();
        NewCommandTemplateSelector selector = CreateSelector(interaction);
        NewInvocation invocation = CreateInvocation(requestedTemplate: "Missing", nonInteractive: true);

        FunctionTemplateInfo? result = await selector.SelectAsync(
            invocation,
            [CreateTemplate("HttpTrigger")],
            CancellationToken.None);

        result.Should().BeNull();
        interaction.Lines.Should().Contain(line => line.Contains("Template 'Missing' was not found", StringComparison.Ordinal));
        interaction.Lines.Should().Contain(line => line.Contains("func new --list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectAsync_MissingTemplateInNonInteractiveMode_RendersRequiredOptionHint()
    {
        TestInteractionService interaction = new();
        NewCommandTemplateSelector selector = CreateSelector(interaction);
        NewInvocation invocation = CreateInvocation(requestedTemplate: null, nonInteractive: true);

        FunctionTemplateInfo? result = await selector.SelectAsync(
            invocation,
            [CreateTemplate("HttpTrigger")],
            CancellationToken.None);

        result.Should().BeNull();
        interaction.Lines.Should().Contain(line => line.Contains("Missing required option: --template", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectAsync_MissingTemplateInInteractiveMode_UsesPicker()
    {
        InteractiveTestInteractionService interaction = new();
        NewCommandTemplateSelector selector = CreateSelector(interaction);
        FunctionTemplateInfo template = CreateTemplate("HttpTrigger");
        NewInvocation invocation = CreateInvocation(requestedTemplate: null, nonInteractive: false);

        FunctionTemplateInfo? result = await selector.SelectAsync(invocation, [template], CancellationToken.None);

        result.Should().BeSameAs(template);
        interaction.Lines.Should().Contain(line => line.StartsWith("SELECT: Select a template:", StringComparison.Ordinal));
    }

    private static NewCommandTemplateSelector CreateSelector(TestInteractionService interaction)
    {
        return new NewCommandTemplateSelector(interaction, new TemplatePicker(interaction));
    }

    private static NewInvocation CreateInvocation(string? requestedTemplate, bool nonInteractive)
    {
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        return new NewInvocation(
            workingDirectory,
            requestedTemplate,
            RequestedFunctionName: null,
            Force: false,
            nonInteractive);
    }

    private static FunctionTemplateInfo CreateTemplate(string id)
    {
        return new FunctionTemplateInfo(
            id,
            "node",
            EngineIds.V2,
            "HTTP trigger",
            Description: null,
            DefaultFunctionName: null,
            Languages: ["javascript"],
            new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}