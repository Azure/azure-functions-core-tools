// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Globalization;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Selection;
using static Azure.Functions.Cli.Tests.Templates.Selection.SelectionFixtures;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

public sealed class TemplateArgumentParserTests
{
    private readonly TemplateArgumentParser _parser = new();

    [Fact]
    public void Parse_NoParametersOrTokens_ReturnsACompleteEmptyMapping()
    {
        var result = _parser.Parse(new(Metadata()), [], []);

        result.IsArgumentCompatible.Should().BeTrue();
        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName.Should().BeEmpty();
        result.ExplicitParameters.Should().BeEmpty();
    }

    [Theory]
    [InlineData("string", " exact text ", " exact text ")]
    [InlineData("bool", "FALSE", "false")]
    [InlineData("int", "+001", "1")]
    [InlineData("int", "-17", "-17")]
    [InlineData("float", "1.25e2", "125")]
    [InlineData("choice", "hOT", "Hot")]
    public void Parse_ExplicitTypedValue_MapsToCanonicalSymbolAndValue(string type, string supplied, string expected)
    {
        var parameter = Parameter("Canonical", type) with { LongName = "value", ShortName = "v" };
        if (type == "choice")
        {
            parameter = parameter with { Choices = [new("Hot", null, null), new("Cold", null, null)] };
        }

        var result = _parser.Parse(new(Metadata() with { Parameters = [parameter] }), ["--value", supplied], []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Canonical", expected));
        result.ExplicitParameters.Should().Equal("Canonical");
        result.AliasesByCanonicalName["Canonical"].Should().Equal("--value", "-v");
    }

    [Theory]
    [InlineData("--Message", "")]
    [InlineData("--Message=", null)]
    public void Parse_ExplicitEmptyString_RemainsExplicit(string option, string? value)
    {
        ImmutableArray<string> tokens = value is null ? [option] : [option, value];
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(defaultValue: "authored")] }), tokens, []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Message"].Should().BeEmpty();
        result.ExplicitParameters.Should().Contain("Message");
    }

    [Fact]
    public void Parse_BareBoolean_IsExplicitTrue()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter("Flag", "bool", "false")] }), ["--Flag"], []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Flag"].Should().Be("true");
        result.ExplicitParameters.Should().Contain("Flag");
    }

    [Fact]
    public void Parse_ExplicitValueEqualToDefault_DoesNotLoseProvenance()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter("Flag", "bool", "false")] });

        var omitted = _parser.Parse(descriptor, [], []);
        var supplied = _parser.Parse(descriptor, ["--Flag=false"], []);

        omitted.ValuesByCanonicalName.Should().BeEquivalentTo(supplied.ValuesByCanonicalName);
        omitted.ExplicitParameters.Should().BeEmpty();
        supplied.ExplicitParameters.Should().ContainSingle().Which.Should().Be("Flag");
    }

    [Fact]
    public void Parse_RequiredAuthoredDefaults_RetainsTheirSpellingAndDoesNotMarkThemExplicit()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter("Number", "int", "+001", required: true), Parameter("Flag", "bool", "False", required: true), Parameter(defaultValue: "", required: true)],
        });

        var result = _parser.Parse(descriptor, [], []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Number"] = "+001",
            ["Flag"] = "False",
            ["Message"] = "",
        });
        result.ExplicitParameters.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingRequiredValue_IsSelectableButIncomplete()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(required: true), Parameter("Optional")] }), [], []);

        result.IsArgumentCompatible.Should().BeTrue();
        result.IsComplete.Should().BeFalse();
        result.MissingRequiredParameters.Should().ContainSingle().Which.CanonicalName.Should().Be("Message");
        result.ValuesByCanonicalName.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--Message=one", "--Message=two")]
    [InlineData("--Message=one", "-m=one")]
    [InlineData("-m=one", "-m=two")]
    public void Parse_DuplicateOccurrencesIncludingDifferentAliases_RejectsRatherThanTakingLastValue(string first, string second)
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter(defaultValue: "authored") with { ShortName = "m" }] });

        var result = _parser.Parse(descriptor, [first, second], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.Diagnostics.Should().Contain(message => message.Contains("more than once", StringComparison.Ordinal));
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
    }

    [Fact]
    public void Parse_RepeatedBareBoolean_RejectsEvenWithoutValueTokens()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter("Flag", "bool")] }), ["--Flag", "--Flag"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.Diagnostics.Should().Contain(message => message.Contains("more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--message")]
    [InlineData("positional")]
    [InlineData("--help")]
    [InlineData("-?")]
    [InlineData("@C:\\not-a-response-file")]
    [InlineData("[parse]")]
    [InlineData("--")]
    [InlineData("--Message:value")]
    public void Parse_UnknownOptionOrUnmatchedToken_IsNotAcceptedAsADefault(string token)
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(defaultValue: "authored")] }), [token], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_OptionWithoutValue_DoesNotSubstituteItsAuthoredDefault()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(defaultValue: "authored")] }), ["--Message"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
        result.ExplicitParameters.Should().Contain("Message");
    }

    [Theory]
    [InlineData("bool", "")]
    [InlineData("bool", "yes")]
    [InlineData("bool", " false ")]
    [InlineData("int", "1.5")]
    [InlineData("int", " 1")]
    [InlineData("int", "2147483648")]
    [InlineData("float", "NaN")]
    [InlineData("float", "Infinity")]
    [InlineData("float", "1e999")]
    [InlineData("float", "1,5")]
    public void Parse_InvalidExplicitType_DoesNotFallBackToDefault(string type, string value)
    {
        string defaultValue = type == "bool" ? "true" : "1";
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(dataType: type, defaultValue: defaultValue)] }), [$"--Message={value}"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
    }

    [Fact]
    public void Parse_InvalidChoice_DoesNotUseDefaultOrFirstChoice()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter(dataType: "choice", defaultValue: "Hot") with { Choices = [new("Hot", null, null), new("Cold", null, null)] }],
        });

        var result = _parser.Parse(descriptor, ["--Message=Other"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
    }

    [Theory]
    [InlineData("--param:Canonical")]
    [InlineData("-p:p")]
    public void Parse_ReservedLongAndShortAliases_UsesTheSpecifiedFallbacks(string alias)
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter("Canonical") with { LongName = "path", ShortName = "p" }] });

        var result = _parser.Parse(descriptor, [alias, "value"], ["--path", "-p"]);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Canonical"].Should().Be("value");
        result.AliasesByCanonicalName["Canonical"].Should().Equal("--param:Canonical", "-p:p");
    }

    [Theory]
    [InlineData("--param:Canonical=value", "value")]
    [InlineData("-p:p=", "")]
    [InlineData("--param:Canonical=--other=value", "--other=value")]
    public void Parse_InlineFallbackAssignment_PreservesTheWholeValue(string token, string expected)
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter("Canonical") with { LongName = "path", ShortName = "p" }] });

        var result = _parser.Parse(descriptor, [token], ["--path", "-p"]);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Canonical"].Should().Be(expected);
        result.ExplicitParameters.Should().Contain("Canonical");
    }

    [Theory]
    [InlineData("--Other=value")]
    [InlineData("a=b=c")]
    [InlineData("")]
    public void Parse_InlineAssignment_PreservesEmptyEqualsAndOptionLookingLiteralValues(string value)
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter(), Parameter("Other")] });

        var result = _parser.Parse(descriptor, [$"--Message={value}"], []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Message"].Should().Be(value);
        result.ValuesByCanonicalName.Should().NotContainKey("Other");
    }

    [Theory]
    [InlineData("[parse]")]
    [InlineData("@C:\\not-a-response-file")]
    public void Parse_DirectiveOrResponseLookingStringValue_RemainsLiteral(string value)
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter()] }), ["--Message", value], []);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Message"].Should().Be(value);
    }

    [Fact]
    public void Parse_UnknownOptionAfterValueTakingAlias_IsNotConsumedAsAStringValue()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter(defaultValue: "authored")] }), ["--Message", "--unknown"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
        result.Diagnostics.Should().Contain(message => message.Contains("requires a value", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(message => message.Contains("--unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_NumericShortAliasAfterNumericParameter_IsNotConsumedAsANegativeValue()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter("Number", "int"), Parameter("Flag", "bool") with { ShortName = "1" }],
        });

        var result = _parser.Parse(descriptor, ["--Number", "-1"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Number");
        result.ValuesByCanonicalName["Flag"].Should().Be("true");
        result.ExplicitParameters.Should().Contain("Number").And.Contain("Flag");
    }

    [Theory]
    [InlineData("install")]
    [InlineData("update")]
    [InlineData("uninstall")]
    public void Parse_ReservedSelectorWordAsStringValue_RemainsAValue(string value)
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter()] }), ["--Message", value], [value]);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Message"].Should().Be(value);
    }

    [Fact]
    public void Parse_ReservedDirectAlias_RemainsUnavailableToTemplateTokens()
    {
        var result = _parser.Parse(new(Metadata() with { Parameters = [Parameter() with { LongName = "path" }] }), ["--path=value"], ["--path"]);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ValuesByCanonicalName.Should().NotContainKey("Message");
    }

    [Fact]
    public void Parse_ReservedFallback_ReportsMetadataFailureInsteadOfInventingAnotherAlias()
    {
        Action action = () => _parser.Parse(new(Metadata() with { Parameters = [Parameter() with { LongName = "path" }] }), [], ["--path", "--param:Message"]);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*already reserved or assigned*");
    }

    [Fact]
    public void Parse_OpaqueCanonicalNameCannotFormRequiredFallback_ReportsMetadataFailure()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter("opaque:symbol") with { LongName = "path" }] });
        var direct = _parser.Parse(descriptor, ["--path=value"], []);

        Action fallback = () => _parser.Parse(descriptor, [], ["--path"]);

        direct.ValuesByCanonicalName.Should().ContainKey("opaque:symbol");
        fallback.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*fallback alias*");
    }

    [Fact]
    public void Parse_HiddenOptionalParameter_RemainsParseableWithoutInventingAShortAlias()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter() with { IsHidden = true }] });

        var valid = _parser.Parse(descriptor, ["--Message=value"], []);
        var unknownShort = _parser.Parse(descriptor, ["-M=value"], []);

        valid.IsComplete.Should().BeTrue();
        valid.AliasesByCanonicalName["Message"].Should().Equal("--Message");
        unknownShort.IsArgumentCompatible.Should().BeFalse();
    }

    [Fact]
    public void Parse_PosixBundle_IsNotExpandedIntoTwoParameters()
    {
        var descriptor = new TemplateDescriptor(Metadata() with
        {
            Parameters = [Parameter("Alpha", "bool") with { ShortName = "a" }, Parameter("Beta", "bool") with { ShortName = "b" }],
        });

        var result = _parser.Parse(descriptor, ["-ab"], []);

        result.IsArgumentCompatible.Should().BeFalse();
        result.ExplicitParameters.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RepeatedCalls_DoNotRetainAliasesValuesOrOptions()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter()] });
        var first = _parser.Parse(descriptor, ["--param:Message=first"], ["--Message"]);
        var second = _parser.Parse(descriptor, ["--Message=second"], []);
        var emptySchema = _parser.Parse(new(Metadata("Other")), ["--Message=third"], []);
        var omitted = _parser.Parse(descriptor, [], []);

        first.ValuesByCanonicalName["Message"].Should().Be("first");
        second.ValuesByCanonicalName["Message"].Should().Be("second");
        second.AliasesByCanonicalName["Message"].Should().Equal("--Message");
        emptySchema.IsArgumentCompatible.Should().BeFalse();
        omitted.ValuesByCanonicalName.Should().BeEmpty();
        omitted.ExplicitParameters.Should().BeEmpty();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void Parse_CultureChanges_DoNotChangeNumericOrChoiceValues(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var descriptor = new TemplateDescriptor(Metadata() with
            {
                Parameters =
                [
                    Parameter("Number", "float"),
                    Parameter("Choice", "choice") with { Choices = [new("ITEM", null, null)] },
                ],
            });

            var result = _parser.Parse(descriptor, ["--Number=1.5", "--Choice=item"], []);

            result.IsComplete.Should().BeTrue();
            result.ValuesByCanonicalName["Number"].Should().Be("1.5");
            result.ValuesByCanonicalName["Choice"].Should().Be("ITEM");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Parse_InvalidMethodArguments_ThrowRatherThanReturningAnArgumentCompatibleResult()
    {
        var descriptor = new TemplateDescriptor(Metadata());
        Action template = () => _parser.Parse(null!, [], []);
        Action tokens = () => _parser.Parse(descriptor, default, []);
        Action token = () => _parser.Parse(descriptor, [null!], []);
        Action reserved = () => _parser.Parse(descriptor, [], default);
        Action reservedEntry = () => _parser.Parse(descriptor, [], [" "]);

        template.Should().Throw<ArgumentNullException>().WithParameterName("template");
        tokens.Should().Throw<ArgumentException>().WithParameterName("tokens");
        token.Should().Throw<ArgumentException>().WithParameterName("tokens");
        reserved.Should().Throw<ArgumentException>().WithParameterName("reservedAliases");
        reservedEntry.Should().Throw<ArgumentException>().WithParameterName("reservedAliases");
    }

    [Fact]
    public void Parse_ResultState_IsImmutable()
    {
        var descriptor = new TemplateDescriptor(Metadata() with { Parameters = [Parameter()] });
        var result = _parser.Parse(descriptor, ["--Message=value"], []);

        Action values = () => ((IDictionary<string, string>)result.ValuesByCanonicalName).Clear();
        Action aliases = () => ((IDictionary<string, ImmutableArray<string>>)result.AliasesByCanonicalName).Clear();
        var changedProvenance = result.ExplicitParameters.Add("Other");

        values.Should().Throw<NotSupportedException>();
        aliases.Should().Throw<NotSupportedException>();
        result.ExplicitParameters.Should().NotContain("Other");
        changedProvenance.Should().Contain("Other");
    }
}
