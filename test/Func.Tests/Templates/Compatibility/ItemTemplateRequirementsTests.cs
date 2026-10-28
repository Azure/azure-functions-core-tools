// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Compatibility;

namespace Azure.Functions.Cli.Tests.Templates.Compatibility;

public sealed class ItemTemplateRequirementsTests
{
    [Fact]
    public void Parse_OneRequirement_ReturnsDeclaredConjunction()
    {
        var requirements = ItemTemplateRequirements.Parse("""{"type":"func-project-capabilities","args":["future.http-model"]}""");

        requirements.IsDeclared.Should().BeTrue();
        requirements.CapabilityIds.Should().Equal("future.http-model");
    }

    [Fact]
    public void Parse_MultipleRequirements_PreservesEveryOpaqueIdentifier()
    {
        var requirements = ItemTemplateRequirements.Parse(
            """{"type":"func-project-capabilities","args":["unfamiliarStack_9.http-model","another.publisher-capability"]}""");

        requirements.CapabilityIds.Should().Equal("another.publisher-capability", "unfamiliarStack_9.http-model");
    }

    [Fact]
    public void Parse_AbsentDeclaration_IsNotAnEmptyDeclaredConjunction()
    {
        var requirements = ItemTemplateRequirements.Parse(null);

        requirements.IsDeclared.Should().BeFalse();
        requirements.CapabilityIds.Should().BeEmpty();
        new ItemTemplateRequirements(["a"]).IsDeclared.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"type":null,"args":["a"]}""")]
    [InlineData("""{"type":1,"args":["a"]}""")]
    [InlineData("""{"type":"func-project-capabilities"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":null}""")]
    [InlineData("""{"type":"func-project-capabilities","args":"a"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[null]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[1]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[["a","b"]]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[{"anyOf":["a","b"]}]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a","a"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"args":["b"]}""")]
    [InlineData("""{"type":"func-project-capabilities","t\u0079pe":"func-project-capabilities","args":["a"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["\uD800"]}""")]
    [InlineData("""{"type":"\uD800","args":["a"]}""")]
    [InlineData("""{"\uD800":"invalid","type":"func-project-capabilities","args":["a"]}""")]
    public void Parse_MalformedDeclaredRequirements_Throws(string declaration)
    {
        Action action = () => ItemTemplateRequirements.Parse(declaration);

        action.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("""{"type":"func-workload","args":["a"]}""")]
    [InlineData("""{"type":"FUNC-PROJECT-CAPABILITIES","args":["a"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"anyOf":["b"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"version":"[1,2)"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["a"],"script":"return true"}""")]
    public void Parse_UnsupportedVocabularyOrSyntax_FailsClearly(string declaration)
    {
        Action action = () => ItemTemplateRequirements.Parse(declaration);

        action.Should().Throw<NotSupportedException>().WithMessage("*supported*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-a")]
    [InlineData(".a")]
    [InlineData("a b")]
    [InlineData("a>=4")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a*")]
    [InlineData("a\nb")]
    [InlineData("\u00e9")]
    public void Constructor_MalformedIdentifier_Throws(string? id)
    {
        Action action = () => new ItemTemplateRequirements([id!]);

        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void Constructor_EmptyDeclaration_ThrowsRatherThanAdmittingEverything()
    {
        Action action = () => new ItemTemplateRequirements([]);

        action.Should().Throw<FormatException>().WithMessage("*at least one*");
    }

    [Fact]
    public void Constructor_NullCollection_Throws()
    {
        Action action = () => new ItemTemplateRequirements(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("capabilityIds");
    }

    [Fact]
    public void Constructor_CallerMutation_DoesNotChangeRequirements()
    {
        List<string> ids = ["b", "a"];
        var requirements = new ItemTemplateRequirements(ids);

        ids.Clear();
        ids.Add("different");

        requirements.CapabilityIds.Should().Equal("a", "b");
    }

    [Fact]
    public void Constructor_CapturedRequirements_RejectMutation()
    {
        var requirements = new ItemTemplateRequirements(["a"]);

        Action action = () => ((IList<string>)requirements.CapabilityIds).Clear();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_EquivalentOrderings_HaveSameRequirements()
    {
        var first = new ItemTemplateRequirements(["a", "b"]);
        var second = new ItemTemplateRequirements(["b", "a"]);

        first.CapabilityIds.Should().Equal(second.CapabilityIds);
        first.IsDeclared.Should().Be(second.IsDeclared);
    }

    [Fact]
    public void Constructor_IdentifiersDifferOnlyByCase_AreDistinctOpaqueRequirements()
    {
        var requirements = new ItemTemplateRequirements(["A", "a"]);

        requirements.CapabilityIds.Should().Equal("A", "a");
    }
}
