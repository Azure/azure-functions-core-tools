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
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.FirstRun;

public sealed class FirstRunCoordinatorTests
{
    private readonly IFirstRunStateStore _stateStore = Substitute.For<IFirstRunStateStore>();
    private readonly ISetupRunner _setupRunner = Substitute.For<ISetupRunner>();
    private readonly PromptingInteractionService _interaction = new();

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenAlreadyComplete()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        Assert.Equal(0, _interaction.ConfirmCalls);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenNonInteractive()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        _interaction.InteractiveOverride = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        Assert.Equal(0, _interaction.ConfirmCalls);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("version")]
    public async Task SkipsAndDoesNotMark_ForExcludedCommands(string commandName)
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync(commandName, Parse(commandName), CancellationToken.None);

        Assert.Equal(0, _interaction.ConfirmCalls);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task SkipsAndDoesNotMark_WhenHelpOrVersionTokenPresent(string token)
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse($"start {token}"), CancellationToken.None);

        Assert.Equal(0, _interaction.ConfirmCalls);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task PromptsOnBareFunc_WhenNoSubcommandGiven()
    {
        // Bare `func` produces a "Required command was not provided" parse
        // error and the resolver labels it "unknown", but it's the canonical
        // first-run trigger and must still prompt.
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        _interaction.ConfirmResponse = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("unknown", Parse(string.Empty), CancellationToken.None);

        Assert.Equal(1, _interaction.ConfirmCalls);
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsAndDoesNotMark_WhenParseHasErrorsAndTokensPresent()
    {
        // A typo like `func startt` produces tokens and parse errors; we stay
        // quiet until the user fixes the command line.
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("unknown", Parse("startt"), CancellationToken.None);

        Assert.Equal(0, _interaction.ConfirmCalls);
        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task RunsSetupAndMarks_WhenUserConfirms()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(0));
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        Assert.Equal(1, _interaction.ConfirmCalls);
        await _setupRunner.Received(1).RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>());
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsSetupButMarks_WhenUserDeclines()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        _interaction.ConfirmResponse = false;
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        Assert.Equal(1, _interaction.ConfirmCalls);
        await _setupRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PropagatesCancellation_FromPrompt()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        FirstRunCoordinator coordinator = CreateCoordinator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), cts.Token));

        await _stateStore.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
    }

    [Fact]
    public async Task SetupFailureIsSwallowed_ButMarkerStillWritten()
    {
        _stateStore.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        _interaction.ConfirmResponse = true;
        _setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<SetupRunResult>>(_ => throw new InvalidOperationException("boom"));
        FirstRunCoordinator coordinator = CreateCoordinator();

        await coordinator.EnsureFirstRunPromptedAsync("start", Parse("start"), CancellationToken.None);

        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:", StringComparison.Ordinal) && l.Contains("boom"));
        await _stateStore.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
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
