// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeItemCompatibilityTests
{
    private static readonly byte[] _sourceBytes = Encoding.UTF8.GetBytes("owned legacy source");
    private readonly TestInteractionService _interaction = new();

    [Theory]
    [InlineData(null, "LegacyDefault", "authoredDefault")]
    [InlineData("ExplicitName", "ExplicitName", "explicit")]
    public async Task Resolve_OriginalDefaultAndExplicitInput_RetainNameOrigin(string? requested, string expected, string origin)
    {
        var compatibility = Create(_ => Contract("LegacyDefault"));

        var result = await compatibility.ResolveAsync(Entry("NativeDefault"), Arguments(), requested, false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Name.Should().Be(expected);
        result.Origin.Should().Be(origin);
    }

    [Fact]
    public async Task Resolve_MissingOriginalContract_FailsInsteadOfTreatingMissingValidatorAsApproval()
    {
        var compatibility = Create(_ => null);

        var result = await compatibility.ResolveAsync(Entry("DefaultName"), Arguments(), "ExplicitName", false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("source-qualified");
    }

    [Theory]
    [InlineData("bad-name", "^[A-Z][A-Za-z]+$")]
    [InlineData("OriginalValue", "[")]
    public async Task Resolve_OriginalRegexOrMalformedRule_RejectsWithoutSanitizingValue(string value, string pattern)
    {
        var prompt = new TemplateUserPrompt("trigger-functionName", null, "string", null, null, true, pattern, null, null);
        var compatibility = Create(_ => Contract(null) with { FunctionNamePrompt = prompt });

        var result = await compatibility.ResolveAsync(Entry("NativeDefault"), Arguments(), value, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Name.Should().Be(value);
        result.Origin.Should().Be("explicit");
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("owned-source");
    }

    [Fact]
    public async Task Resolve_DeclaredRegex_WinsOverStackDefaultAndOptionalNoRuleRemainsNoOp()
    {
        var prompt = new TemplateUserPrompt("trigger-functionName", null, "string", null, null, true, "^allowed$", null, null);
        var compatibility = Create(_ => Contract(null) with
        {
            FunctionNamePrompt = prompt,
            DefaultFunctionNameValidator = new Regex("^different$"),
        });

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), "allowed", false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_OriginalPromptRegex_ValidatesCanonicalValueWithoutChangingNoOpOrSourceTokens()
    {
        TemplateParameterDefinition definition = new("Connection", "connection", null, null, "string", [], null, false, false, false);
        string[] raw = ["--connection=Original-Invalid"];
        var parsed = new TemplateArgumentParser().Parse(new("Controlled", [definition], new HashSet<string>(), raw));
        var compatibility = Create(_ => Contract("DefaultName") with
        {
            Rules = [new("Connection", new("connection-input", null, "string", null, null, false, "^[a-z]+$", null, null))],
        });

        var result = await compatibility.ResolveAsync(Entry(null), parsed, null, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("Original-Invalid").And.Contain("owned-source");
        parsed.ValuesByCanonicalName.Should().Contain("Connection", "Original-Invalid");
        raw.Should().Equal("--connection=Original-Invalid");
    }

    [Fact]
    public async Task Resolve_NoDefaultAndNoninteractive_RefusesInsteadOfInventingDirectoryName()
    {
        var compatibility = Create(_ => Contract(null));

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), null, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Origin.Should().Be("missing");
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("--name");
    }

    [Fact]
    public async Task Resolve_InteractiveMissingName_PromptsAndChecksCancellation()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.PromptForInputAsync("Function name", null, Arg.Any<CancellationToken>()).Returns("PromptedName");
        NativeItemCompatibility compatibility = new(interaction, _ => Contract(null), ReadOwnedSourceAsync);

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), null, true, CancellationToken.None);

        result.Name.Should().Be("PromptedName");
        result.Origin.Should().Be("prompted");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_OriginalBooleanRegex_RemainsLegacyNoOp()
    {
        var compatibility = Create(_ => Contract("DefaultName") with
        {
            FunctionNamePrompt = new("trigger-functionName", null, "bool", null, null, false, "^does-not-match$", null, null),
        });

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), null, false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_MismatchedCandidate_RefusesCrossTemplateValidation()
    {
        var compatibility = Create(_ => Contract("DefaultName"));

        Func<Task> act = () => compatibility.ResolveAsync(Entry(null), Arguments() with { TemplateIdentity = "Other" },
            null, false, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullDependencies_RejectsProgrammerErrors()
    {
        Action interaction = () => new NativeItemCompatibility(null!, _ => Contract(null), ReadOwnedSourceAsync);
        Action contracts = () => new NativeItemCompatibility(_interaction, null!, ReadOwnedSourceAsync);
        Action source = () => new NativeItemCompatibility(_interaction, _ => Contract(null), null!);

        interaction.Should().Throw<ArgumentNullException>();
        contracts.Should().Throw<ArgumentNullException>();
        source.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Resolve_MismatchedInstalledPackageOrSource_RefusesStaleLegacyRules()
    {
        var compatibility = Create(_ => Contract("DefaultName") with
        {
            Package = new("different-installed-package", "2.0.0"),
        });

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), null, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("package/version");
    }

    private static NativeTemplateLegacyContract Contract(string? defaultName) =>
        new(new("Controlled", "owned-source", Convert.ToHexString(SHA256.HashData(_sourceBytes))), null, defaultName, null, null, []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_MissingOrChangedOwnedSource_RejectsEvenSyntacticallyValidClaimedHash(bool missing)
    {
        NativeItemCompatibility compatibility = new(_interaction, _ => Contract("DefaultName"),
            (_, _, _) => Task.FromResult<ReadOnlyMemory<byte>?>(missing ? null : Encoding.UTF8.GetBytes("changed source")));

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), null, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("missing or changed");
    }

    [Fact]
    public async Task Resolve_OriginalRegexTimeout_FailsWithUnsanitizedInputAndSourceDiagnostic()
    {
        var compatibility = Create(_ => Contract(null) with
        {
            FunctionNamePrompt = new("trigger-functionName", null, "string", null, null, true, "^(a+)+$", null, null),
        });
        string input = new string('a', 10000) + "!";

        var result = await compatibility.ResolveAsync(Entry(null), Arguments(), input, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Name.Should().Be(input);
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("timed out").And.Contain("owned-source").And.Contain(input);
    }

    private NativeItemCompatibility Create(Func<ResolvedTemplate, NativeTemplateLegacyContract?> contracts) =>
        new(_interaction, contracts, ReadOwnedSourceAsync);

    private static Task<ReadOnlyMemory<byte>?> ReadOwnedSourceAsync(ResolvedTemplate selected, string identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        selected.Entry.Identity.Should().Be("Controlled");
        identity.Should().Be("owned-source");
        return Task.FromResult<ReadOnlyMemory<byte>?>(_sourceBytes);
    }

    private static ResolvedTemplate Entry(string? defaultName)
    {
        TemplateCatalogEntry entry = new("Controlled", "Controlled", null, TemplateType.Item, [], null, "JavaScript", 0, null, false, [], null)
        {
            DefaultName = defaultName,
            ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Eligible, []),
        };
        ISelectedTemplateInvoker invoker = Substitute.For<ISelectedTemplateInvoker>();
        invoker.InvokeAsync(Arg.Any<TemplateInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateInvocationResult(TemplateInvocationStatus.Success, []));
        invoker.ValidateConfigurationAsync(Arg.Any<CancellationToken>()).Returns((TemplateInvocationResult?)null);
        return new(entry, invoker);
    }

    private static TemplateCandidateParseResult Arguments() => new("Controlled", new Dictionary<string, string?>(), [], [], [], []);
}
