// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateCommandAdapterTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IInitExecutionRunner _init = Substitute.For<IInitExecutionRunner>();
    private readonly INewExecutionRunner _items = Substitute.For<INewExecutionRunner>();
    private readonly TemplateConstraintEvaluationSnapshot _availability = new([]);

    [Fact]
    public async Task New_PublicPathGrammar_UsesOriginalAliasesAndInlineTokensInsteadOfLegacyOverrides()
    {
        NewExecutionRequest? captured = null;
        _items.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));
        var legacy = Substitute.For<INewCommandRunner>();
        NewCommand command = new(legacy, Substitute.For<INewCommandTemplateOptionProvider>());
        string target = Path.Combine(Path.GetTempPath(), "native-adapter-target");
        string[] tokens = [target, "-t", "item", "-n", "Named", "--output=json", "--force=false",
            "--non-interactive", "--greeting=first", "--greeting", "second", "--param:Auth=anonymous"];
        var parsed = CreateAdapter().ParseNewInvocation(command, tokens, dryRun: true);
        tokens[^1] = "caller mutation";

        int exit = await parsed.InvokeAsync();

        exit.Should().Be(1);
        captured.Should().NotBeNull();
        captured!.WorkingDirectory.Info.FullName.Should().Be(target);
        captured.TemplateReference.Should().Be("item");
        captured.Name.Should().Be("Named");
        captured.ExplicitLanguage.Should().BeNull();
        captured.Force.Should().BeFalse();
        captured.NonInteractive.Should().BeTrue();
        captured.DryRun.Should().BeTrue();
        captured.JsonOutput.Should().BeTrue();
        captured.RawTemplateTokens.Should().Equal("--greeting=first", "--greeting", "second", "--param:Auth=anonymous");
        captured.ReservedAliases.Should().Contain(["--template", "-t", "--name", "-n", "--output", "--verbose"]);
        legacy.ReceivedCalls().Should().BeEmpty();
        command.PathArgument.Should().NotBeNull();
        command.Options.Select(option => option.Name).Should().NotContain(["--path", "--language"]);
        command.Options.Select(option => option.Name).Should().Contain("--dry-run");
    }

    [Fact]
    public async Task Init_WorkloadOptions_KeepCanonicalRegistryAliasesAndExplicitScalarProvenance()
    {
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("controlled");
        initializer.SupportedLanguages.Returns(["JavaScript"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns(call =>
        {
            var registry = call.Arg<IInitOptionRegistry>();
            return
            [
                registry.GetOrAdd(CommonInitOptions.NoBundle()),
                registry.GetOrAdd(CommonInitOptions.BundlesChannel()),
            ];
        });
        InitExecutionRequest? captured = null;
        _init.ExecuteAsync(Arg.Do<InitExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(InitExecutionResult.Reject("control"));
        var command = CreateInitCommand([initializer]);
        string target = Path.Combine(Path.GetTempPath(), "native-init-adapter");
        var parsed = CreateAdapter().ParseInitInvocation(command,
            [target, "-s", "controlled", "-l", "js", "--no-bundles", "-c:Preview", "--param:Auth=anonymous"],
            new("project", NonInteractive: true, DryRun: true, JsonOutput: true));

        int exit = await parsed.InvokeAsync();

        exit.Should().Be(1);
        captured!.TemplateReference.Should().Be("project");
        captured.ExplicitStack.Should().Be("controlled");
        captured.ExplicitLanguage.Should().Be("js");
        captured.RawTemplateTokens.Should().Equal("--no-bundles", "true", "-c:Preview", "--param:Auth=anonymous");
        captured.ReservedAliases.Should().Contain(["--stack", "-s", "--language", "-l"]);
        captured.ReservedAliases.Should().NotContain(["--no-bundles", "--bundles-channel", "-c"]);
        initializer.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IProjectInitializer.InitializeAsync));
        command.Options.Select(option => option.Name).Should().Contain(["--template", "--dry-run", "--output", "--non-interactive"]);
        command.Options.Single(option => option.Name == "--bundles-channel").Aliases.Should().Contain("-c");
    }

    [Theory]
    [InlineData("--template", "project")]
    [InlineData("--template=project")]
    public async Task Init_ExplicitTemplateReference_IsHostSelectionRatherThanNativeParameter(params string[] selector)
    {
        InitExecutionRequest? captured = null;
        _init.ExecuteAsync(Arg.Do<InitExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(InitExecutionResult.Reject("control"));
        var command = CreateInitCommand([]);
        string target = Path.Combine(Path.GetTempPath(), "native-explicit-init-adapter");
        var parsed = CreateAdapter().ParseInitInvocation(command, [.. selector, target, "--non-interactive"]);

        int exit = await parsed.InvokeAsync();

        exit.Should().Be(1);
        captured!.TemplateReference.Should().Be("project");
        captured.WorkingDirectory.Info.FullName.Should().Be(target);
        captured.RawTemplateTokens.Should().BeEmpty();
        captured.ReservedAliases.Should().Contain("--template");
    }

    [Fact]
    public void Input_ImplicitBooleanAndInvalidTail_MapsDiagnosticsBackToOriginalTemplateIndices()
    {
        Command root = new RootCommand();
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("controlled");
        initializer.SupportedLanguages.Returns(["JavaScript"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns(call =>
            new[] { call.Arg<IInitOptionRegistry>().GetOrAdd(CommonInitOptions.NoBundle()) });
        var command = CreateInitCommand([initializer]);
        command.TreatUnmatchedTokensAsErrors = false;
        root.Subcommands.Add(command);
        string target = Path.Combine(Path.GetTempPath(), "native-input");
        string[] tokens = [target, "--no-bundles", "--unknown=original"];
        var parsed = root.Parse([root.Name, "init", .. tokens], new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null });
        var input = NativeTemplateCommandInput.Capture(command, parsed, tokens,
            new HashSet<Option> { command.StackOption, command.NameOption, command.LanguageOption, command.ForceOption });
        var definition = new TemplateParameterDefinition("NoBundles", "no-bundles", null, null, "bool", [], "false", false, false, false);

        var result = input.RestoreProvenance(new TemplateArgumentParser().Parse(
            new("controlled", [definition], input.ReservedAliases, input.NativeTokens)));

        input.OriginalTokens.Should().Equal(tokens);
        input.TemplateTokens.Should().Equal("--no-bundles", "--unknown=original");
        input.NativeTokens.Should().Equal("--no-bundles", "true", "--unknown=original");
        result.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.TokenIndices.SequenceEqual(new[] { 1 }));
        result.InvalidInputDiagnostics.SelectMany(diagnostic => diagnostic.TokenIndices).Should().OnlyContain(index => index < 2);
    }

    [Theory]
    [InlineData("--list")]
    [InlineData("-l")]
    public async Task New_List_UsesNativeRunnerWithoutLegacyProviderDispatch(string list)
    {
        NewExecutionRequest? captured = null;
        _items.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionResult("listed", true));
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());

        int exit = await CreateAdapter().ParseNewInvocation(command, [list, "--output", "json"]).InvokeAsync();

        exit.Should().Be(0);
        captured!.List.Should().BeTrue();
        captured.RawTemplateTokens.Should().BeEmpty();
        _interaction.Lines.Should().ContainSingle(line => line.StartsWith("JSON:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--output", "yaml")]
    public async Task New_UnsupportedOutput_StopsBeforeNativeOrLegacyRunner(params string[] tokens)
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());

        int exit = await CreateAdapter().ParseNewInvocation(command, tokens).InvokeAsync();

        exit.Should().Be(1);
        _items.ReceivedCalls().Should().BeEmpty();
        _interaction.AllOutput.Should().Contain("--output plain");
    }

    [Fact]
    public async Task New_EmptyOutput_PreservesPublicParserFailure()
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        var parsed = CreateAdapter().ParseNewInvocation(command, ["--output="]);

        int exit = await parsed.InvokeAsync();

        exit.Should().Be(1);
        parsed.Errors.Should().NotBeEmpty();
        _items.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--", "tail")]
    [InlineData("@literal-response-file")]
    [InlineData("--greeting=value")]
    public async Task New_OriginalTemplateTail_IsNotRetokenized(params string[] tail)
    {
        NewExecutionRequest? captured = null;
        _items.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        string target = Path.Combine(Path.GetTempPath(), "native-tail");

        await CreateAdapter().ParseNewInvocation(command, [target, "-t", "item", .. tail]).InvokeAsync();

        captured!.RawTemplateTokens.Should().Equal(tail);
    }

    [Fact]
    public async Task New_HostPathAfterDelimiter_RemainsPathRatherThanTemplateReference()
    {
        NewExecutionRequest? captured = null;
        _items.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        string target = Path.Combine(Path.GetTempPath(), "native-delimited");

        await CreateAdapter().ParseNewInvocation(command, ["-t", "item", "--", target]).InvokeAsync();

        captured!.WorkingDirectory.Info.FullName.Should().Be(target);
        captured.RawTemplateTokens.Should().BeEmpty();
    }

    [Fact]
    public void Adapter_AlreadyParentedCommand_RefusesMutation()
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        RootCommand root = [command];

        Action act = () => CreateAdapter().ParseNewInvocation(command, []);

        act.Should().Throw<ArgumentException>().WithMessage("*fresh, unparented*");
        root.Subcommands.Should().ContainSingle();
    }

    [Fact]
    public void Help_EffectiveNativeAliases_UseStrictParserCollisionsAndDoNotAttachToCommand()
    {
        TemplateParameterDefinition[] parameters =
        [
            new("Auth", "output", "n", "Authorization", "choice",
                [new("anonymous", null, null), new("function", null, null)], "function", false, false, false),
            new("Enabled", "enabled", "e", "Explicit Boolean", "bool", [], "false", false, false, true)
            {
                LongAliases = ["enable-feature"],
            },
            new("Secret", "secret", null, "Hidden", "string", [], null, false, true, false),
        ];
        TemplateCatalogEntry entry = new("controlled", "Controlled", null, TemplateType.Item, [], null,
            "JavaScript", 0, null, false, parameters, null);
        HashSet<string> reserved = ["--output", "-n"];
        NativeTemplateHelpProjector projector = new(new TemplateArgumentParser());

        var help = projector.Project(entry, reserved, CancellationToken.None);
        var parsed = new TemplateArgumentParser().Parse(new(entry.Identity, parameters, reserved, []));

        help.TemplateId.Should().Be(entry.Identity);
        help.HydratedOptions.Select(option => option.Name).Should().Equal("--param:Auth", "--enabled");
        help.HydratedOptions[0].Aliases.Should().NotContain("-n");
        help.HydratedOptions[1].Aliases.Should().Contain("--enable-feature");
        help.HydratedOptions.Should().OnlyContain(option => option.Arity.Equals(ArgumentArity.ExactlyOne));
        help.HydratedOptions.Select(option => option.Name).Should().BeEquivalentTo(
            parsed.Parameters.Where(parameter => !parameter.Definition.IsHidden).Select(parameter => parameter.LongAlias));
        new HelpCommand(_interaction, [], Substitute.For<ICliVersionProvider>())
            .WriteOptionsSection("Template options", help.HydratedOptions);
        _interaction.AllOutput.Should().Contain("--param:Auth").And.Contain("Choices: anonymous, function.")
            .And.Contain("--enable-feature").And.Contain("Default: function.").And.Contain("Explicit value (bool).").And.NotContain("--secret");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArgumentReports_AdditionalLongAliases_MatchTheEffectiveParserSurface(bool project)
    {
        TemplateParameterDefinition definition = new("Mode", "Mode", null, null, "string", [], null, false, false, false)
        {
            LongAliases = ["force"],
        };
        TemplateCandidateParseResult parsed = new TemplateArgumentParser().Parse(
            new("controlled", [definition], new HashSet<string>(["--force"]), ["--param:Mode", "value"]));
        TestInteractionService interaction = new();

        int exit = project
            ? new InitExecutionRenderer(interaction).Render(new("control", true) { ArgumentResults = [parsed] }, jsonOutput: true)
            : new NewExecutionRenderer(interaction).Render(new("control", true) { ArgumentResults = [parsed] }, jsonOutput: true);

        exit.Should().Be(0);
        using var json = JsonDocument.Parse(interaction.Lines.Single()["JSON: ".Length..]);
        JsonElement parameter = json.RootElement.GetProperty("argumentResults")[0].GetProperty("parameters")[0];
        parameter.GetProperty("canonicalName").GetString().Should().Be("Mode");
        parameter.GetProperty("longAlias").GetString().Should().Be("--Mode");
        parameter.GetProperty("longAliases").EnumerateArray().Select(alias => alias.GetString()).Should().Equal("--param:Mode");
    }

    private NativeTemplateCommandAdapter CreateAdapter() => new(
        _init, _items, new(_interaction), new(_interaction), _availability);

    private InitCommand CreateInitCommand(IReadOnlyList<IProjectInitializer> initializers) => new(
        _interaction, new RecordingWorkloadHintRenderer(), Substitute.For<ILocalSettingsProvider>(),
        Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
        new InstalledBundleScanner(Substitute.For<IInstalledBundleWorkloads>()), Substitute.For<IFileSystem>(), initializers);
}
