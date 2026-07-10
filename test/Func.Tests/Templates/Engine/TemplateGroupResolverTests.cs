// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class TemplateGroupResolverTests
{
    // Mirrors the language declarations of the real .NET/Node/Python initializers
    // so the resolver derives stacks the same way it does in production.
    private readonly TemplateGroupResolver _resolver = new(
    [
        Initializer("dotnet", new() { ["C#"] = ["csharp"], ["F#"] = ["fsharp"] }),
        Initializer("node", new() { ["JavaScript"] = ["js"], ["TypeScript"] = ["ts"] }),
        Initializer("python", new() { ["Python"] = ["py"] }),
    ]);

    [Fact]
    public void BuildGroups_CollapsesSharedGroupIdentityIntoOneGroup()
    {
        IReadOnlyList<TemplateGroup> groups = _resolver.BuildGroups(
        [
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"),
        ]);

        TemplateGroup group = Assert.Single(groups);
        Assert.Equal("Func.Http", group.Identity);
        Assert.Equal(2, group.Variants.Count);
        Assert.Equal(["httptrigger"], group.ShortNames);
    }

    [Fact]
    public void BuildGroups_UngroupedTemplateBecomesSingletonGroupKeyedByIdentity()
    {
        IReadOnlyList<TemplateGroup> groups = _resolver.BuildGroups(
        [
            Template("Func.Timer.Node", groupIdentity: null, "timertrigger", language: "JavaScript"),
        ]);

        TemplateGroup group = Assert.Single(groups);
        Assert.Equal("Func.Timer.Node", group.Identity);
        Assert.Equal(["timertrigger"], group.ShortNames);
    }

    [Fact]
    public void TryFindGroup_MatchesShortNameCaseInsensitively()
    {
        IReadOnlyList<TemplateGroup> groups = _resolver.BuildGroups(
        [
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"),
        ]);

        Assert.True(_resolver.TryFindGroup(groups, "HttpTrigger", out TemplateGroup? group));
        Assert.NotNull(group);
        Assert.Equal("Func.Http", group!.Identity);
    }

    [Fact]
    public void TryFindGroup_ReturnsFalseForUnknownShortName()
    {
        IReadOnlyList<TemplateGroup> groups = _resolver.BuildGroups(
        [
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"),
        ]);

        Assert.False(_resolver.TryFindGroup(groups, "queuetrigger", out TemplateGroup? group));
        Assert.Null(group);
    }

    [Theory]
    [InlineData("JavaScript", "Func.Http.Node", "node")]
    [InlineData("Python", "Func.Http.Python", "python")]
    public void Resolve_SharedShortName_SelectsVariantByLanguageAndDerivesStack(
        string language,
        string expectedIdentity,
        string expectedStack)
    {
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, language);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal(expectedIdentity, resolved.Variant.Template.Identity);

        // Stack is derived from the winning variant's language, not hardcoded.
        Assert.Equal(expectedStack, resolved.Stack);
    }

    [Fact]
    public void Resolve_ByAlias_MatchesCanonicalLanguageVariant()
    {
        // `--language ts` (alias) must resolve the TypeScript variant.
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node.Js", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Node.Ts", "Func.Http", "httptrigger", language: "TypeScript"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, "ts");

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Ts", resolved.Variant.Template.Identity);
        Assert.Equal("node", resolved.Stack);
    }

    [Fact]
    public void Resolve_BySingleLanguageStackId_MatchesVariant()
    {
        // A resolved single-language project surfaces Language == StackName
        // (e.g. "python"); it must still match the canonical "Python" variant.
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, "python");

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Python", resolved.Variant.Template.Identity);
        Assert.Equal("python", resolved.Stack);
    }

    [Fact]
    public void Resolve_ByLanguageOnly_DerivesStackFromSurvivingVariant()
    {
        // Empty-directory `--language typescript` scenario: no ambient project,
        // the surviving TypeScript variant supplies the Node stack.
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node.Js", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Node.Ts", "Func.Http", "httptrigger", language: "TypeScript"),
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, "TypeScript");

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Node.Ts", resolved.Variant.Template.Identity);
        Assert.Equal("node", resolved.Stack);
    }

    [Fact]
    public void Resolve_NoLanguage_SingleVariantResolves()
    {
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Python", "Func.Http", "httptrigger", language: "Python"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, language: null);

        TemplateVariantResolution.Resolved resolved = Assert.IsType<TemplateVariantResolution.Resolved>(resolution);
        Assert.Equal("Func.Http.Python", resolved.Variant.Template.Identity);
        Assert.Equal("python", resolved.Stack);
    }

    [Fact]
    public void Resolve_NoLanguage_MultipleVariants_IsAmbiguous()
    {
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node.Js", "Func.Http", "httptrigger", language: "JavaScript"),
            Template("Func.Http.Node.Ts", "Func.Http", "httptrigger", language: "TypeScript"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, language: null);

        TemplateVariantResolution.Ambiguous ambiguous = Assert.IsType<TemplateVariantResolution.Ambiguous>(resolution);
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    [Fact]
    public void Resolve_NoVariantForLanguage_IsNoMatch()
    {
        TemplateGroup group = SingleGroup(
            Template("Func.Http.Node", "Func.Http", "httptrigger", language: "JavaScript"));

        TemplateVariantResolution resolution = _resolver.Resolve(group, "Python");

        Assert.IsType<TemplateVariantResolution.NoMatch>(resolution);
    }

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
