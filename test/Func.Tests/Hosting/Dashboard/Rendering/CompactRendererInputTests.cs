// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Threading.Channels;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using NSubstitute;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactRendererInputTests
{
    private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task OnStartAsync_WhenInjectedConsoleIsNonInteractive_DoesNotReadDefaultInput()
    {
        using var writer = new StringWriter();
        var console = NewConsole(writer, interactive: false);
        var interaction = NewInteraction(console, interactive: true);
        await using var renderer = NewRenderer(console, interaction);

        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        Task? inputTask = GetInputTask(renderer);
        Exception? readFailure = inputTask is null
            ? null
            : await Record.ExceptionAsync(() => inputTask.WaitAsync(_testTimeout));
        Exception? summaryFailure = await Record.ExceptionAsync(() => SummarizeAsync(renderer));

        readFailure.Should().BeNull();
        summaryFailure.Should().BeNull();
        inputTask.Should().BeNull();
        console.Profile.Capabilities.Interactive.Should().BeFalse();
        string output = writer.ToString();
        int enterIndex = output.IndexOf("\u001b[?1049h\u001b[H", StringComparison.Ordinal);
        int exitIndex = output.IndexOf("\u001b[?1049l", StringComparison.Ordinal);
        enterIndex.Should().BeGreaterThanOrEqualTo(0);
        exitIndex.Should().BeGreaterThan(enterIndex);
        output.Should().Contain("Azure Functions host stopped");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OnStartAsync_RequiresBothInteractionAndConsoleInputSupport(bool interactionSupported, bool inputSupported)
    {
        using var writer = new StringWriter();
        var realConsole = NewConsole(writer, inputSupported);
        var input = Substitute.For<IAnsiConsoleInput>();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new TaskCompletionSource<ConsoleKeyInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        input.ReadKeyAsync(true, Arg.Any<CancellationToken>()).Returns(call =>
        {
            readStarted.TrySetResult();
            return key.Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var console = new InputConsole(realConsole, input);
        var interaction = NewInteraction(console, interactionSupported);
        await using var renderer = NewRenderer(console, interaction);

        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        Task? inputTask = GetInputTask(renderer);
        try
        {
            if (interactionSupported && inputSupported)
            {
                await readStarted.Task.WaitAsync(_testTimeout);
                inputTask.Should().NotBeNull();
            }
            else
            {
                inputTask.Should().BeNull();
            }
        }
        finally
        {
            await SummarizeAsync(renderer);
        }

        if (inputTask is not null)
        {
            inputTask.IsCompleted.Should().BeTrue();
        }
        else
        {
            await input.DidNotReceive().ReadKeyAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnSummaryAsync_WhenReadIsPending_CancelsAndJoinsBeforeWritingSummary(bool cancelLifetime)
    {
        using var writer = new StringWriter();
        using var lifetime = new CancellationTokenSource();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = Substitute.For<IAnsiConsoleInput>();
        input.ReadKeyAsync(true, Arg.Any<CancellationToken>()).Returns(call => ReadAsync(call.Arg<CancellationToken>()));
        var console = new InputConsole(NewConsole(writer, interactive: true), input);
        var interaction = NewInteraction(console, interactive: true);
        await using var renderer = NewRenderer(console, interaction);

        await renderer.OnStartAsync(new DashboardState(), lifetime.Token);
        Task? summaryTask = null;
        try
        {
            await readStarted.Task.WaitAsync(_testTimeout);
            if (cancelLifetime)
            {
                lifetime.Cancel();
            }

            summaryTask = SummarizeAsync(renderer);
            await cancellationObserved.Task.WaitAsync(_testTimeout);
            summaryTask.IsCompleted.Should().BeFalse();
            interaction.DidNotReceive().WriteLine(Arg.Any<string>());
        }
        finally
        {
            releaseRead.TrySetResult();
            await (summaryTask ?? SummarizeAsync(renderer));
        }

        GetInputTask(renderer)!.IsCanceled.Should().BeTrue();
        await input.Received(1).ReadKeyAsync(true, Arg.Any<CancellationToken>());
        writer.ToString().Should().Contain("Azure Functions host stopped");

        async Task<ConsoleKeyInfo?> ReadAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult());
            readStarted.TrySetResult();
            await releaseRead.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    [Fact]
    public async Task OnStartAsync_WhenAlreadyCanceled_DoesNotReadInput()
    {
        using var writer = new StringWriter();
        using var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        var input = Substitute.For<IAnsiConsoleInput>();
        var console = new InputConsole(NewConsole(writer, interactive: true), input);
        var interaction = NewInteraction(console, interactive: true);
        await using var renderer = NewRenderer(console, interaction);

        await renderer.OnStartAsync(new DashboardState(), lifetime.Token);
        await SummarizeAsync(renderer);

        GetInputTask(renderer)!.IsCanceled.Should().BeTrue();
        await input.DidNotReceive().ReadKeyAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        writer.ToString().Should().Contain("Azure Functions host stopped");
    }

    [Fact]
    public async Task OnStartAsync_WhenKeysArrive_HandlesQuitAndJoinsPendingRead()
    {
        using var writer = new StringWriter();
        var keys = Channel.CreateUnbounded<ConsoleKeyInfo?>();
        keys.Writer.TryWrite(null);
        keys.Writer.TryWrite(new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false));
        keys.Writer.TryWrite(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));
        var nextReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readCount = 0;
        var input = Substitute.For<IAnsiConsoleInput>();
        input.ReadKeyAsync(true, Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (++readCount == 4)
            {
                nextReadStarted.TrySetResult();
            }

            return keys.Reader.ReadAsync(call.Arg<CancellationToken>()).AsTask();
        });
        var console = new InputConsole(NewConsole(writer, interactive: true), input);
        var interaction = NewInteraction(console, interactive: true);
        await using var renderer = NewRenderer(console, interaction);
        int shutdownRequests = 0;
        renderer.ShutdownRequested += () => shutdownRequests++;

        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        try
        {
            await nextReadStarted.Task.WaitAsync(_testTimeout);
            shutdownRequests.Should().Be(1);
        }
        finally
        {
            await SummarizeAsync(renderer);
        }

        GetInputTask(renderer)!.IsCanceled.Should().BeTrue();
        readCount.Should().Be(4);
        writer.ToString().Should().Contain("Azure Functions host stopped");
    }

    [Fact]
    public async Task OnSummaryAsync_WhenInputFails_PropagatesOriginalFailure()
    {
        using var writer = new StringWriter();
        var failure = new IOException("Input device failed.");
        var input = Substitute.For<IAnsiConsoleInput>();
        input.ReadKeyAsync(true, Arg.Any<CancellationToken>()).Returns(Task.FromException<ConsoleKeyInfo?>(failure));
        var console = new InputConsole(NewConsole(writer, interactive: true), input);
        var interaction = NewInteraction(console, interactive: true);
        await using var renderer = NewRenderer(console, interaction);

        await renderer.OnStartAsync(new DashboardState(), CancellationToken.None);
        Task inputTask = GetInputTask(renderer)!;
        Exception? readFailure = await Record.ExceptionAsync(() => inputTask.WaitAsync(_testTimeout));
        Exception? summaryFailure = await Record.ExceptionAsync(() => SummarizeAsync(renderer));

        readFailure.Should().BeSameAs(failure);
        summaryFailure.Should().BeSameAs(failure);
        inputTask.IsFaulted.Should().BeTrue();
        await input.Received(1).ReadKeyAsync(true, Arg.Any<CancellationToken>());
    }

    private static IAnsiConsole NewConsole(StringWriter writer, bool interactive)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 120;
        console.Profile.Height = 24;
        console.Profile.Capabilities.AlternateBuffer = true;
        return console;
    }

    private static IInteractionService NewInteraction(IAnsiConsole console, bool interactive)
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.Theme.Returns(new DefaultTheme());
        interaction.IsInteractive.Returns(interactive);
        interaction.When(service => service.WriteLine(Arg.Any<string>())).Do(call => console.WriteLine(call.Arg<string>()));
        interaction.When(service => service.WriteBlankLine()).Do(_ => console.WriteLine());
        return interaction;
    }

    private static CompactRenderer NewRenderer(IAnsiConsole console, IInteractionService interaction)
    {
        var platform = Substitute.For<IPlatform>();
        platform.IsMacOS.Returns(true);
        return new CompactRenderer(interaction, new FunctionPalette(), new CompactDashboardShortcutLabels(platform), platform, console);
    }

    private static Task? GetInputTask(CompactRenderer renderer)
        => (Task?)typeof(CompactRenderer).GetField("_inputTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);

    private static Task SummarizeAsync(CompactRenderer renderer)
        => renderer.OnSummaryAsync(new DashboardState().BuildSummary("stopped", DateTimeOffset.UtcNow), CancellationToken.None).WaitAsync(_testTimeout);

    private sealed class InputConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
    {
        private readonly IAnsiConsole _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private readonly IAnsiConsoleInput _input = input ?? throw new ArgumentNullException(nameof(input));

        public Profile Profile => _inner.Profile;

        public IAnsiConsoleCursor Cursor => _inner.Cursor;

        public IAnsiConsoleInput Input => _input;

        public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

        public RenderPipeline Pipeline => _inner.Pipeline;

        public void Clear(bool home) => _inner.Clear(home);

        public void Write(IRenderable renderable) => _inner.Write(renderable);
    }
}