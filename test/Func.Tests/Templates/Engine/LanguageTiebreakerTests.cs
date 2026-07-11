// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class LanguageTiebreakerTests
{
    // Mirrors the language declarations of the real .NET/Node/Python initializers
    // so the resolver derives stacks the same way it does in production.
    private readonly TemplateGroupResolver _resolver = new(
    [
        Initializer("dotnet", new() { ["C#"] = ["csharp"], ["F#"] = ["fsharp"] }),
        Initializer("node", new() { ["JavaScript"] = ["js"], ["TypeScript"] = ["ts"] }),
        Initializer("python", new() { ["Python"] = ["py"] }),
    ]);

    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();

    [Fact]
    public async Task ResolveAsync_AmbientLanguage_BreaksTieWithoutPrompting()
    {
        // A Node project whose configuration indicates TypeScript selects the
        // TypeScript variant with no prompt.
        TemplateGroup group = NodeGroup();

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: "TypeScript", explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Ts", resolved.Variant.Template.Identity);
        Assert.Equal("node", resolved.Stack);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AmbientAlias_BreaksTieWithoutPrompting()
    {
        // The ambient signal may be an alias (a resolved project surfaces 'ts')
        // and still selects the canonical TypeScript variant.
        TemplateGroup group = NodeGroup();

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: "ts", explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Ts", resolved.Variant.Template.Identity);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ExplicitLanguage_BreaksRemainingTie()
    {
        // No ambient signal, but --language js narrows the group to JavaScript.
        TemplateGroup group = NodeGroup();

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: null, explicitLanguage: "js");

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Js", resolved.Variant.Template.Identity);
        Assert.Equal("node", resolved.Stack);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AmbientResolves_ExplicitLanguageNotConsulted()
    {
        // Ambient breaks the tie first, so an also-supplied --language is unused.
        TemplateGroup group = NodeGroup();

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: "JavaScript", explicitLanguage: "ts");

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Js", resolved.Variant.Template.Identity);
    }

    [Fact]
    public async Task ResolveAsync_NoAmbientNoLanguage_PromptsAndResolvesChoice()
    {
        // Neither ambient signals nor --language determine js vs ts: prompt, then
        // resolve the user's pick and derive its stack.
        TemplateGroup group = NodeGroup();
        _interaction
            .PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns("TypeScript");

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: null, explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Ts", resolved.Variant.Template.Identity);
        Assert.Equal("node", resolved.Stack);
        await _interaction.Received(1).PromptForSelectionAsync(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<string>>(c => c.Contains("JavaScript") && c.Contains("TypeScript")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AmbientLanguageNotInGroup_PromptsAmongAllVariants()
    {
        // A Python ambient signal does not fit a Node-only group; the soft signal is
        // ignored and, with no --language, the user is prompted.
        TemplateGroup group = NodeGroup();
        _interaction
            .PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns("JavaScript");

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: "python", explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Js", resolved.Variant.Template.Identity);
        await _interaction.Received(1).PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ExplicitLanguageMatchesNoVariant_IsNoMatchWithoutPrompt()
    {
        // An explicit language absent from the group is a failure, not a prompt.
        TemplateGroup group = NodeGroup();

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: null, explicitLanguage: "python");

        Assert.IsType<TemplateVariantResolution.NoMatch>(resolution);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_SingleVariantGroup_ResolvesWithoutPrompt()
    {
        // Only one variant exists, so there is no tie to break regardless of inputs.
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: null, explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Python", resolved.Variant.Template.Identity);
        Assert.Equal("python", resolved.Stack);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MultiStackAmbient_SelectsAcrossStacksWithoutPrompt()
    {
        // A cross-stack group (Node + Python) resolves the Python variant from the
        // ambient signal, deriving the Python stack from the winner.
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node.Js", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Node.Ts", "Func.Http", "httptrigger", language: "TypeScript"),
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution =
            await _tiebreaker().ResolveAsync(group, ambientLanguage: "py", explicitLanguage: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Python", resolved.Variant.Template.Identity);
        Assert.Equal("python", resolved.Stack);
        await _interaction.DidNotReceive().PromptForSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    private LanguageTiebreaker _tiebreaker() => new(_resolver, _interaction);

    private TemplateGroup NodeGroup() => SingleGroup(
        Template("Func.Http.Node.Js", "Func.Http", "httptrigger", language: "JavaScript"),
        Template("Func.Http.Node.Ts", "Func.Http", "httptrigger", language: "TypeScript"));

    private TemplateGroup SingleGroup(params ITemplateInfo[] templates)
        => Assert.Single(_resolver.BuildGroups(templates));

    private static IProjectInitializer Initializer(
        string stack,
        Dictionary<string, IReadOnlyList<string>> aliases)
    {
        IProjectInitializer initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns(stack);
        initializer.SupportedLanguageAliases.Returns(aliases);
        return initializer;
    }

    private static ITemplateInfo Template(
        string identity,
        string? groupIdentity,
        string shortName,
        string? language)
    {
        ITemplateInfo template = Substitute.For<ITemplateInfo>();
        template.Identity.Returns(identity);
        template.GroupIdentity.Returns(groupIdentity);
        template.ShortNameList.Returns([shortName]);

        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (language is not null)
        {
            tags[TemplateGroupResolver.LanguageTag] = language;
        }

        template.TagsCollection.Returns(tags);
        return template;
    }
}
