// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Clipboard;
using Azure.Functions.Cli.Console.Terminal;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class FullScreenDashboardTests
{
    [Fact]
    public async Task StalledOutput_DoesNotPreventInputRestorationDuringShutdown()
    {
        ITerminalSession terminal = Substitute.For<ITerminalSession>();
        terminal.Size.Returns(new TerminalSize(80, 24));
        terminal.ReadAsync(Arg.Any<CancellationToken>()).Returns(call => IdleInput(call.Arg<CancellationToken>()));
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.When(value => value.Render(Arg.Any<IRenderable>())).Do(_ =>
        {
            entered.TrySetResult();
            release.Wait();
            returned.TrySetResult();
        });
        IPlatform platform = Substitute.For<IPlatform>();
        await using CompactRenderer renderer = new(Interaction(), new FunctionPalette(),
            new CompactDashboardShortcutLabels(platform), terminal: terminal);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await FluentActions.Awaiting(() => renderer.OnSummaryAsync(
                    new DashboardState().BuildSummary("sigint", DateTimeOffset.UtcNow), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)))
                .Should().ThrowAsync<IOException>().WithMessage("*did not stop*");
            terminal.Received().Restore();
            await terminal.Received().DisposeAsync();
        }
        finally
        {
            release.Set();
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SelectionCtrlC_CopiesWithoutCancellation_ThenStopsAndEscalatesWithoutSelection()
    {
        using CancellationTokenSource run = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IInteractionService interaction = Interaction();
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        string? copied = null;
        clipboard.CopyAsync(Arg.Any<string>(), terminal, Arg.Any<CancellationToken>()).Returns(call =>
        {
            copied = call.Arg<string>();
            return Task.FromResult(new ClipboardCopyResult(ClipboardCopyStatus.Confirmed, "Copied"));
        });
        int forced = 0;
        ConsoleInterruptCoordinator interrupts = new(run.Cancel, _ => Interlocked.Increment(ref forced));
        await using CompactRenderer renderer = Renderer(terminal, interaction, clipboard, interrupts);
        await renderer.OnStartAsync(new DashboardState(), run.Token);
        await renderer.OnEventAsync(Log("copy me"), [], timeout.Token);
        await terminal.WaitForTextAsync("copy me", timeout.Token);

        terminal.Select("copy me");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);
        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync("Copied", timeout.Token);

        copied.Should().Be("copy me");
        run.IsCancellationRequested.Should().BeFalse();
        forced.Should().Be(0);

        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync("Stopping", timeout.Token);
        run.IsCancellationRequested.Should().BeTrue();
        terminal.Send(Interrupt());
        await terminal.ForcedInputProcessedAsync(() => forced != 0, timeout.Token);
        forced.Should().Be(1);
        await renderer.OnSummaryAsync(new DashboardState().BuildSummary("sigint", DateTimeOffset.UtcNow), timeout.Token);
        terminal.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task FailedCopy_KeepsSelectionAndSurfacesFailure()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        clipboard.CopyAsync(Arg.Any<string>(), terminal, Arg.Any<CancellationToken>())
            .Returns(new ClipboardCopyResult(ClipboardCopyStatus.Failed, "Clipboard denied"));
        int stopped = 0;
        ConsoleInterruptCoordinator interrupts = new(() => stopped++, _ => stopped++);
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard, interrupts);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("retain me"), [], timeout.Token);
        await terminal.WaitForTextAsync("retain me", timeout.Token);
        terminal.Select("retain me");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);
        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync("Clipboard denied", timeout.Token);
        terminal.Text.Should().Contain("Ctrl+C copy");
        stopped.Should().Be(0);
    }

    [Fact]
    public async Task PasteOutsideSearch_DoesNotExecuteShortcuts()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        await using CompactRenderer renderer = Renderer(terminal, Interaction());
        int shutdown = 0;
        renderer.ShutdownRequested += () => shutdown++;
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("preserved log"), [], timeout.Token);
        await terminal.WaitForTextAsync("preserved log", timeout.Token);
        terminal.Send(new TerminalInputEvent(TerminalInputKind.Paste, Text: "qce"));
        terminal.Send(new TerminalInputEvent(TerminalInputKind.Key, new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false)));
        await terminal.WaitForTextAsync("Help", timeout.Token);
        shutdown.Should().Be(0);
    }

    [Theory]
    [InlineData(120, 24)]
    [InlineData(40, 8)]
    [InlineData(3, 2)]
    [InlineData(1, 1)]
    public void Frames_FillExactlyTheViewportWithoutCorruptingText(int width, int height)
    {
        StringWriter writer = new();
        IAnsiConsole console = CreateConsole(writer, width, height);
        DashboardFrame frame = new(width, height, new Text("HEADER"), 2, [new Text("log")], new Text("q quit"), Style.Plain);
        console.Write(frame);
        string output = writer.ToString().Replace("\r", string.Empty, StringComparison.Ordinal);
        output.Split('\n').Should().HaveCount(height);
        output.Should().NotContain("\u001b[3J");
        foreach (string row in output.Split('\n'))
        {
            row.Length.Should().BeLessThanOrEqualTo(width);
        }
    }

    [Fact]
    public void SelectedEmoji_IsNotSplitByTheSpectreFramePipeline()
    {
        StringWriter writer = new();
        IAnsiConsole console = CreateConsole(writer, 3, 6);
        CompactLogBuffer buffer = new();
        buffer.Add(new CompactLogLine(new Text("\ud83d\ude00\ud83d\ude00"), null, false, LogLevel.Information));
        LogViewport viewport = new();
        viewport.Refresh(buffer.Capture(), 3, 3, new LogFilter());
        viewport.BeginDrag(0, 0);
        viewport.Drag(1, 1);
        DashboardFrame frame = new(3, 6, new Text("H"), 1, viewport.RenderRows(new DefaultTheme().Selection), new Text("q"), Style.Plain);
        console.Write(frame);
        string output = writer.ToString();

        output.Should().NotContain("\ufffd");
        output.Count(char.IsHighSurrogate).Should().Be(2);
        viewport.CaptureSelection().Text.Should().Be("\ud83d\ude00\ud83d\ude00");
    }

    private static IInteractionService Interaction()
    {
        IInteractionService interaction = Substitute.For<IInteractionService>();
        interaction.Theme.Returns(new DefaultTheme());
        return interaction;
    }

    private static CompactRenderer Renderer(ScriptedTerminal terminal, IInteractionService interaction,
        IClipboardWriter? clipboard = null, ConsoleInterruptCoordinator? interrupts = null)
    {
        IPlatform platform = Substitute.For<IPlatform>();
        return new CompactRenderer(interaction, new FunctionPalette(), new CompactDashboardShortcutLabels(platform),
            clipboard: clipboard, interrupts: interrupts, terminal: terminal);
    }

    private static HostLogEntry Log(string message)
        => new(DateTimeOffset.UnixEpoch, "Host.Test", LogLevel.Information, default, message, null, HostLogEntry.EmptyAttributes);

    private static TerminalInputEvent Interrupt()
        => new(TerminalInputKind.Key, new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));

    private static async IAsyncEnumerable<TerminalInputEvent> IdleInput([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }

    private static IAnsiConsole CreateConsole(StringWriter writer, int width, int height)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        return console;
    }

    private sealed class ScriptedTerminal : ITerminalSession
    {
        private readonly Channel<TerminalInputEvent> _input = Channel.CreateUnbounded<TerminalInputEvent>();
        private readonly Channel<bool> _frames = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly Lock _lock = new();
        private string _text = string.Empty;
        private int _logTop;

        public TerminalSize Size => new(120, 24);
        public bool Disposed { get; private set; }
        public string Text { get { lock (_lock) { return _text; } } }

        public void Send(TerminalInputEvent input) => _input.Writer.TryWrite(input);

        public void Select(string text)
        {
            lock (_lock)
            {
                string row = _text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')[_logTop];
                int column = row.IndexOf(text, StringComparison.Ordinal);
                column.Should().BeGreaterThanOrEqualTo(0);
                Send(new TerminalInputEvent(TerminalInputKind.PointerDown, X: column, Y: _logTop));
                Send(new TerminalInputEvent(TerminalInputKind.PointerMove, X: column + text.Length - 1, Y: _logTop));
                Send(new TerminalInputEvent(TerminalInputKind.PointerUp, X: column + text.Length - 1, Y: _logTop));
            }
        }

        public async Task WaitForTextAsync(string text, CancellationToken cancellationToken)
        {
            while (!Text.Contains(text, StringComparison.Ordinal))
            {
                await _frames.Reader.ReadAsync(cancellationToken);
            }
        }

        public async Task ForcedInputProcessedAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                await Task.Delay(10, cancellationToken);
            }
        }

        public async IAsyncEnumerable<TerminalInputEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (TerminalInputEvent input in _input.Reader.ReadAllAsync(cancellationToken))
            {
                yield return input;
            }
        }

        public void Render(IRenderable frame)
        {
            using StringWriter writer = new();
            CreateConsole(writer, Size.Width, Size.Height).Write(frame);
            lock (_lock)
            {
                _text = writer.ToString();
                _logTop = ((DashboardFrame)frame).LogTop;
            }

            _frames.Writer.TryWrite(true);
        }

        public void WriteControl(string sequence) { }
        public void SetMouseCapture(bool enabled) { }
        public void Restore() { }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
