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
        requirements.Fingerprint.Should().HaveLength(64);
    }

    [Fact]
    public void Parse_MultipleRequirements_PreservesEveryOpaqueIdentifier()
    {
        var requirements = ItemTemplateRequirements.Parse(
            """{"type":"func-project-capabilities","args":["unfamiliarRuntime_9.http-model","another.publisher-capability"]}""");

        requirements.CapabilityIds.Should().Equal("another.publisher-capability", "unfamiliarRuntime_9.http-model");
    }

    [Fact]
    public void Parse_AbsentDeclaration_IsNotAnEmptyDeclaredConjunction()
    {
        var requirements = ItemTemplateRequirements.Parse(null);

        requirements.IsDeclared.Should().BeFalse();
        requirements.CapabilityIds.Should().BeEmpty();
        requirements.Fingerprint.Should().NotBe(new ItemTemplateRequirements(["a"]).Fingerprint);
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
    public void Constructor_CallerMutation_DoesNotChangeRequirementsOrFingerprint()
    {
        List<string> ids = ["b", "a"];
        var requirements = new ItemTemplateRequirements(ids);
        var fingerprint = requirements.Fingerprint;

        ids.Clear();
        ids.Add("different");

        requirements.CapabilityIds.Should().Equal("a", "b");
        requirements.Fingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public void Constructor_CapturedRequirements_RejectMutation()
    {
        var requirements = new ItemTemplateRequirements(["a"]);

        Action action = () => ((IList<string>)requirements.CapabilityIds).Clear();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_EquivalentOrderings_HaveSameFingerprint()
    {
        var first = new ItemTemplateRequirements(["a", "b"]);
        var second = new ItemTemplateRequirements(["b", "a"]);
        var changed = new ItemTemplateRequirements(["a", "c"]);

        first.Fingerprint.Should().Be(second.Fingerprint).And.NotBe(changed.Fingerprint);
    }

    [Fact]
    public void Fingerprints_DeclaredAndAbsentRequirements_PreserveCanonicalRepresentation()
    {
        var declared = new ItemTemplateRequirements(["b", "a"]);
        var absent = ItemTemplateRequirements.Parse(null);

        declared.Fingerprint.Should().Be("4C5188EAFDCA050FB097BE621B04518259C24991C336ADD00B2113A6B752CBFA");
        absent.Fingerprint.Should().Be("39E871627D94E47CBE71F39DAD086C689A83BCCB5D6A2F0DF907BB330CB1D492");
    }

    [Fact]
    public void Constructor_IdentifiersDifferOnlyByCase_AreDistinctOpaqueRequirements()
    {
        var requirements = new ItemTemplateRequirements(["A", "a"]);

        requirements.CapabilityIds.Should().Equal("A", "a");
    }
}
