// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateConstraintDeclarationReaderTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"constraints\":{}}")]
    [InlineData("{/* comment */\"constraints\":{},}")]
    public void Read_NoConstraints_ReturnsEmpty(string configuration)
    {
        TemplateConstraintDeclarationReader.Read(configuration).Should().BeEmpty();
    }

    [Fact]
    public void Read_WorkloadAlternatives_ReturnsTypedRequirements()
    {
        const string Configuration = """{"constraints":{"stacks":{"type":"func-workload","args":["node",{"id":"python","version":"[1.0,2.0)"}]}}}""";

        var declarations = TemplateConstraintDeclarationReader.Read(Configuration);

        declarations.Should().ContainSingle();
        declarations[0].Label.Should().Be("stacks");
        declarations[0].Alternatives.Select(item => item.Identity).Should().Equal("node", "python");
        declarations[0].Alternatives[1].Version!.ToNormalizedString().Should().Be("[1.0.0, 2.0.0)");
        declarations[0].Arguments!.Value.GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("\"[4.20,)\"", null)]
    [InlineData("{\"id\":\"Microsoft.Azure.Functions.ExtensionBundle.Preview\",\"version\":\"[4.29,)\"}", "Microsoft.Azure.Functions.ExtensionBundle.Preview")]
    [InlineData("{}", null)]
    public void Read_BundleArgument_ReturnsRequirement(string argument, string? identity)
    {
        string configuration = JsonSerializer.Serialize(new
        {
            constraints = new { bundle = new { type = "func-bundle", args = JsonSerializer.Deserialize<JsonElement>(argument) } },
        });
        var declarations = TemplateConstraintDeclarationReader.Read(configuration);

        declarations[0].Alternatives.Should().ContainSingle();
        declarations[0].Alternatives[0].Identity.Should().Be(identity);
    }

    [Fact]
    public void Read_UnknownType_PreservesArgumentsWithoutEvaluating()
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"future":{"type":"custom-future","args":{"feature":true}}}}""");

        declarations[0].Type.Should().Be("custom-future");
        declarations[0].Alternatives.Should().BeEmpty();
        declarations[0].Arguments!.Value.GetProperty("feature").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"constraints\":{},\"constraints\":{}}")]
    [InlineData("{\"constraints\":{},\"Constraints\":{}}")]
    [InlineData("{\"constraints\":{\"same\":{\"type\":\"host\"},\"same\":{\"type\":\"os\"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"host\",\"type\":\"os\"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"host\",\"args\":[],\"args\":{}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":{\"id\":\"node\",\"id\":\"python\"}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":{\"id\":\"node\",\"version\":\"1\",\"version\":\"2\"}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"future\",\"args\":{\"nested\":{\"key\":1,\"key\":2}}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"host\",\"t\\u0079pe\":\"os\"}}}")]
    public void Read_DuplicateProperties_Throws(string configuration)
    {
        Action action = () => TemplateConstraintDeclarationReader.Read(configuration);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"\\uD800\"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":\"\\uDC00\"}}}")]
    [InlineData("{\"constraints\":{\"\\uD800\":{\"type\":\"host\"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"future\",\"args\":{\"value\":\"\\uD800\"}}}}")]
    [InlineData("{\"\\uD800\":{}}")]
    [InlineData("{\"tags\":{\"type\":\"\\uD800\"},\"constraints\":{\"x\":{\"type\":\"func-bundle\",\"args\":\"1.0\"}}}")]
    public void Read_InvalidUnicode_ThrowsMetadataException(string configuration)
    {
        Action action = () => TemplateConstraintDeclarationReader.Read(configuration);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_DistinctObjectAlternatives_AcceptsRepeatedFieldNames()
    {
        const string Configuration = """{"constraints":{"stacks":{"type":"func-workload","args":[{"id":"node","version":"1.0"},{"id":"python","version":"2.0"}]}}}""";

        var declarations = TemplateConstraintDeclarationReader.Read(Configuration, ["FuncTemplate"]);

        declarations[0].Alternatives.Select(item => item.Identity).Should().Equal("node", "python");
        declarations[0].Alternatives[0].Version!.MinVersion!.ToNormalizedString().Should().Be("1.0.0");
        declarations[0].Alternatives[1].Version!.MinVersion!.ToNormalizedString().Should().Be("2.0.0");
    }

    [Fact]
    public void Read_TypeValueCase_IsPreservedAsUnknown()
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"x":{"type":"Func-workload"}}}""");

        declarations[0].Type.Should().Be("Func-workload");
        declarations[0].Alternatives.Should().BeEmpty();
    }

    [Fact]
    public void Read_UnknownFuncTypeInSharedPackage_Throws()
    {
        Action action = () => TemplateConstraintDeclarationReader.Read("""{"constraints":{"x":{"type":"func-future"}}}""", ["FuncTemplate", "template"]);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_ItemTemplateBundle_ReturnsParsedRange()
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"tags":{"type":"item"},"constraints":{"x":{"type":"func-bundle","args":"[4.20,5.0)"}}}""");

        declarations[0].Alternatives[0].Version!.MinVersion!.ToNormalizedString().Should().Be("4.20.0");
        declarations[0].Alternatives[0].Version!.MaxVersion!.ToNormalizedString().Should().Be("5.0.0");
        declarations[0].Alternatives[0].Version!.IsMaxInclusive.Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"constraints\":null}")]
    [InlineData("{\"constraints\":[]}")]
    [InlineData("{\"constraints\":{\"x\":null}}")]
    [InlineData("{\"constraints\":{\"x\":\"node\"}}")]
    [InlineData("{\"constraints\":{\"x\":{}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":3}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\" \"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\"}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":[]}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":null}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":{}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":{\"id\":\"node\",\"version\":false}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-workload\",\"args\":{\"id\":\"node\",\"version\":\"bad range\"}}}}")]
    [InlineData("{\"constraints\":{\"x\":{\"type\":\"func-bundle\",\"args\":true}}}")]
    public void Read_MalformedDeclarations_Throws(string configuration)
    {
        Action action = () => TemplateConstraintDeclarationReader.Read(configuration);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("-node")]
    [InlineData("node other")]
    [InlineData("worker.nupkg")]
    [InlineData("worker.NUPKG")]
    [InlineData("/node")]
    public void Read_InvalidWorkloadName_Throws(string name)
    {
        string configuration = JsonSerializer.Serialize(new
        {
            constraints = new { requirement = new { type = "func-workload", args = name } },
        });
        Action action = () => TemplateConstraintDeclarationReader.Read(configuration);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_SharedPackageWithFuncType_Throws()
    {
        Action action = () => TemplateConstraintDeclarationReader.Read("""{"constraints":{"x":{"type":"func-workload","args":"node"}}}""", ["FuncTemplate", "Template"]);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_SharedPackageWithoutFuncType_Accepts()
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"x":{"type":"host","args":{}}}}""", ["FuncTemplate", "Template"]);

        declarations.Should().ContainSingle();
    }

    [Fact]
    public void Read_ProjectTemplateWithBundle_Throws()
    {
        Action action = () => TemplateConstraintDeclarationReader.Read("""{"tags":{"type":"project"},"constraints":{"x":{"type":"func-bundle","args":"1.0"}}}""");

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_InvalidJson_PreservesJsonCause()
    {
        Action action = () => TemplateConstraintDeclarationReader.Read("{");

        action.Should().Throw<InvalidTemplateMetadataException>().WithInnerException<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Read_NullConfiguration_Throws()
    {
        Action action = () => TemplateConstraintDeclarationReader.Read(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}