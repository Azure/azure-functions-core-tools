// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplateArgumentParserExplicitParameterTests
{
    [Theory]
    [InlineData("--target-framework", "net8.0")]
    [InlineData("--target-framework=net8.0")]
    [InlineData("-tfm:net8.0")]
    public void Parse_ExplicitAuthoredDefaultValue_RetainsCanonicalExplicitOrigin(params string[] tokens)
    {
        var result = Parse(tokens);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName.Should().Contain("Framework", "net8.0");
        result.ExplicitParameterNames.Should().BeEquivalentTo("Framework");
    }

    [Fact]
    public void Parse_OmittedOrCompletedValue_IsNotExplicitInput()
    {
        var omitted = Parse([]);
        var completed = Parse([], new Dictionary<string, string?> { ["Framework"] = "net10.0" });

        omitted.ValuesByCanonicalName.Should().Contain("Framework", "net8.0");
        completed.ValuesByCanonicalName.Should().Contain("Framework", "net10.0");
        omitted.ExplicitParameterNames.Should().BeEmpty();
        completed.ExplicitParameterNames.Should().BeEmpty();
    }

    [Fact]
    public void Parse_InvalidExplicitValue_RemainsExplicitAndCannotBeReplacedByCompletion()
    {
        var result = Parse(["-tfm", "net7.0"], new Dictionary<string, string?> { ["Framework"] = "net10.0" });

        result.IsComplete.Should().BeFalse();
        result.ExplicitParameterNames.Should().Contain("Framework");
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("cannot replace explicit input", StringComparison.Ordinal));
    }

    private static TemplateCandidateParseResult Parse(IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string?>? completed = null) =>
        new TemplateArgumentParser().Parse(new("Source", [new TemplateParameterDefinition("Framework", "target-framework", "tfm",
            "Framework", "choice", [new("net8.0", null, null), new("net10.0", null, null)], "net8.0", false, false, false)],
            new HashSet<string>(), tokens, completed));
}
