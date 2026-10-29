// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration.Native;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class NativeTemplateMetadataTests
{
    private const string Prefix = """{"identity":"Native.Item","name":"Native item","shortName":"native-item","tags":{"type":"item"}""";

    [Theory]
    [InlineData("""{"type":"func-project-capabilities","args":["azure.functions.node.model-v4.javascript-commonjs"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["Future.Model","future.model"]}""")]
    [InlineData("""{"\u0074ype":"func-project-capabilities","\u0061rgs":["future.model"]}""")]
    public void Read_ExactItemCarrier_ProjectsThroughTheAcceptedNeutralParser(string declaration)
    {
        var metadata = Read(",\"funcProjectCapabilities\":" + declaration);

        var requirements = ItemTemplateRequirements.Parse(metadata.RequirementDeclaration);

        requirements.IsDeclared.Should().BeTrue();
        requirements.CapabilityIds.Should().Equal(ItemTemplateRequirements.Parse(declaration).CapabilityIds);
    }

    [Fact]
    public void Read_AbsentCarrier_DoesNotInferRequirements()
    {
        var metadata = Read();

        metadata.RequirementDeclaration.Should().BeNull();
        ItemTemplateRequirements.Parse(metadata.RequirementDeclaration).IsDeclared.Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"args":["future.model"]}""")]
    [InlineData("""{"type":"func-project-capabilities"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":"future.model"}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[42]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[null]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["bad/id"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":[" future.model"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["future.model","future.model"]}""")]
    [InlineData("""{"type":"func-project-capabilities","type":"func-project-capabilities","args":["future.model"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["future.model"],"\u0061rgs":["other"]}""")]
    [InlineData("""{"type":"func-project-capabilities","Type":"func-project-capabilities","args":["future.model"]}""")]
    public void Read_MalformedOrAmbiguousInnerDeclaration_FailsWithMetadataContext(string declaration)
    {
        var action = () => Read(",\"funcProjectCapabilities\":" + declaration);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*content/item/.template.config/template.json*");
    }

    [Theory]
    [InlineData("""{"type":"another-vocabulary","args":["future.model"]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["future.model"],"alternatives":[]}""")]
    [InlineData("""{"Type":"func-project-capabilities","args":["future.model"]}""")]
    public void Read_UnsupportedInnerSyntax_RejectsTheRetainedValidationFailure(string declaration)
    {
        var action = () => Read(",\"funcProjectCapabilities\":" + declaration);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*funcProjectCapabilities*");
    }

    [Theory]
    [InlineData("funcProjectCapabilities")]
    [InlineData("FuncProjectCapabilities")]
    [InlineData("funcProjectCapabilit\\u0069es")]
    public void Read_RepeatedOrCaseVariantDecodedRootCarrier_IsRejectedBeforeNormalization(string secondName)
    {
        var declaration = """{"type":"func-project-capabilities","args":["future.model"]}""";
        var action = () => Read($",\"funcProjectCapabilities\":{declaration},\"{secondName}\":{declaration}");

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*Duplicate or ambiguous decoded property*");
    }

    [Fact]
    public void Read_SoleAlternateCarrierCasing_IsNotASupportedAlias()
    {
        var action = () => Read(""","FuncProjectCapabilities":{"type":"func-project-capabilities","args":["future.model"]}""");

        action.Should().Throw<NotSupportedException>().WithMessage("*alternate carrier casing*");
    }

    [Fact]
    public void Read_EncodedExactCarrierName_IsRecognizedAfterJsonDecoding()
    {
        var metadata = Read(""","funcProjectCapabilit\u0069es":{"type":"func-project-capabilities","args":["future.model"]}""");

        ItemTemplateRequirements.Parse(metadata.RequirementDeclaration).CapabilityIds.Should().Equal("future.model");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"type":"func-project-capabilities","args":["future.model"]}""")]
    public void Read_ProjectCarrierMisuse_IsRejectedEvenWhenTheValueWouldOtherwiseBeValid(string declaration)
    {
        string configuration = $$"""{"identity":"Native.Project","name":"Project","shortName":"project","tags":{"type":"project"},"funcProjectCapabilities":{{declaration}}}""";

        var action = () => new NativeTemplateMetadata("project/.template.config/template.json", configuration, null);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*item-only*forbidden on project*");
    }

    [Fact]
    public void Read_MalformedJson_PreservesParsingFailureAsInnerException()
    {
        var action = () => new NativeTemplateMetadata("item/template.json", "{", null);

        action.Should().Throw<InvalidTemplateMetadataException>().WithInnerException<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Read_EmptyAuthoredDefault_RetainsItsExactValue()
    {
        var metadata = Read(""","symbols":{"Message":{"type":"parameter","datatype":"string","defaultValue":""}}""");

        metadata.ParameterDefaults["Message"].Should().BeEmpty();
    }

    [Fact]
    public void Read_DuplicateDecodedHostAlias_IsRejectedBeforeHostProjection()
    {
        var host = """{"symbolInfo":{"Message":{"longName":"message","\u006congName":"other"}}}""";

        var action = () => new NativeTemplateMetadata("content/item/.template.config/template.json", Prefix + "}", host);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*Duplicate or ambiguous decoded property*");
    }

    [Theory]
    [InlineData(""","precedence":"10" """)]
    [InlineData(""","symbols":{"Bad":{"type":"parameter","datatype":null}}""")]
    [InlineData(""","symbols":{"Bad":{"type":"parameter","choices":[{"choice":"a"},{"choice":"A"}]}}""")]
    public void Read_LossyNativeScalarOrChoiceNormalization_IsRejectedAtTheOriginalBoundary(string suffix)
    {
        var action = () => Read(suffix);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("""{"identity":"\uD800","name":"x","shortName":"x","tags":{"type":"item"}}""")]
    [InlineData("""{"identity":"x","name":"x","shortName":"x","tags":{"type":"item"},"\uD800":0}""")]
    public void Read_InvalidDecodedUnicode_HasAnOriginalMetadataFailure(string configuration)
    {
        var action = () => new NativeTemplateMetadata("item/.template.config/template.json", configuration, null);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*item/.template.config/template.json*");
    }

    [Fact]
    public void Read_CaseDistinctOpaqueSymbolNames_AreNeitherFoldedNorMerged()
    {
        var metadata = Read(
            ""","symbols":{"Value":{"type":"parameter","defaultValue":"upper"},"value":{"type":"parameter","defaultValue":"lower"}}""");

        metadata.ParameterDefaults.Should().HaveCount(2);
        metadata.ParameterDefaults["Value"].Should().Be("upper");
        metadata.ParameterDefaults["value"].Should().Be("lower");
    }

    [Theory]
    [InlineData("func-role", "null")]
    [InlineData("func-role", "42")]
    [InlineData("func-role", "true")]
    [InlineData("func-role", "[]")]
    [InlineData("func-role", "{}")]
    [InlineData("func-role", "\"\"")]
    [InlineData("func-role", "\" \"")]
    [InlineData("func-role", "\" private-helper\"")]
    [InlineData("func-role", "\"private-helper \"")]
    [InlineData("Func-Role", "\"private-helper\"")]
    public void Read_MalformedInternalRole_RejectsWithOriginalMetadataContext(string property, string value)
    {
        string configuration = $$$"""
            {"identity":"Native.Item","name":"Native item","shortName":"native-item","tags":{"type":"item","{{{property}}}":{{{value}}}}}
            """;

        var action = () => new NativeTemplateMetadata("content/item/.template.config/template.json", configuration, null);

        action.Should().Throw<InvalidTemplateMetadataException>()
            .WithMessage("*content/item/.template.config/template.json*func-role*");
    }

    [Theory]
    [InlineData("func-role")]
    [InlineData("func-r\\u006fle")]
    [InlineData("Func-Role")]
    public void Read_DuplicateDecodedInternalRole_IsRejectedBeforeNormalization(string secondProperty)
    {
        string configuration = $$$"""
            {"identity":"Native.Item","name":"Native item","shortName":"native-item",
             "tags":{"type":"item","func-role":"private-helper","{{{secondProperty}}}":"another-helper"}}
            """;

        var action = () => new NativeTemplateMetadata("content/item/.template.config/template.json", configuration, null);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*Duplicate or ambiguous decoded property*");
    }

    private static NativeTemplateMetadata Read(string suffix = "")
        => new("content/item/.template.config/template.json", Prefix + suffix + "}", null);
}
