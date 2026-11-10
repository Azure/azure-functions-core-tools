// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Azure.Functions.Cli.Console.Terminal;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class TerminalSessionTests
{
    [Fact]
    public async Task Render_UsesAbsoluteHomeWithoutTrailingNewlineOrScrollbackErase()
    {
        StringWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        writer.GetStringBuilder().Clear();

        session.Render(new Text("last cell"));

        writer.ToString().Should().StartWith("\u001b[H");
        writer.ToString().Should().Contain("last cell").And.NotEndWith("\n").And.NotContain("\u001b[3J");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Restoration_IsIdempotentAndJoinsTheNativeReader()
    {
        StringWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        await driver.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.SetMouseCapture(false);
        session.SetMouseCapture(true);
        session.Restore();
        session.Restore();
        await session.DisposeAsync();
        await session.DisposeAsync();

        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
        writer.ToString().Split("\u001b[?1049l", StringSplitOptions.None).Length.Should().Be(2);
        writer.ToString().Should().Contain("\u001b[?1002h").And.Contain("\u001b[?1006h").And.NotContain("\u001b[?1003h");
    }

    [Fact]
    public async Task ConsumerCancellation_StopsAndJoinsReaderBeforeReturning()
    {
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(new StringWriter()), driver);
        session.Activate();
        using CancellationTokenSource cancellation = new();
        await using var reader = session.ReadAsync(cancellation.Token).GetAsyncEnumerator();
        Task<bool> next = reader.MoveNextAsync().AsTask();
        await driver.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Func<Task> read = async () => await next.WaitAsync(TimeSpan.FromSeconds(5));
        await read.Should().ThrowAsync<OperationCanceledException>();
        driver.ActiveReads.Should().Be(0);
        driver.RestoreCount.Should().Be(0, "input cancellation is not terminal restoration");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_WakesAPendingConsumerAndDoesNotRaceDisposedCancellationSource()
    {
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(new StringWriter()), driver);
        session.Activate();
        await using var reader = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        Task<bool> next = reader.MoveNextAsync().AsTask();

        await session.DisposeAsync();

        Func<Task> read = async () => await next.WaitAsync(TimeSpan.FromSeconds(5));
        await read.Should().ThrowAsync<OperationCanceledException>();
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task InputFailures_ReachConsumerAndDisposalStillRestoresEverything()
    {
        FakeDriver driver = new() { ReadFailure = new IOException("native read failed") };
        TerminalSession session = new(CreateConsole(new StringWriter()), driver);
        session.Activate();
        await using var reader = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        Func<Task> read = async () => await reader.MoveNextAsync();
        await read.Should().ThrowAsync<IOException>().WithMessage("native read failed");

        Func<Task> dispose = () => session.DisposeAsync().AsTask();
        await dispose.Should().ThrowAsync<IOException>();
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task InputRestorationFailure_DoesNotPreventVisibleScreenRestoration()
    {
        StringWriter writer = new();
        FakeDriver driver = new() { RestoreInputFailure = new IOException("native restore failed") };
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();

        Func<Task> dispose = () => session.DisposeAsync().AsTask();

        await dispose.Should().ThrowAsync<IOException>().WithMessage("native restore failed");
        writer.ToString().Should().Contain("\u001b[?1049l");
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CleanupFailure_DoesNotReplaceOriginalInputFailure()
    {
        FailingWriter writer = new();
        FakeDriver driver = new() { ReadFailure = new IOException("original read failure") };
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        await driver.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Fail = true;

        Func<Task> dispose = () => session.DisposeAsync().AsTask();

        await dispose.Should().ThrowAsync<IOException>().WithMessage("original read failure");
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task PartialActivation_RestoresNativeModesEvenWhenOutputFails()
    {
        FailingWriter writer = new() { Fail = true };
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        Action activate = session.Activate;

        activate.Should().Throw<IOException>();
        await FluentActions.Awaiting(() => session.DisposeAsync().AsTask()).Should().ThrowAsync<IOException>();
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task FailedActivation_OwnsCleanupEvenWhenRestorationFinishesAfterTheException()
    {
        using FailedActivationWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        try
        {
            Action activate = session.Activate;
            activate.Should().Throw<IOException>().Which.Should().BeSameAs(writer.Failure);
            await writer.Restoring.Task.WaitAsync(TimeSpan.FromSeconds(5));
            driver.DisposeCount.Should().Be(0);
        }
        finally
        {
            writer.Release.Set();
        }

        await driver.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
        writer.ToString().Should().Contain(TerminalSession.LeaveScreen);
        await session.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDeadline_DoesNotAbandonCleanupWhenOutputLaterRecovers(bool signalCleanupFails)
    {
        using BlockingWriter writer = new() { BlockSequence = "\u001b[H" };
        IOException? signalFailure = signalCleanupFails ? new("signal cleanup failure") : null;
        FakeDriver driver = new() { SignalDisposeFailure = signalFailure };
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        var render = Task.Run(() => session.Render(new Text("blocked frame")));
        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await FluentActions.Awaiting(() => session.DisposeAsync().AsTask())
                .Should().ThrowAsync<TimeoutException>();

            driver.InputRestored.Should().BeTrue();
            driver.DisposeCount.Should().Be(0);
        }
        finally
        {
            writer.Release.Set();
        }

        await render.WaitAsync(TimeSpan.FromSeconds(5));
        await driver.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        writer.ToString().Split(TerminalSession.LeaveScreen, StringSplitOptions.None).Length.Should().Be(2);
        if (signalFailure is null)
        {
            await session.DisposeAsync();
        }
        else
        {
            (await FluentActions.Awaiting(() => session.DisposeAsync().AsTask()).Should().ThrowAsync<IOException>())
                .Which.Should().BeSameAs(signalFailure);
        }
    }

    [Fact]
    public async Task BrokenRestoreOutput_DoesNotPreventNativeRestoreOrReaderJoin()
    {
        FailingWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        await driver.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Fail = true;

        Func<Task> dispose = () => session.DisposeAsync().AsTask();
        await dispose.Should().ThrowAsync<IOException>();
        driver.RestoreCount.Should().Be(1);
        driver.DisposeCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task EmergencyRestore_DoesNotWaitForABlockedFrame()
    {
        using BlockingWriter writer = new() { BlockSequence = "\u001b[H" };
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        var render = Task.Run(() => session.Render(new Text("blocked frame")));
        await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var elapsed = Stopwatch.StartNew();
        session.Restore();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        driver.InputRestored.Should().BeTrue();
        render.IsCompleted.Should().BeFalse();
        driver.RestoreCount.Should().Be(0, "native output modes must remain enabled until protocol cleanup can write");
        Task disposal = session.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse("the skipped protocol work must remain pending");
        writer.Release.Set();
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        writer.ToString().Split(TerminalSession.LeaveScreen, StringSplitOptions.None).Length.Should().Be(2);
        driver.RestoreCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task EmergencyRestore_RestoresInputBeforeBoundedWaitForStalledControlOutput()
    {
        using BlockingWriter writer = new() { BlockSequence = "\u001b[?1049l" };
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();

        var elapsed = Stopwatch.StartNew();
        session.Restore();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        driver.InputRestored.Should().BeTrue();
        await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Release.Set();
        await session.DisposeAsync();
        driver.RestoreCount.Should().Be(1);
        driver.ActiveReads.Should().Be(0);
    }

    [Fact]
    public async Task Suspension_RestoresBeforeStopAndReacquiresWithoutResettingMouseChoice()
    {
        StringWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();
        session.SetMouseCapture(false);
        driver.SuspendCallback!();
        driver.SuspendCount.Should().Be(1);
        string suspendedOutput = writer.ToString();
        session.Render(new Text("must not paint while suspended"));
        writer.ToString().Should().Be(suspendedOutput);

        driver.ResumeCallback!();
        driver.ResumeCount.Should().Be(1);
        driver.MouseCapture.Should().BeFalse();
        await using var reader = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Kind.Should().Be(TerminalInputKind.FocusLost);
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Kind.Should().Be(TerminalInputKind.Resize);
        await session.DisposeAsync();
        driver.SignalsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ResumeAfterUncatchableStop_ReappliesRawModeWithoutOverwritingSavedPrivateModes()
    {
        StringWriter writer = new();
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(writer), driver);
        session.Activate();

        driver.ResumeCallback!();

        driver.ResumeCount.Should().Be(1);
        writer.ToString().Split("\u001b[?25s", StringSplitOptions.None).Length.Should().Be(2);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task SecondConsumer_IsRejected()
    {
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(new StringWriter()), driver);
        session.Activate();
        driver.Input.Add(TerminalKey.Create('q'));
        await using var first = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        (await first.MoveNextAsync()).Should().BeTrue();
        await using var second = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        Func<Task> read = async () => await second.MoveNextAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task CapturedKeysRemainKeyEvents()
    {
        FakeDriver driver = new();
        TerminalSession session = new(CreateConsole(new StringWriter()), driver);
        session.Activate();
        driver.Input.Add(TerminalKey.Create('\u0003'));
        await using var reader = session.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Kind.Should().Be(TerminalInputKind.Key);
        reader.Current.Key.Should().Be(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));
        await session.DisposeAsync();
    }

    [Fact]
    public void NullDependencies_AreRejected()
    {
        Action console = () => new TerminalSession(null!, new FakeDriver());
        Action driver = () => new TerminalSession(CreateConsole(new StringWriter()), null!);
        console.Should().Throw<ArgumentNullException>();
        driver.Should().Throw<ArgumentNullException>();
    }

    internal static IAnsiConsole CreateConsole(TextWriter writer)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        return console;
    }

    private sealed class FailingWriter : StringWriter
    {
        public bool Fail { get; set; }

        public override void Write(string? value)
        {
            if (Fail)
            {
                throw new IOException("injected output failure");
            }

            base.Write(value);
        }
    }

    private sealed class BlockingWriter : StringWriter
    {
        public required string BlockSequence { get; init; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new();

        public override void Write(string? value)
        {
            if (value?.Contains(BlockSequence, StringComparison.Ordinal) == true)
            {
                Entered.TrySetResult();
                if (!Release.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The test did not release its blocked terminal writer.");
                }
            }

            base.Write(value);
        }

        protected override void Dispose(bool disposing)
        {
            Release.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FailedActivationWriter : StringWriter
    {
        public IOException Failure { get; } = new("injected activation failure");

        public TaskCompletionSource Restoring { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new();

        public override void Write(string? value)
        {
            if (value == TerminalSession.EnterScreen)
            {
                throw Failure;
            }

            if (value == TerminalSession.LeaveScreen)
            {
                Restoring.TrySetResult();
                if (!Release.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The test did not release its restoration writer.");
                }
            }

            base.Write(value);
        }

        protected override void Dispose(bool disposing)
        {
            Release.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FakeDriver : ITerminalDriver, ITerminalSuspension, ITerminalEmergencyRestoration
    {
        private int _activeReads;

        public bool IsSupported => true;

        public TerminalSize Size => new(80, 24);

        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingCollection<TerminalInputEvent> Input { get; } = new(8);

        public Exception? ReadFailure { get; init; }

        public Exception? RestoreInputFailure { get; init; }

        public Exception? SignalDisposeFailure { get; init; }

        public int ActiveReads => Volatile.Read(ref _activeReads);

        public int RestoreCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int SuspendCount { get; private set; }

        public int ResumeCount { get; private set; }

        public bool MouseCapture { get; private set; } = true;

        public bool SignalsDisposed { get; private set; }

        public bool InputRestored { get; private set; }

        public Action? SuspendCallback { get; private set; }

        public Action? ResumeCallback { get; private set; }

        public void Activate()
        {
        }

        public IEnumerable<TerminalInputEvent> Read(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeReads);
            ReadEntered.TrySetResult();
            try
            {
                if (ReadFailure is not null)
                {
                    throw ReadFailure;
                }

                return Input.TryTake(out TerminalInputEvent? input, 20, cancellationToken) ? [input] : [];
            }
            finally
            {
                Interlocked.Decrement(ref _activeReads);
            }
        }

        public void SetMouseCapture(bool enabled) => MouseCapture = enabled;

        public void Restore() => RestoreCount++;

        public void RestoreInput()
        {
            if (RestoreInputFailure is not null)
            {
                throw RestoreInputFailure;
            }

            InputRestored = true;
        }

        public void Dispose()
        {
            DisposeCount++;
            Input.Dispose();
            Disposed.TrySetResult();
        }

        public IDisposable ObserveSuspension(Action suspend, Action resume)
        {
            SuspendCallback = suspend;
            ResumeCallback = resume;
            return new Registration(this);
        }

        public void Suspend() => SuspendCount++;

        public void Resume() => ResumeCount++;

        private sealed class Registration(FakeDriver driver) : IDisposable
        {
            public void Dispose()
            {
                driver.SignalsDisposed = true;
                if (driver.SignalDisposeFailure is not null)
                {
                    throw driver.SignalDisposeFailure;
                }
            }
        }
    }
}
