// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplateArgumentParserProvenanceTests
{
    [Theory]
    [InlineData("safe", "--param:Framework", "safe")]
    [InlineData("safe", "-p:f=safe")]
    [InlineData("SAFE", "--param:Framework=SAFE")]
    public void Parse_ReservedAliases_RecordOnlyCanonicalExplicitIdentity(string expected, params string[] raw)
    {
        var before = raw.ToArray();

        var result = Parse(raw);

        result.IsComplete.Should().BeTrue();
        result.ExplicitParameterNames.Should().Equal("Framework");
        result.ExplicitParameterNames.Contains("framework").Should().BeFalse();
        result.ExplicitParameterNames.Contains("--param:Framework").Should().BeFalse();
        result.ValuesByCanonicalName["Framework"].Should().Be(expected);
        raw.Should().Equal(before);
    }

    [Fact]
    public void Parse_DefaultAndEqualExplicitValue_PreserveDifferentOrigins()
    {
        var omitted = Parse([]);
        var explicitValue = Parse(["--param:Framework", "safe"]);
        var completed = Parse([], new Dictionary<string, string?> { ["Framework"] = "safe" });

        explicitValue.ValuesByCanonicalName.Should().BeEquivalentTo(omitted.ValuesByCanonicalName);
        completed.ValuesByCanonicalName.Should().BeEquivalentTo(omitted.ValuesByCanonicalName);
        explicitValue.ExplicitParameterNames.Should().Equal("Framework");
        omitted.ExplicitParameterNames.Should().BeEmpty();
        completed.ExplicitParameterNames.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ExplicitResult_IsImmutableAndIndependentOfLaterInput()
    {
        List<string> raw = ["--param:Framework", "safe"];
        var first = Parse(raw);
        raw.Clear();
        var second = Parse(raw);

        Action mutate = () => ((ISet<string>)first.ExplicitParameterNames).Add("Other");

        mutate.Should().Throw<NotSupportedException>();
        first.ExplicitParameterNames.Should().Equal("Framework");
        second.ExplicitParameterNames.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DuplicateScalar_RemainsInvalidWithCanonicalOrigin()
    {
        var result = Parse(["--param:Framework=safe", "-p:f=strict"]);

        result.IsComplete.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Framework");
        result.ExplicitParameterNames.Should().Equal("Framework");
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("repeated", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingExplicitValue_DoesNotBecomeDefaultOrCompletion()
    {
        var result = Parse(["--param:Framework"], new Dictionary<string, string?> { ["Framework"] = "strict" });

        result.IsComplete.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Framework");
        result.ExplicitParameterNames.Should().Equal("Framework");
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("cannot replace explicit input", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--")]
    [InlineData("[suggest]")]
    public void Parse_UnaccountedInput_DoesNotFabricateCanonicalOrigins(string token)
    {
        var result = Parse([token]);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ExplicitParameterNames.Should().BeEmpty();
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.TokenIndices.Contains(0));
    }

    [Theory]
    [InlineData("-42", false)]
    [InlineData("-9223372036854775808", false)]
    [InlineData("-1.25e3", false)]
    [InlineData("--unknown", true)]
    [InlineData("--", true)]
    [InlineData("-literal", true)]
    [InlineData("literal", false)]
    [InlineData("", false)]
    public void IsOptionLikeToken_ExistingPredicate_RemainsUnchanged(string token, bool expected)
    {
        TemplateArgumentParser.IsOptionLikeToken(token).Should().Be(expected);
    }

    [Fact]
    public void IsOptionLikeToken_CurrentAndInvariantNumbers_RemainScalarValues()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            TemplateArgumentParser.IsOptionLikeToken("-1,25").Should().BeFalse();
            TemplateArgumentParser.IsOptionLikeToken("-1.25").Should().BeFalse();
            TemplateArgumentParser.IsOptionLikeToken("--value").Should().BeTrue();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static TemplateCandidateParseResult Parse(IReadOnlyList<string> raw, IReadOnlyDictionary<string, string?>? completed = null)
        => new TemplateArgumentParser().Parse(new("Candidate",
            [new("Framework", "force", "f", null, "choice", [new("safe", null, null), new("strict", null, null)], "safe", false, false, false)],
            new HashSet<string>(["--force", "-f"], StringComparer.Ordinal), raw, completed));
}
