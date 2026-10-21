// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateConfigurationSnapshotTests
{
    [Fact]
    public void Constructor_ParsedView_RemainsOwnedAndPreservesRawContent()
    {
        const string content = """{/* captured */"tags":{"type":"item",},"constraints":{},}""";

        TemplateConfigurationSnapshot snapshot = new(content, "raw-byte-fingerprint", TemplateType.Item, []);

        snapshot.Root.GetProperty("tags").GetProperty("type").GetString().Should().Be("item");
        snapshot.Root.GetProperty("constraints").EnumerateObject().Should().BeEmpty();
        snapshot.Content.Should().Be(content);
        snapshot.Fingerprint.Should().Be("raw-byte-fingerprint");
        snapshot.Root.Should().Be(snapshot.Root);
        TemplateConstraintDeclarationReader.Read(snapshot.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("")]
    public void Constructor_InvalidCapturedJson_PreservesMetadataExceptionAndCause(string content)
    {
        Action capture = () => _ = new TemplateConfigurationSnapshot(content, "fingerprint", TemplateType.Item, []);

        capture.Should().Throw<InvalidTemplateMetadataException>()
            .WithMessage("template.json is not valid JSON.").WithInnerException<JsonException>();
    }

    [Fact]
    public void Constructor_NullContent_Throws()
    {
        Action capture = () => _ = new TemplateConfigurationSnapshot(null!, "fingerprint", TemplateType.Item, []);

        capture.Should().Throw<ArgumentNullException>().WithParameterName("Content");
    }

    [Theory]
    [InlineData("""{"constraints":{"stack":{"type":"func-workload","Type":"func-workload","args":"node"}}}""")]
    [InlineData("""{"constraints":{"stack":{"type":"func-workload","args":"node"},"stack":{"type":"func-workload","args":"node"}}}""")]
    [InlineData("[]")]
    public void Read_ParsedView_PreservesRawDeclarationValidation(string content)
    {
        TemplateConfigurationSnapshot snapshot = new(content, "fingerprint", TemplateType.Item, []);
        Action raw = () => TemplateConstraintDeclarationReader.Read(content);
        Action captured = () => TemplateConstraintDeclarationReader.Read(snapshot.Root);

        var expected = raw.Should().Throw<InvalidTemplateMetadataException>();
        captured.Should().Throw<InvalidTemplateMetadataException>().WithMessage(expected.Which.Message);
    }

    [Fact]
    public void Read_CaseDistinctLabels_PreservesBothDeclarations()
    {
        const string content = """{"constraints":{"stack":{"type":"func-workload","args":"node"},"STACK":{"type":"func-workload","args":"node"}}}""";
        TemplateConfigurationSnapshot snapshot = new(content, "fingerprint", TemplateType.Item, []);

        var declarations = TemplateConstraintDeclarationReader.Read(snapshot.Root);

        declarations.Select(declaration => declaration.Label).Should().Equal("stack", "STACK");
        declarations.Should().OnlyContain(declaration => declaration.Type == "func-workload" && declaration.Arguments!.Value.GetRawText() == "\"node\"");
        declarations.Select(declaration => (declaration.Label, declaration.Type, declaration.Arguments!.Value.GetRawText()))
            .Should().Equal(TemplateConstraintDeclarationReader.Read(content)
                .Select(declaration => (declaration.Label, declaration.Type, declaration.Arguments!.Value.GetRawText())));
    }
}
