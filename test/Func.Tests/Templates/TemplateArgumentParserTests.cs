// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Globalization;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplateArgumentParserTests
{
    private readonly ITemplateArgumentParser _parser = new TemplateArgumentParser();

    [Theory]
    [InlineData("--greeting", "two words")]
    [InlineData("-g", "x=y:z")]
    [InlineData("--greeting", "")]
    [InlineData("--greeting", "@not-a-response-file")]
    public void Parse_ExplicitValue_PreservesTokensAndCanonicalIdentity(string alias, string value)
    {
        string[] tokens = [alias, value];
        string[] before = [.. tokens];
        var request = Request([Parameter("Greeting", "greeting", "g")], tokens);

        var result = _parser.Parse(request);

        result.IsComplete.Should().BeTrue();
        result.TemplateIdentity.Should().Be("Candidate");
        result.ValuesByCanonicalName.Should().BeEquivalentTo(new Dictionary<string, string?> { ["Greeting"] = value });
        result.Parameters.Should().ContainSingle().Which.LongAlias.Should().Be("--greeting");
        tokens.Should().Equal(before);
    }

    [Theory]
    [InlineData("--greeting=hello=world", "hello=world")]
    [InlineData("--greeting:hello", "hello")]
    [InlineData("--greeting=-literal", "-literal")]
    [InlineData("--greeting=--flag", "--flag")]
    [InlineData("--greeting=", "")]
    [InlineData("--greeting=[suggest]", "[suggest]")]
    [InlineData("--greeting=--template-parameter-0", "--template-parameter-0")]
    [InlineData("--greeting=@not-a-response-file", "@not-a-response-file")]
    public void Parse_InlineValue_PreservesTheValue(string token, string expected)
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "greeting")], [token]));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().Be(expected);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("positional")]
    [InlineData("@not-a-response-file")]
    [InlineData("--")]
    [InlineData("[parse]")]
    [InlineData("[suggest]")]
    [InlineData("--template-parameter-0=value")]
    public void Parse_UnrecognizedToken_DoesNotDisappear(string token)
    {
        var result = _parser.Parse(Request([Parameter("Mode", "mode")], [token]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
        result.ValuesByCanonicalName.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Parse_ExecutablePrefixInTemplateTail_IsNotDiscarded(bool fullPath, bool validOptionFollows)
    {
        string prefix = fullPath ? RootCommand.ExecutablePath : RootCommand.ExecutableName;
        string[] tokens = validOptionFollows ? [prefix, "--greeting", "hello"] : [prefix];
        var request = Request([Parameter("Greeting", "greeting", defaultValue: "default")], tokens);

        var result = _parser.Parse(request);

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
        tokens[0].Should().Be(prefix);
    }

    [Fact]
    public void Parse_DirectiveLookingValue_IsLiteralWithoutParserActions()
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "greeting")], ["--greeting", "[suggest]"]));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().Be("[suggest]");
    }

    [Theory]
    [InlineData("--mode")]
    [InlineData("--mode", "--unknown")]
    [InlineData("--mode", "first", "--mode", "second")]
    [InlineData("--mode", "first", "-m", "second")]
    [InlineData("--mode", "first", "extra")]
    public void Parse_MissingRepeatedOrExtraValue_IsInvalid(params string[] tokens)
    {
        var result = _parser.Parse(Request([Parameter("Mode", "mode", "m", defaultValue: "fallback")], tokens));

        result.IsComplete.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_ExplicitInvalidValue_DoesNotUseDefaultOrPromptReplacement()
    {
        var request = Request([Choice(defaultValue: "safe")], ["--mode", "invalid"])
            with { CompletedValuesByCanonicalName = new Dictionary<string, string?> { ["Mode"] = "safe" } };

        var result = _parser.Parse(request);

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("invalid", StringComparison.Ordinal));
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("cannot replace", StringComparison.Ordinal));
        result.ValuesByCanonicalName.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--param:Mode", "safe")]
    [InlineData("--param:Mode=safe", null)]
    [InlineData("--param:Mode:safe", null)]
    [InlineData("-p:m", "safe")]
    [InlineData("-p:m=safe", null)]
    [InlineData("-p:m:safe", null)]
    public void Parse_ReservedLongAndShortAliases_UseDeterministicFallbacks(string token, string? value)
    {
        string[] tokens = value is null ? [token] : [token, value];
        var request = Request([Choice(longName: "force", shortName: "m")], tokens, ["--force", "-m"]);

        var result = _parser.Parse(request);

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Mode"].Should().Be("safe");
        result.Parameters.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TemplateArgumentParameter(request.Parameters[0], "--param:Mode", "-p:m"));
    }

    [Fact]
    public void Parse_ReservedDirectAlias_RemainsAnError()
    {
        var result = _parser.Parse(Request([Parameter("Force", "force")], ["--force", "value"], ["--force"]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("--param:Greeting=--flag")]
    [InlineData("-p:g=--flag")]
    public void Parse_FallbackInlineFlagLookingValue_IsLiteral(string token)
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "force", "g")], [token], ["--force", "-g"]));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().Be("--flag");
    }

    [Theory]
    [InlineData("--param:Greeting=")]
    [InlineData("-p:g=")]
    public void Parse_FallbackInlineEmptyValue_IsExplicit(string token)
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "force", "g")], [token], ["--force", "-g"]));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReservedFallbackRepeatedWithAnotherAlias_IsInvalid()
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "force", "g")],
            ["--param:Greeting=one", "-p:g=two"], ["--force", "-g"]));

        result.IsComplete.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_BareLifecycleReservation_AssignsFallback()
    {
        var result = _parser.Parse(Request([Parameter("Install", "install")], ["--param:Install", "value"], ["install"]));

        result.IsComplete.Should().BeTrue();
        result.Parameters[0].LongAlias.Should().Be("--param:Install");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_MissingShortAlias_DoesNotGenerateOne(string? shortName)
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "greeting", shortName)], ["-g", "value"]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.Parameters.Should().ContainSingle().Which.ShortAlias.Should().BeNull();
    }

    [Fact]
    public void Parse_InitAndNewReservations_ShareSchemaWithoutSharingState()
    {
        var parameter = Parameter("Level", "language", "l", required: true);
        var request = Request([parameter], [], ["--language", "-l"]);

        var first = _parser.Parse(request);
        var second = _parser.Parse(request with { ReservedAliases = new HashSet<string> { "--language" } });
        var third = _parser.Parse(request);

        first.MissingRequiredParameters[0].Should().BeEquivalentTo(
            new TemplateArgumentParameter(parameter, "--param:Level", "-p:l"));
        second.MissingRequiredParameters[0].ShortAlias.Should().Be("-l");
        third.Should().BeEquivalentTo(first);
        first.IsArgumentCompatible.Should().BeTrue();
        first.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_VisibleRequiredWithoutDefault_IsSelectableButIncomplete()
    {
        var result = _parser.Parse(Request([Parameter("Endpoint", "endpoint", required: true)], []));

        result.IsArgumentCompatible.Should().BeTrue();
        result.IsComplete.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().BeEmpty();
        result.MissingRequiredParameters.Should().ContainSingle().Which.LongAlias.Should().Be("--endpoint");
    }

    [Fact]
    public void Parse_Defaults_AreCanonicalAndIncludeRequiredDefaults()
    {
        var result = _parser.Parse(Request(
            [Choice(defaultValue: "safe", required: true), Parameter("Optional", "optional", defaultValue: "present")], []));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName.Should().BeEquivalentTo(new Dictionary<string, string?> { ["Mode"] = "safe", ["Optional"] = "present" });
    }

    [Fact]
    public void Parse_InvalidDefault_IsAnAuthoringFailureNotExplicitInput()
    {
        var result = _parser.Parse(Request([Choice(defaultValue: "invalid", required: true)], []));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().BeEmpty();
        result.DefinitionDiagnostics.Should().ContainSingle();
        result.MissingRequiredParameters.Should().ContainSingle();
    }

    [Fact]
    public void Parse_HiddenRequiredWithoutValue_IsAnAuthoringFailure()
    {
        var parameter = Parameter("Secret", "secret", required: true) with { IsHidden = true };

        var result = _parser.Parse(Request([parameter], []));
        var supplied = _parser.Parse(Request([parameter], ["--secret", "value"]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.DefinitionDiagnostics.Should().ContainSingle(message => message.Contains("hidden", StringComparison.Ordinal));
        supplied.IsComplete.Should().BeTrue();
        supplied.Parameters[0].Definition.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Parse_NullCompletion_DoesNotHideRequiredAuthoringFailure()
    {
        var parameter = Parameter("Secret", "secret", required: true) with { IsHidden = true };
        var request = Request([parameter], [])
            with { CompletedValuesByCanonicalName = new Dictionary<string, string?> { ["Secret"] = null } };

        var result = _parser.Parse(request);

        result.IsArgumentCompatible.Should().BeFalse();
        result.IsComplete.Should().BeFalse();
        result.DefinitionDiagnostics.Should().ContainSingle();
    }

    [Theory]
    [InlineData("safe", true)]
    [InlineData("SAFE", true)]
    [InlineData("s", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Parse_PostPromptCompletion_RevalidatesAgainstSelectedSchema(string? value, bool complete)
    {
        var request = Request([Choice(required: true)], []);
        var before = _parser.Parse(request);

        var result = _parser.Parse(request with { CompletedValuesByCanonicalName = new Dictionary<string, string?> { ["Mode"] = value } });

        before.IsArgumentCompatible.Should().BeTrue();
        before.IsComplete.Should().BeFalse();
        result.IsComplete.Should().Be(complete);
        if (complete)
        {
            result.ValuesByCanonicalName["Mode"].Should().Be(value);
        }
        else
        {
            result.MissingRequiredParameters.Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("unknown")]
    [InlineData("name")]
    public void Parse_CompletionKeys_MustBeDeclaredCanonicalNames(string key)
    {
        var request = Request([Choice(required: true)], [])
            with { CompletedValuesByCanonicalName = new Dictionary<string, string?> { [key] = "safe" } };

        var result = _parser.Parse(request);

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains(key, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("string", "")]
    [InlineData("text", "two words")]
    [InlineData("BOOL", "TRUE")]
    [InlineData("boolean", "false")]
    [InlineData("int", "9223372036854775807")]
    [InlineData("integer", "-9223372036854775808")]
    [InlineData("float", "-1.25e3")]
    [InlineData("float", "1.25e3")]
    [InlineData("hex", "0xFFFFFFFFFFFFFFFF")]
    [InlineData("hex", "0X10")]
    public void Parse_NativeScalarTypes_ValidateWithoutRewritingValues(string type, string value)
    {
        var result = _parser.Parse(Request([Parameter("Value", "value", dataType: type)], ["--value", value]));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Value"].Should().Be(value);
    }

    [Theory]
    [InlineData("bool", "1")]
    [InlineData("boolean", "yes")]
    [InlineData("int", "9223372036854775808")]
    [InlineData("integer", "1.1")]
    [InlineData("float", "wrong")]
    [InlineData("hex", "FF")]
    [InlineData("hex", "0x")]
    [InlineData("hex", "0x10000000000000000")]
    public void Parse_InvalidNativeScalarValue_IsIncompatible(string type, string value)
    {
        var result = _parser.Parse(Request([Parameter("Value", "value", dataType: type)], ["--value", value]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().ContainSingle();
        result.ValuesByCanonicalName.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BooleanWithoutValue_DoesNotInventAnUnprojectedDefault()
    {
        var result = _parser.Parse(Request([Parameter("Value", "value", dataType: "bool")], ["--value"]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_Float_AcceptsCurrentAndInvariantDecimalSeparators()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var request = Request([Parameter("Value", "value", dataType: "float")], ["--value", "1,25"]);

            var local = _parser.Parse(request);
            var invariant = _parser.Parse(request with { RawTemplateTokens = ["--value", "1.25"] });

            local.IsComplete.Should().BeTrue();
            invariant.IsComplete.Should().BeTrue();
            local.ValuesByCanonicalName["Value"].Should().Be("1,25");
            invariant.ValuesByCanonicalName["Value"].Should().Be("1.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("decimal")]
    [InlineData("custom")]
    public void Parse_UnsupportedDefinition_DoesNotGuessAStringType(string type)
    {
        var result = _parser.Parse(Request([Parameter("Value", "value", dataType: type)], []));

        result.IsArgumentCompatible.Should().BeFalse();
        result.DefinitionDiagnostics.Should().ContainSingle(message => message.Contains("unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_InvalidAliasesAndFallbackCollision_ReturnAuthoringDiagnostics()
    {
        foreach (var alias in new[] { "", "-prefixed", "white space", "colon:name", "equals=value" })
        {
            var invalid = _parser.Parse(Request([Parameter("Value", alias)], []));
            invalid.DefinitionDiagnostics.Should().ContainSingle();
        }

        var collision = _parser.Parse(Request([Parameter("Value", "force")], [], ["--force", "--param:Value"]));
        collision.IsArgumentCompatible.Should().BeFalse();
        collision.DefinitionDiagnostics.Should().ContainSingle();
    }

    [Fact]
    public void Parse_DuplicateCanonicalNamesOrAliases_AreNotOrderDependent()
    {
        TemplateParameterDefinition[][] variants =
        [
            [Parameter("Value", "one"), Parameter("Value", "two")],
            [Parameter("One", "same"), Parameter("Two", "same")],
            [Parameter("One", "one", "s"), Parameter("Two", "two", "s")],
        ];
        foreach (var definitions in variants)
        {
            _parser.Parse(Request(definitions, [])).DefinitionDiagnostics.Should().NotBeEmpty();
            _parser.Parse(Request(definitions.Reverse().ToArray(), [])).DefinitionDiagnostics.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Parse_InvalidChoices_AreNotAnEmptySuccessfulSchema()
    {
        var parameter = Choice() with { Choices = [new("safe", null, null), new("SAFE", null, null)] };

        var result = _parser.Parse(Request([parameter], []));

        result.IsArgumentCompatible.Should().BeFalse();
        result.DefinitionDiagnostics.Should().ContainSingle();
    }

    [Fact]
    public void Parse_NameDefinition_RequiresTheSettledHostBoundary()
    {
        var result = _parser.Parse(Request([Parameter("name", "name")], []));

        result.IsArgumentCompatible.Should().BeFalse();
        result.DefinitionDiagnostics.Should().ContainSingle(message => message.Contains("--name", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Result_SnapshotsCollectionsAndCannotBeMutated()
    {
        List<TemplateParameterChoice> choices = [new("safe", null, null)];
        List<TemplateParameterDefinition> definitions = [Choice() with { Choices = choices }];
        var result = _parser.Parse(Request(definitions, ["--mode", "safe"]));
        definitions.Clear();
        choices.Clear();

        result.Parameters.Should().ContainSingle().Which.Definition.Choices.Should().ContainSingle();
        Action changeValue = () => ((IDictionary<string, string?>)result.ValuesByCanonicalName)["Mode"] = "changed";
        Action changeAlias = () => ((IList<TemplateArgumentParameter>)result.Parameters).Clear();
        Action changeChoice = () => ((IList<TemplateParameterChoice>)result.Parameters[0].Definition.Choices).Clear();
        changeValue.Should().Throw<NotSupportedException>();
        changeAlias.Should().Throw<NotSupportedException>();
        changeChoice.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Parse_PreCanceled_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Action act = () => _parser.Parse(Request([], []), cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Parse_InvalidMethodArguments_ThrowSpecificContractExceptions()
    {
        var request = Request([], []);
        Action nullRequest = () => _parser.Parse(null!);
        Action emptyIdentity = () => _parser.Parse(request with { TemplateIdentity = " " });
        Action nullParameters = () => _parser.Parse(request with { Parameters = null! });
        Action nullTokens = () => _parser.Parse(request with { RawTemplateTokens = null! });
        Action nullReserved = () => _parser.Parse(request with { ReservedAliases = null! });
        Action nullToken = () => _parser.Parse(request with { RawTemplateTokens = [null!] });
        nullRequest.Should().Throw<ArgumentNullException>();
        emptyIdentity.Should().Throw<ArgumentException>();
        nullParameters.Should().Throw<ArgumentNullException>();
        nullTokens.Should().Throw<ArgumentNullException>();
        nullReserved.Should().Throw<ArgumentNullException>();
        nullToken.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddTemplatesOrchestrator_RegistersOneSharedParser()
    {
        ServiceCollection services = new();
        services.AddTemplatesOrchestrator();
        using var provider = services.BuildServiceProvider();

        var parser = provider.GetRequiredService<ITemplateArgumentParser>();

        parser.Should().BeOfType<TemplateArgumentParser>();
        provider.GetRequiredService<ITemplateArgumentParser>().Should().BeSameAs(parser);
    }

    [Fact]
    public void Parse_ConsecutiveSchemas_DoesNotMutatePublicCommandOrGlobalOptions()
    {
        var command = new NewCommand(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        RootCommand root = [command];
        root.Options.Add(new Option<bool>("--verbose") { Recursive = true });
        var globalOptions = root.Options.ToArray();
        var commandOptions = command.Options.ToArray();
        var arguments = command.Arguments.ToArray();

        var first = _parser.Parse(Request([Parameter("First", "force")], ["--param:First=one"], ["--force"]));
        var second = _parser.Parse(Request([Parameter("Second", "verbose")], ["--param:Second=two"], ["--verbose"]));

        first.IsComplete.Should().BeTrue();
        second.IsComplete.Should().BeTrue();
        root.Options.Should().Equal(globalOptions);
        command.Options.Should().Equal(commandOptions);
        command.Arguments.Should().Equal(arguments);
        command.TreatUnmatchedTokensAsErrors.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_ExecutableSpellingAsAValue_IsLiteral(bool inline)
    {
        string value = RootCommand.ExecutableName;
        string[] raw = inline ? ["--greeting=" + value] : ["--greeting", value];

        var result = _parser.Parse(Request([Parameter("Greeting", "greeting")], raw));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().Be(value);
    }

    [Fact]
    public void Parse_RepeatedExecutableTokens_AreBothDiagnosedAtOriginalIndices()
    {
        string[] raw = [RootCommand.ExecutableName, RootCommand.ExecutableName, "--greeting", "hello"];

        var result = _parser.Parse(Request([Parameter("Greeting", "greeting")], raw));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.SelectMany(diagnostic => diagnostic.TokenIndices).Distinct().Should().BeEquivalentTo([0, 1]);
    }

    [Theory]
    [InlineData("--param:Mode=invalid", 0)]
    [InlineData("-p:m=invalid", 0)]
    public void Parse_InlineDiagnostic_RefersToTheOriginalTokenNotSurrogate(string raw, int expectedIndex)
    {
        var result = _parser.Parse(Request([Choice(longName: "force", shortName: "m")], [raw], ["--force", "-m"]));

        result.InvalidInputDiagnostics.Should().ContainSingle().Which.TokenIndices.Should().Equal(expectedIndex);
        result.InvalidInputDiagnostics[0].Message.Should().NotContain("--template-parameter-");
    }

    [Fact]
    public void Parse_SeparatedDiagnostic_RetainsOriginalAliasAndValueIndices()
    {
        string[] raw = ["--param:Mode", "invalid"];

        var result = _parser.Parse(Request([Choice(longName: "force")], raw, ["--force"]));

        result.InvalidInputDiagnostics.Should().ContainSingle().Which.TokenIndices.Should().Equal(0, 1);
        raw.Should().Equal("--param:Mode", "invalid");
    }

    [Fact]
    public void Parse_KnownAliasCannotBeSwallowedAsAnotherOptionValue()
    {
        var result = _parser.Parse(Request(
            [Parameter("First", "first"), Parameter("Second", "second")], ["--first", "--second"]));

        result.IsArgumentCompatible.Should().BeFalse();
        result.InvalidInputDiagnostics.SelectMany(diagnostic => diagnostic.TokenIndices).Should().Contain(1);
    }

    [Fact]
    public void Parse_EmptyInputAndDefaultCompletion_DoNotNeedAUserRootToken()
    {
        var result = _parser.Parse(Request([Parameter("Greeting", "greeting", defaultValue: "default")], []));
        var emptySchema = _parser.Parse(Request([], []));

        result.IsComplete.Should().BeTrue();
        result.ValuesByCanonicalName["Greeting"].Should().Be("default");
        emptySchema.IsComplete.Should().BeTrue();
        emptySchema.ValuesByCanonicalName.Should().BeEmpty();
    }

    private static TemplateArgumentParseRequest Request(IReadOnlyList<TemplateParameterDefinition> parameters, IReadOnlyList<string> tokens,
        IEnumerable<string>? reserved = null)
        => new("Candidate", parameters, new HashSet<string>(reserved ?? [], StringComparer.Ordinal), tokens);

    private static TemplateParameterDefinition Parameter(string canonical, string longName, string? shortName = null,
        string dataType = "string", string? defaultValue = null, bool required = false)
        => new(canonical, longName, shortName, null, dataType, [], defaultValue, required, false, false);

    private static TemplateParameterDefinition Choice(string longName = "mode", string? shortName = null,
        string? defaultValue = null, bool required = false)
        => new("Mode", longName, shortName, null, "choice", [new("safe", null, null), new("strict", null, null)],
            defaultValue, required, false, false);
}
