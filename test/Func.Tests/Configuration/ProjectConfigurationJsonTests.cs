// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class ProjectConfigurationJsonTests
{
    [Theory]
    [InlineData(null, null, "{}")]
    [InlineData("node", null, """{"stack":{"runtime":"node"}}""")]
    [InlineData(null, "JavaScript", """{"stack":{"language":"JavaScript"}}""")]
    [InlineData("node", "JavaScript", """{"stack":{"runtime":"node","language":"JavaScript"}}""")]
    public void Create_PreservesPublicOptionalFieldsAndCanonicalInputs(string? runtime, string? language, string expected)
    {
        string actual = ProjectConfigurationJson.Create(runtime, language);

        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)).Should().BeTrue();
    }

    [Fact]
    public void MergeLanguage_PreservesRuntimeAndUnrelatedFieldsUnlessCanonicalRuntimeIsSupplied()
    {
        const string existing = """{"stack":{"runtime":"NODE","unknown":true},"defaultProfile":"custom","profiles":["custom"],"extra":[1,2]}""";
        var expected = JsonNode.Parse(existing)!;
        expected["stack"]!["language"] = "JavaScript";

        string publicMerge = ProjectConfigurationJson.MergeLanguage(existing, "JavaScript");
        string stagedMerge = ProjectConfigurationJson.MergeLanguage(existing, "JavaScript", "node");

        JsonNode.DeepEquals(JsonNode.Parse(publicMerge), expected).Should().BeTrue();
        expected["stack"]!["runtime"] = "node";
        JsonNode.DeepEquals(JsonNode.Parse(stagedMerge), expected).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MergeLanguage_MissingLanguage_RejectsBeforeSerialization(string? language)
    {
        Action act = () => ProjectConfigurationJson.MergeLanguage("""{"stack":{}}""", language!);

        act.Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Fact]
    public void MergeLanguage_NullInput_RejectsProgrammerError()
    {
        Action act = () => ProjectConfigurationJson.MergeLanguage(null!, "JavaScript");

        act.Should().Throw<ArgumentNullException>().WithParameterName("existing");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"stack":[]}""")]
    public void MergeLanguage_InvalidInput_DoesNotInventAnEmptyConfiguration(string input)
    {
        Action act = () => ProjectConfigurationJson.MergeLanguage(input, "JavaScript");

        act.Should().Throw<JsonException>();
    }
}
