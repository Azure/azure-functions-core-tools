// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Console.Clipboard;
using Azure.Functions.Cli.Console.Terminal;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console.Clipboard;

public class ClipboardWriterTests
{
    [Fact]
    public async Task ConfirmedLocalWindows_UsesOnlyNativeWrite()
    {
        Fixture fixture = new(windows: true);
        fixture.Native.IsLocalSession.Returns(true);
        fixture.Native.CopyAsync("selection", Arg.Any<CancellationToken>())
            .Returns(new ClipboardCopyResult(ClipboardCopyStatus.Confirmed, "copied"));

        var result = await fixture.Writer.CopyAsync("selection", fixture.Terminal, CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Confirmed);
        _ = fixture.Runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Theory]
    [InlineData("SSH_CONNECTION")]
    [InlineData("SSH_TTY")]
    [InlineData("CODESPACES")]
    [InlineData("VSCODE_AGENT_FOLDER")]
    [InlineData("WSL_DISTRO_NAME")]
    [InlineData("REMOTE_CONTAINERS")]
    [InlineData("REMOTE_CONTAINERS_IPC")]
    [InlineData("TMUX")]
    public async Task RemoteSession_NeverWritesServerDesktopClipboard(string variable)
    {
        Fixture fixture = new(windows: true);
        fixture.Variables[variable] = "present";
        fixture.Native.IsLocalSession.Returns(true);

        var result = await fixture.Writer.CopyAsync("remote text", fixture.Terminal, CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Sent);
        _ = fixture.Native.DidNotReceiveWithAnyArgs().CopyAsync(default!, default);
        _ = fixture.Runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        fixture.Terminal.Received(1).WriteControl("\u001b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes("remote text")) + "\u0007");
    }

    [Theory]
    [InlineData("Apple_Terminal")]
    [InlineData("iTerm.app")]
    public async Task LocalMac_UsesSystemPbcopyWithUtf8Stdin(string terminalProgram)
    {
        Fixture fixture = new(mac: true);
        fixture.Variables["TERM_PROGRAM"] = terminalProgram;
        const string text = "private\n界😀";

        var result = await fixture.Writer.CopyAsync(text, fixture.Terminal, CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Confirmed);
        await fixture.Runner.Received(1).RunAsync(Arg.Is<ProcessRunRequest>(request =>
            request.FileName == "/usr/bin/pbcopy" && request.StandardInput == text && request.Arguments.Count == 0
            && request.Timeout == TimeSpan.FromSeconds(5) && request.MaximumCapturedOutputCharacters == 4096
            && request.EnvironmentVariables!["LC_CTYPE"] == "en_US.UTF-8"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("wayland", "wl-copy")]
    [InlineData("x11", "xsel")]
    public async Task LocalLinux_UsesSuitableOptionalHelper(string sessionType, string executable)
    {
        Fixture fixture = Linux(sessionType);
        var result = await fixture.Writer.CopyAsync("data", fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Confirmed);
        await fixture.Runner.Received(1).RunAsync(Arg.Is<ProcessRunRequest>(request =>
            request.FileName == executable && request.StandardInput == "data"
            && !request.Arguments.Contains("data")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SshWithForwardedDisplay_StillUsesTerminalClipboard()
    {
        Fixture fixture = Linux("x11");
        fixture.Variables["SSH_CONNECTION"] = "remote";
        fixture.Variables["DISPLAY"] = "localhost:10.0";
        var result = await fixture.Writer.CopyAsync("data", fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Sent);
        _ = fixture.Runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(false, false, 1)]
    public async Task HelperFailure_IsExplicitWithoutSilentFallbackOrDiagnosticLeak(bool missing, bool timeout, int? exitCode)
    {
        Fixture fixture = Linux("wayland");
        fixture.Runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(exitCode, "secret selection", "secret selection", timeout, missing));

        var result = await fixture.Writer.CopyAsync("secret selection", fixture.Terminal, CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        result.Message.Should().NotContain("secret selection");
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
        await fixture.Runner.Received(1).RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HelperIoError_DoesNotLeakSelectionOrTryAnotherDestination()
    {
        Fixture fixture = Linux("x11");
        fixture.Runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ProcessOutcome>(new IOException("private selection")));
        var result = await fixture.Writer.CopyAsync("private selection", fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        result.Message.Should().NotContain("private selection");
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Fact]
    public async Task Osc52_ExactlyAtLimit_IsCompleteAndUnconfirmed()
    {
        Fixture fixture = new();
        string text = new('x', ClipboardWriter.MaximumOsc52Bytes);
        string? sequence = null;
        fixture.Terminal.When(terminal => terminal.WriteControl(Arg.Any<string>())).Do(call => sequence = call.Arg<string>());

        var result = await fixture.Writer.CopyAsync(text, fixture.Terminal, CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Sent);
        result.Message.Should().Contain("not confirmed");
        sequence.Should().StartWith("\u001b]52;c;").And.EndWith("\u0007");
        Encoding.UTF8.GetString(Convert.FromBase64String(sequence![7..^1])).Should().Be(text);
    }

    [Fact]
    public async Task Osc52_Utf8ByteLimitRejectsRatherThanTruncates()
    {
        Fixture fixture = new();
        string text = new('界', (ClipboardWriter.MaximumOsc52Bytes / 3) + 1);
        var result = await fixture.Writer.CopyAsync(text, fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Fact]
    public async Task LocalLimit_IsCheckedBeforeNativeClipboardUse()
    {
        Fixture fixture = new(windows: true);
        fixture.Native.IsLocalSession.Returns(true);
        var result = await fixture.Writer.CopyAsync(new string('x', ClipboardWriter.MaximumLocalBytes + 1), fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        _ = fixture.Native.DidNotReceiveWithAnyArgs().CopyAsync(default!, default);
    }

    [Fact]
    public async Task LossyText_IsRejected()
    {
        Fixture fixture = new();
        string[] invalidTexts = ["a\0b", new string('\ud800', 1)];
        foreach (string text in invalidTexts)
        {
            var result = await fixture.Writer.CopyAsync(text, fixture.Terminal, CancellationToken.None);
            result.Status.Should().Be(ClipboardCopyStatus.Failed);
        }

        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Fact]
    public async Task UnsupportedTerminal_ExplainsNativeCopyEscapeRoute()
    {
        Fixture fixture = new();
        fixture.Variables["TERM"] = "dumb";
        var result = await fixture.Writer.CopyAsync("data", fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        result.Message.Should().Contain("Release mouse capture");
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Fact]
    public async Task OnlyOneCopyPayload_IsInFlight()
    {
        Fixture fixture = new(windows: true);
        fixture.Native.IsLocalSession.Returns(true);
        TaskCompletionSource<ClipboardCopyResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Native.CopyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(completion.Task);

        Task<ClipboardCopyResult> first = fixture.Writer.CopyAsync("first", fixture.Terminal, CancellationToken.None);
        var second = await fixture.Writer.CopyAsync("second", fixture.Terminal, CancellationToken.None);
        second.Status.Should().Be(ClipboardCopyStatus.Failed);
        second.Message.Should().Contain("already in progress");
        completion.SetResult(new ClipboardCopyResult(ClipboardCopyStatus.Confirmed, "copied"));
        (await first).Status.Should().Be(ClipboardCopyStatus.Confirmed);
        await fixture.Native.Received(1).CopyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutClipboardSideEffects()
    {
        Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> copy = () => fixture.Writer.CopyAsync("data", fixture.Terminal, cancellation.Token);
        await copy.Should().ThrowAsync<OperationCanceledException>();
        fixture.Terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
    }

    [Fact]
    public async Task BrokenTerminalOutput_IsAnExplicitFailure()
    {
        Fixture fixture = new();
        fixture.Terminal.When(terminal => terminal.WriteControl(Arg.Any<string>())).Do(_ => throw new IOException("closed"));
        var result = await fixture.Writer.CopyAsync("data", fixture.Terminal, CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
    }

    [Fact]
    public void NullDependencies_AreRejected()
    {
        var platform = Substitute.For<IPlatform>();
        var environment = Substitute.For<IProcessEnvironment>();
        var runner = Substitute.For<IProcessRunner>();
        Action platformNull = () => new ClipboardWriter(null!, environment, runner);
        Action environmentNull = () => new ClipboardWriter(platform, null!, runner);
        Action runnerNull = () => new ClipboardWriter(platform, environment, null!);
        platformNull.Should().Throw<ArgumentNullException>();
        environmentNull.Should().Throw<ArgumentNullException>();
        runnerNull.Should().Throw<ArgumentNullException>();
    }

    private static Fixture Linux(string sessionType)
    {
        Fixture fixture = new();
        fixture.Variables["XDG_SESSION_TYPE"] = sessionType;
        fixture.Variables["WAYLAND_DISPLAY"] = "wayland-0";
        fixture.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        fixture.Variables["DISPLAY"] = ":0";
        return fixture;
    }

    private sealed class Fixture
    {
        public Fixture(bool windows = false, bool mac = false)
        {
            IPlatform platform = Substitute.For<IPlatform>();
            platform.IsWindows.Returns(windows);
            platform.IsMacOS.Returns(mac);
            IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
            environment.Get(Arg.Any<string>()).Returns(call => Variables.GetValueOrDefault(call.Arg<string>()));
            Runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
                .Returns(new ProcessOutcome(0, string.Empty, string.Empty, false, false));
            Writer = new ClipboardWriter(platform, environment, Runner, Native);
        }

        public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

        public IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();

        public IWindowsClipboard Native { get; } = Substitute.For<IWindowsClipboard>();

        public ITerminalSession Terminal { get; } = Substitute.For<ITerminalSession>();

        public ClipboardWriter Writer { get; }
    }
}
