// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.FirstRun;
using NSubstitute;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.FirstRun;

public sealed class FirstRunCoordinatorTests
{
    private readonly IFirstRunStateStore _stateStore = Substitute.For<IFirstRunStateStore>();
    private readonly ISetupRunner _setupRunner = Substitute.For<ISetupRunner>();
    private readonly PromptingInteractionService _interaction = new();

    public FirstRunCoordinatorTests()
    {
        _stateStore.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.NeverPrompted);
    }

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenWorkloadsInstalled()
    {
        _stateStore.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.WorkloadsInstalled);
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(0);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenNonInteractive()
    {
        _interaction.InteractiveOverride = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(0);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("version")]
    [InlineData("workload")]
    [InlineData("workload install")]
    [InlineData("workload list")]
    [InlineData("workload search")]
    [InlineData("workload update")]
    [InlineData("workload uninstall")]
    public async Task SkipsAndDoesNotMark_ForExcludedCommands(string commandName)
    {
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync(commandName, Parse(commandName), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(0);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task SkipsAndDoesNotMark_WhenHelpOrVersionTokenPresent(string token)
    {
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse($"start {token}"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(0);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task PromptsOnBareFunc_WhenNoSubcommandGiven()
    {
        // Bare `func` produces a "Required command was not provided" parse
        // error and the resolver labels it "unknown", but it's the canonical
        // first-run trigger and must still prompt.
        _interaction.ConfirmResponse = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("unknown", Parse(string.Empty), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(1);
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenParseHasErrorsAndTokensPresent()
    {
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("unknown", Parse("startt"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(0);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task RunsSetupAndMarks_WhenUserConfirms()
    {
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(0));
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        // After a successful setup we always short-circuit the user's command,
        // regardless of which one they typed, because the workload loader
        // snapshot is stale until the next process.
        result.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("Re-run `func start`"));
        _interaction.ConfirmCalls.Should().Be(1);
        await _setupRunner.Received(1).RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>());
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsSetupButMarks_WhenUserDeclines()
    {
        _interaction.ConfirmResponse = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.ConfirmCalls.Should().Be(1);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PropagatesCancellation_FromPrompt_AndWritesMarker()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        FirstRunCoordinator coordinator = CreateCoordinator();

        await FluentActions.Awaiting(() => coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), cts.Token)).Should().ThrowAsync<OperationCanceledException>();

        // Ctrl+C at the prompt should still write the marker so the user
        // is not re-prompted next time. The token passed to MarkCompleteAsync
        // must NOT be the cancelled one, otherwise the real file-backed
        // store's File.WriteAllTextAsync would throw and the marker would
        // never land on disk.
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task SetupFailureIsSwallowed_ButMarkerStillWritten()
    {
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<SetupRunResult>>(_ => throw new InvalidOperationException("boom"));
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:", StringComparison.Ordinal) && l.Contains("boom"));
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("init")]
    [InlineData("new")]
    [InlineData("start")]
    [InlineData("run")]
    [InlineData("quickstart")]
    public async Task ShortCircuitsWithReRunHint_AfterSuccessfulSetup(string commandName)
    {
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(0));
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync(commandName, Parse(commandName), CancellationToken.None);

        result.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains($"Re-run `func {commandName}`"));
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("help")]
    [InlineData("unknown")]
    public async Task ShortCircuitsWithGenericHint_AfterSuccessfulSetup_ForSentinelCommandNames(string commandName)
    {
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(0));
        FirstRunCoordinator coordinator = CreateCoordinator();

        // Sentinel names come from bare/unparseable invocations, so pass an
        // empty parse result rather than parsing the sentinel as a token.
        int? result = await coordinator.EnsureFirstRunPromptedAsync(commandName, Parse(string.Empty), CancellationToken.None);

        result.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("Run `func <command>`"));
        _interaction.Lines.Should().NotContain(l => l.Contains($"Re-run `func {commandName}`"));
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotShortCircuit_WhenInitDeclinesSetup()
    {
        _interaction.ConfirmResponse = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("init", Parse("init"), CancellationToken.None);

        result.Should().BeNull();
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotShortCircuit_WhenInitSetupFails()
    {
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(1));
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("init", Parse("init"), CancellationToken.None);

        result.Should().BeNull();
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowsBreadcrumb_WhenMarkerPresentButNoWorkloads()
    {
        _stateStore.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.MarkerWithoutWorkloads);
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("`func setup`"));
        _interaction.ConfirmCalls.Should().Be(0);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task DoesNotShowBreadcrumb_ForSkippedCommands()
    {
        _stateStore.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.MarkerWithoutWorkloads);
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("setup", Parse("setup"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.Lines.Should().NotContain(l => l.Contains("`func setup`"));
    }

    [Fact]
    public async Task DoesNotShowBreadcrumb_WhenNonInteractive()
    {
        _stateStore.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.MarkerWithoutWorkloads);
        _interaction.InteractiveOverride = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        int? result = await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        result.Should().BeNull();
        _interaction.Lines.Should().NotContain(l => l.Contains("`func setup`"));
    }

    private FirstRunCoordinator CreateCoordinator() => new(_interaction, _stateStore, _setupRunner);

    private static ParseResult Parse(string args)
    {
        var root = new FuncRootCommand();

        // Minimal subcommands so Parse can resolve common test cases without errors.
        root.Subcommands.Add(new Command("setup"));
        root.Subcommands.Add(new Command("help"));
        root.Subcommands.Add(new Command("version"));
        root.Subcommands.Add(new Command("start"));
        root.Subcommands.Add(new Command("init"));
        root.Subcommands.Add(new Command("new"));
        root.Subcommands.Add(new Command("run"));
        root.Subcommands.Add(new Command("quickstart"));

        return root.Parse(args);
    }

    private sealed class PromptingInteractionService : IInteractionService
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public bool InteractiveOverride { get; set; } = true;

        public bool ConfirmResponse { get; set; } = true;

        public int ConfirmCalls { get; private set; }

        public ITheme Theme { get; } = new DefaultTheme();

        public bool IsInteractive => InteractiveOverride;

        public void WriteLine(string text) => _lines.Add(text);

        public void WriteBlankLine() => _lines.Add(string.Empty);

        public void WriteLine(Action<InlineLine> build) => _lines.Add("LINE");

        public void Write(IRenderable renderable) => _lines.Add($"RENDERABLE: {renderable.GetType().Name}");

        public void WriteTitle(string text) => _lines.Add($"TITLE: {text}");

        public void WriteSectionHeader(string title) => _lines.Add($"RULE: {title}");

        public void WriteHint(string message) => _lines.Add($"HINT: {message}");

        public void WriteSuccess(string message) => _lines.Add($"SUCCESS: {message}");

        public void WriteError(string message) => _lines.Add($"ERROR: {message}");

        public void WriteWarning(string message) => _lines.Add($"WARNING: {message}");

        public void WriteDefinitionList(IEnumerable<DefinitionItem> items)
        {
        }

        public void WriteTable(string[] columns, IEnumerable<string[]> rows)
        {
        }

        public void WriteJson(object value)
        {
        }

        public Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);

        public Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);

        public Task<T> RunWithProgressAsync<T>(string initialDescription, Func<IProgressContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmCalls++;
            return Task.FromResult(ConfirmResponse);
        }

        public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue ?? string.Empty);
    }
}
