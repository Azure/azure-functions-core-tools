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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MouseControlOutput_LeavesStopAndEscalationResponsive(bool quit)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource run = new();
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTerminal terminal = new()
        {
            MouseCaptureChanging = enabled =>
            {
                if (!enabled)
                {
                    entered.TrySetResult();
                    release.Wait(timeout.Token);
                }
            },
        };
        int forced = 0;
        ConsoleInterruptCoordinator interrupts = new(run.Cancel, _ => Interlocked.Increment(ref forced));
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), interrupts: interrupts);
        renderer.ShutdownRequested += run.Cancel;
        await renderer.OnStartAsync(new DashboardState(), run.Token);
        await terminal.WaitForTextAsync("Logs", timeout.Token);
        try
        {
            terminal.Send(Key('m', ConsoleKey.M));
            await entered.Task.WaitAsync(timeout.Token);
            terminal.Send(new TerminalInputEvent(TerminalInputKind.PointerDown, X: 0, Y: terminal.LogTop));

            await terminal.SendAndWaitAsync(quit ? Key('q', ConsoleKey.Q) : Interrupt(), timeout.Token);
            run.IsCancellationRequested.Should().BeTrue();
            await terminal.SendAndWaitAsync(Interrupt(), timeout.Token);

            forced.Should().Be(1);
            terminal.Restored.Should().BeTrue();
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData('\u00e7', ConsoleKey.C)]
    [InlineData('@', ConsoleKey.Q)]
    public async Task NativeAltGrText_ReachesSearchWithoutCopyingOrStopping(char character, ConsoleKey key)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard);
        int shutdown = 0;
        renderer.ShutdownRequested += () => shutdown++;
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        terminal.Send(Key('/', ConsoleKey.Oem2));
        await terminal.WaitForTextAsync("Search functions", timeout.Token);
        WindowsInputRecord record = new()
        {
            EventType = 1,
            Key = new WindowsKeyRecord
            {
                KeyDown = 1,
                RepeatCount = 1,
                VirtualKeyCode = (ushort)key,
                UnicodeCharacter = character,
                ControlKeyState = 8 | 1,
            },
        };

        terminal.Send(new WindowsInputNormalizer().Decode(record, default).Single());
        await terminal.WaitForTextAsync(character.ToString(), timeout.Token);

        shutdown.Should().Be(0);
        _ = clipboard.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default);
    }

    [Fact]
    public async Task EvictedMouseDownAnchor_ControlCReportsExpiryInsteadOfStopping()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        CompactLogBuffer buffer = new(1);
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard, logBuffer: buffer);
        int shutdown = 0;
        renderer.ShutdownRequested += () => shutdown++;
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("old"), [], timeout.Token);
        await terminal.WaitForTextAsync("old", timeout.Token);
        await terminal.SendAndWaitAsync(new TerminalInputEvent(TerminalInputKind.PointerDown, X: 0, Y: terminal.LogTop), timeout.Token);
        await terminal.WaitForTextAsync("Logs · Paused", timeout.Token);

        await renderer.OnEventAsync(Log("new"), [], timeout.Token);
        await terminal.WaitForTextAsync("Selection expired", timeout.Token);
        await terminal.SendAndWaitAsync(new TerminalInputEvent(TerminalInputKind.PointerMove, X: 5, Y: terminal.LogTop), timeout.Token);
        await terminal.SendAndWaitAsync(new TerminalInputEvent(TerminalInputKind.PointerUp, X: 5, Y: terminal.LogTop), timeout.Token);
        await terminal.SendAndWaitAsync(Interrupt(), timeout.Token);

        shutdown.Should().Be(0);
        _ = clipboard.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default);
        terminal.Text.Should().Contain("Selection expired");
    }

    [Fact]
    public async Task TerminalSelection_ReleasesEvictedHistoryWithoutRepainting()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        CompactLogBuffer buffer = new(2, 1024);
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), logBuffer: buffer);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("old payload " + new string('x', 100)), [], timeout.Token);
        await terminal.WaitForTextAsync("old payload", timeout.Token);
        WeakReference<string> oldText = FirstText(buffer);
        terminal.Send(Key('m', ConsoleKey.M));
        await terminal.WaitForTextAsync("Terminal selection", timeout.Token);
        int frames = terminal.FrameCount;

        await renderer.OnEventAsync(Log("new one"), [], timeout.Token);
        await renderer.OnEventAsync(Log("new two"), [], timeout.Token);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!IsAlive(oldText))
            {
                break;
            }
            await Task.Delay(10, timeout.Token);
        }

        IsAlive(oldText).Should().BeFalse("the terminal owns the frozen pixels, not a second retained history");
        terminal.FrameCount.Should().Be(frames);
        buffer.Capture().Entries.Should().HaveCount(2);
        GC.KeepAlive(renderer);
        GC.KeepAlive(buffer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCopyCompletion_DoesNotRepaintTerminalSelection(bool confirmed)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        TaskCompletionSource<ClipboardCopyResult> completion = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.CopyAsync(Arg.Any<string>(), terminal, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            entered.TrySetResult();
            return completion.Task;
        });
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("copy target"), [], timeout.Token);
        await terminal.WaitForTextAsync("copy target", timeout.Token);
        terminal.Select("copy target");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);
        terminal.Send(Interrupt());
        await entered.Task.WaitAsync(timeout.Token);
        terminal.Send(Key('m', ConsoleKey.M));
        await terminal.WaitForTextAsync("Terminal selection", timeout.Token);
        int frames = terminal.FrameCount;

        completion.SetResult(new ClipboardCopyResult(confirmed ? ClipboardCopyStatus.Confirmed : ClipboardCopyStatus.Failed, "late result"));
        await renderer.OnEventAsync(Log("arrived after capture release"), [], timeout.Token);
        await Task.Delay(200, timeout.Token);

        terminal.FrameCount.Should().Be(frames);
        terminal.Text.Should().NotContain("late result");
        terminal.MouseCapture.Should().BeFalse();
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(40, 24)]
    [InlineData(40, 10)]
    public async Task Help_RealCopyFeedbackAndLastShortcutAreReachableWithoutResizing(int width, int height)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new() { Size = new TerminalSize(width, height) };
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        clipboard.CopyAsync(Arg.Any<string>(), terminal, Arg.Any<CancellationToken>())
            .Returns(new ClipboardCopyResult(ClipboardCopyStatus.Sent,
                "Copy request sent to the terminal, not confirmed. Terminal or tmux permissions may prevent copying; the selection is retained."));
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("select"), [], timeout.Token);
        await terminal.WaitForTextAsync("select", timeout.Token);
        terminal.Select("select");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);
        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync("Copy unconfirmed", timeout.Token);
        terminal.Send(Key('?', ConsoleKey.Oem2));
        await terminal.WaitForTextAsync("Copy request sent", timeout.Token);
        terminal.Text.Should().Contain("↑/↓ scroll");

        terminal.Send(Key('\0', ConsoleKey.End));
        await terminal.WaitForTextAsync("Advanced", timeout.Token);
        terminal.Text.Should().NotContain("Copy request sent");
        terminal.Send(Key('\0', ConsoleKey.Home));
        await terminal.WaitForTextAsync("Copy request sent", timeout.Token);

        terminal.Text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Should().HaveCount(height);
    }

    [Fact]
    public async Task RedrawNotifications_CoalesceWhileOutputIsBusyAndRenderTheLatestUpdate()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int block = 1;
        ScriptedTerminal terminal = new()
        {
            Rendering = () =>
            {
                if (Interlocked.Exchange(ref block, 0) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(timeout.Token);
                }
            },
        };
        await using CompactRenderer renderer = Renderer(terminal, Interaction());
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await Parallel.ForAsync(0, 1000, timeout.Token,
                async (index, token) => await renderer.OnEventAsync(Log($"entry {index}"), [], token));
            await renderer.OnEventAsync(Log("final notification"), [], timeout.Token);
        }
        finally
        {
            release.Set();
        }

        await terminal.WaitForTextAsync("final notification", timeout.Token);
        renderer.Completion.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task BusyOutput_DoesNotReinterpretAQueuedSearchCharacterAsQuit()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int block = 1;
        ScriptedTerminal terminal = new()
        {
            Rendering = () =>
            {
                if (Interlocked.Exchange(ref block, 0) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(timeout.Token);
                }
            },
        };
        await using CompactRenderer renderer = Renderer(terminal, Interaction());
        int shutdown = 0;
        renderer.ShutdownRequested += () => shutdown++;
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await terminal.SendAndWaitAsync(Key('/', ConsoleKey.Oem2), timeout.Token);
            await terminal.SendAndWaitAsync(Key('q', ConsoleKey.Q), timeout.Token);
            shutdown.Should().Be(0);
        }
        finally
        {
            release.Set();
        }

        await terminal.WaitForTextAsync("Search: q", timeout.Token);
        shutdown.Should().Be(0);
    }

    [Fact]
    public async Task StalledOutput_DoesNotPreventInputRestorationDuringShutdown()
    {
        TaskCompletionSource<(TimeSpan Delay, Action Expire)> shutdownDeadline = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeProvider time = Substitute.For<TimeProvider>();
        time.CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>())
            .Returns(call =>
            {
                if (call.ArgAt<TimeSpan>(3) == Timeout.InfiniteTimeSpan)
                {
                    TimerCallback callback = call.ArgAt<TimerCallback>(0);
                    object? state = call.ArgAt<object?>(1);
                    shutdownDeadline.TrySetResult((call.ArgAt<TimeSpan>(2), () => callback(state)));
                }

                return Substitute.For<ITimer>();
            });
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
            new CompactDashboardShortcutLabels(platform), terminal: terminal, timeProvider: time);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task summary = renderer.OnSummaryAsync(
                new DashboardState().BuildSummary("sigint", DateTimeOffset.UtcNow), CancellationToken.None);
            (TimeSpan delay, Action expire) = await shutdownDeadline.Task.WaitAsync(TimeSpan.FromSeconds(5));
            delay.Should().Be(TimeSpan.FromSeconds(2));
            expire();

            await FluentActions.Awaiting(() => summary.WaitAsync(TimeSpan.FromSeconds(5)))
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedCopy_KeepsSelectionAndSurfacesDetails(bool sent)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        string message = sent ? "Copy request sent; not confirmed" : "Clipboard denied";
        clipboard.CopyAsync(Arg.Any<string>(), terminal, Arg.Any<CancellationToken>())
            .Returns(new ClipboardCopyResult(sent ? ClipboardCopyStatus.Sent : ClipboardCopyStatus.Failed, message));
        int stopped = 0;
        ConsoleInterruptCoordinator interrupts = new(() => stopped++, _ => stopped++);
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard, interrupts);
        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        await renderer.OnEventAsync(Log("retain me"), [], timeout.Token);
        await terminal.WaitForTextAsync("retain me", timeout.Token);
        terminal.Select("retain me");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);
        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync(sent ? "Copy unconfirmed" : "Copy failed", timeout.Token);
        terminal.Text.Should().Contain("Ctrl+C copy");
        stopped.Should().Be(0);

        terminal.Send(new TerminalInputEvent(TerminalInputKind.Key, new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false)));
        await terminal.WaitForTextAsync(message, timeout.Token);
        string details = string.Join(" ", terminal.Text.Split('\n').Select(line => line.Trim(' ', '\r', '│')));
        details.Should().Contain("advanced terminal selection").And.Contain("terminal's Copy command")
            .And.Contain("Ctrl+C sent to the CLI stops the host");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    public async Task TerminalSelection_ReturnPreservesReadingPosition_AndControlCStopsRatherThanCopies(int width)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource run = new();
        ScriptedTerminal terminal = new() { Size = new TerminalSize(width, 24) };
        IClipboardWriter clipboard = Substitute.For<IClipboardWriter>();
        ConsoleInterruptCoordinator interrupts = new(run.Cancel, _ => { });
        await using CompactRenderer renderer = Renderer(terminal, Interaction(), clipboard, interrupts);
        await renderer.OnStartAsync(new DashboardState(), run.Token);
        await renderer.OnEventAsync(Log("anchor"), [], timeout.Token);
        await terminal.WaitForTextAsync("anchor", timeout.Token);
        int logTop = terminal.LogTop;
        terminal.Select("anchor");
        await terminal.WaitForTextAsync("Ctrl+C copy", timeout.Token);

        terminal.Send(Key('m', ConsoleKey.M));
        await terminal.WaitForTextAsync("Terminal selection", timeout.Token);

        terminal.MouseCapture.Should().BeFalse();
        terminal.LogTop.Should().Be(logTop);
        terminal.Text.Should().Contain("host running").And.Contain("m return to TUI").And.Contain("Ctrl+C stop")
            .And.NotContain("Ctrl+C copy");
        run.IsCancellationRequested.Should().BeFalse();
        await renderer.OnEventAsync(Log("collected while paused"), [], timeout.Token);

        terminal.Send(Key('m', ConsoleKey.M));
        await terminal.WaitForTextAsync("End live", timeout.Token);

        terminal.MouseCapture.Should().BeTrue();
        terminal.LogTop.Should().Be(logTop);
        terminal.Text.Should().Contain("Paused").And.NotContain("Terminal selection");
        terminal.Send(Key('\0', ConsoleKey.End));
        await terminal.WaitForTextAsync("Logs · Live", timeout.Token);
        terminal.Text.Should().Contain("collected while paused");

        terminal.Send(Key('m', ConsoleKey.M));
        await terminal.WaitForTextAsync("Terminal selection", timeout.Token);
        terminal.Send(Interrupt());
        await terminal.WaitForTextAsync("Stopping", timeout.Token);

        run.IsCancellationRequested.Should().BeTrue();
        _ = clipboard.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task NormalSummary_RestoresTerminalAndPrintsOnlyDurationAndCounts(int? hostExitCode)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IInteractionService interaction = Interaction();
        interaction.When(value => value.WriteLine(Arg.Any<string>())).Do(_ => terminal.Disposed.Should().BeTrue());
        await using CompactRenderer renderer = Renderer(terminal, interaction);
        await renderer.OnStartAsync(new DashboardState(), timeout.Token);
        await renderer.OnEventAsync(Log("earlier invocation failure", LogLevel.Error), [], timeout.Token);
        renderer.BeginShutdown(hostExitCode);
        SummaryEvent summary = new(DateTimeOffset.UnixEpoch, hostExitCode is null ? "sigint" : "source_completed",
            UptimeSeconds: 564, FunctionCount: 12, TotalInvocations: 64,
            SucceededInvocations: 60, FailedInvocations: 4, ErrorCount: 4);

        await renderer.OnSummaryAsync(summary, timeout.Token);

        interaction.Received(1).WriteBlankLine();
        interaction.Received(1).WriteLine("  Azure Functions host stopped after 00:09:24");
        interaction.Received(1).WriteLine("  12 functions  ·  64 invocations  ·  4 errors");
        interaction.Received(2).WriteLine(Arg.Any<string>());
        interaction.DidNotReceiveWithAnyArgs().WriteRawLine(default!);
        interaction.DidNotReceiveWithAnyArgs().WriteError(default!);
    }

    [Fact]
    public async Task FailedHostSummary_PreservesExitCodeAndLoggedFailureContextAfterRestoration()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ScriptedTerminal terminal = new();
        IInteractionService interaction = Interaction();
        interaction.When(value => value.WriteError(Arg.Any<string>())).Do(_ => terminal.Disposed.Should().BeTrue());
        interaction.When(value => value.WriteRawLine(Arg.Any<string>())).Do(_ => terminal.Disposed.Should().BeTrue());
        await using CompactRenderer renderer = Renderer(terminal, interaction);
        await renderer.OnStartAsync(new DashboardState(), timeout.Token);
        await renderer.OnEventAsync(Log("host failed to continue", LogLevel.Error), [], timeout.Token);
        renderer.BeginShutdown(42);

        await renderer.OnSummaryAsync(new DashboardState().BuildSummary("source_completed", DateTimeOffset.UtcNow), timeout.Token);

        interaction.Received(1).WriteError("The host exited with code 42.");
        interaction.Received(1).WriteLine("  Last logged error:");
        interaction.Received(1).WriteRawLine(Arg.Is<string>(line => line.Contains("host failed to continue", StringComparison.Ordinal)));
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
        DashboardFrame frame = new(width, height, new Text("HEADER"), 2, new Text("Logs"), [new Text("log")], new Text("q quit"));
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
        DashboardFrame frame = new(3, 6, new Text("H"), 1, new Text("Logs"), viewport.RenderRows(new DefaultTheme().Selection), new Text("q"));
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
        IClipboardWriter? clipboard = null, ConsoleInterruptCoordinator? interrupts = null, CompactLogBuffer? logBuffer = null)
    {
        IPlatform platform = Substitute.For<IPlatform>();
        return new CompactRenderer(interaction, new FunctionPalette(), new CompactDashboardShortcutLabels(platform),
            clipboard: clipboard, interrupts: interrupts, terminal: terminal, logBuffer: logBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<string> FirstText(CompactLogBuffer buffer) => new(buffer.Snapshot()[0].Text);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<string> reference) => reference.TryGetTarget(out _);

    private static HostLogEntry Log(string message, LogLevel level = LogLevel.Information)
        => new(DateTimeOffset.UnixEpoch, "Host.Test", level, default, message, null, HostLogEntry.EmptyAttributes);

    private static TerminalInputEvent Key(char character, ConsoleKey key)
        => new(TerminalInputKind.Key, new ConsoleKeyInfo(character, key, false, false, false));

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
        private readonly Channel<(TerminalInputEvent Input, TaskCompletionSource? Processed)> _input =
            Channel.CreateUnbounded<(TerminalInputEvent, TaskCompletionSource?)>();
        private readonly Channel<bool> _frames = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly Lock _lock = new();
        private string _text = string.Empty;
        private int _logTop;
        private int _frameCount;

        public TerminalSize Size { get; init; } = new(120, 24);
        public Action<bool>? MouseCaptureChanging { get; init; }
        public Action? Rendering { get; init; }
        public bool MouseCapture { get; private set; } = true;
        public bool Disposed { get; private set; }
        public bool Restored { get; private set; }
        public string Text { get { lock (_lock) { return _text; } } }
        public int LogTop { get { lock (_lock) { return _logTop; } } }
        public int FrameCount { get { lock (_lock) { return _frameCount; } } }

        public void Send(TerminalInputEvent input) => _input.Writer.TryWrite((input, null));

        public async Task SendAndWaitAsync(TerminalInputEvent input, CancellationToken cancellationToken)
        {
            TaskCompletionSource processed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _input.Writer.TryWrite((input, processed));
            await processed.Task.WaitAsync(cancellationToken);
        }

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
            await foreach ((TerminalInputEvent input, TaskCompletionSource? processed) in _input.Reader.ReadAllAsync(cancellationToken))
            {
                yield return input;
                processed?.TrySetResult();
            }
        }

        public void Render(IRenderable frame)
        {
            Rendering?.Invoke();
            using StringWriter writer = new();
            CreateConsole(writer, Size.Width, Size.Height).Write(frame);
            lock (_lock)
            {
                _text = writer.ToString();
                _logTop = ((DashboardFrame)frame).LogTop;
                _frameCount++;
            }

            _frames.Writer.TryWrite(true);
        }

        public void WriteControl(string sequence) { }
        public void SetMouseCapture(bool enabled)
        {
            MouseCaptureChanging?.Invoke(enabled);
            MouseCapture = enabled;
        }
        public void Restore() => Restored = true;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
