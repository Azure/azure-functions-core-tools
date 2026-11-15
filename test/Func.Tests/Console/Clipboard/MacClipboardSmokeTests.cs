// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Console.Clipboard;
using Azure.Functions.Cli.Console.Terminal;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console.Clipboard;

public class MacClipboardSmokeTests
{
    [MacClipboardTheory]
    [InlineData("Apple_Terminal")]
    [InlineData("iTerm.app")]
    public async Task LocalHelper_ConfirmsLosslessUnicodeAndNewlineCopy(string terminalProgram)
    {
        IPlatform platform = Substitute.For<IPlatform>();
        platform.IsMacOS.Returns(true);
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("TERM_PROGRAM").Returns(terminalProgram);
        ITerminalSession terminal = Substitute.For<ITerminalSession>();
        ProcessRunner runner = new();
        ClipboardWriter writer = new(platform, environment, runner);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        const string text = "func TUI native clipboard\n\u754c\U0001f600e\u0301\nlogical line";

        ClipboardCopyResult result = await writer.CopyAsync(text, terminal, deadline.Token);

        result.Status.Should().Be(ClipboardCopyStatus.Confirmed, result.Message);
        terminal.DidNotReceiveWithAnyArgs().WriteControl(default!);
        ProcessOutcome pasted = await runner.RunAsync(new ProcessRunRequest("/usr/bin/pbpaste", [],
            WorkingDirectory: null, Timeout: TimeSpan.FromSeconds(5),
            EnvironmentVariables: new Dictionary<string, string?>
            {
                ["LC_CTYPE"] = "en_US.UTF-8",
                ["LC_ALL"] = "en_US.UTF-8",
            }), deadline.Token);
        pasted.ExecutableNotFound.Should().BeFalse();
        pasted.TimedOut.Should().BeFalse();
        pasted.ExitCode.Should().Be(0, pasted.StandardError);
        pasted.StandardOutput.Should().Be(text);
    }

    private sealed class MacClipboardTheoryAttribute : TheoryAttribute
    {
        public MacClipboardTheoryAttribute()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Skip = "Native pbcopy qualification runs on macOS only.";
            }
            else if (Environment.GetEnvironmentVariable("FUNC_CLIPBOARD_NATIVE_TESTS") != "1")
            {
                Skip = "Set FUNC_CLIPBOARD_NATIVE_TESTS=1 only on a disposable desktop: this test replaces the clipboard.";
            }
        }
    }
}
