// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Processes;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(15);

    private static (string FileName, string[] Args) ShellInvocation(string script)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("cmd.exe", ["/c", script]);
        }

        return ("/bin/sh", ["-c", script]);
    }

    [Fact]
    public async Task RunAsync_Success_CapturesStdout()
    {
        (string fileName, string[] args) = ShellInvocation("echo hello");

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, WorkingDirectory: null, _defaultTimeout),
            CancellationToken.None);

        outcome.ExecutableNotFound.Should().BeFalse();
        outcome.TimedOut.Should().BeFalse();
        outcome.ExitCode.Should().Be(0);
        outcome.StandardOutput.Should().Contain("hello");
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_IsCaptured()
    {
        (string fileName, string[] args) = ShellInvocation("exit 7");

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, WorkingDirectory: null, _defaultTimeout),
            CancellationToken.None);

        outcome.ExitCode.Should().Be(7);
        outcome.TimedOut.Should().BeFalse();
        outcome.ExecutableNotFound.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_BogusExecutable_ReturnsExecutableNotFound()
    {
        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(
                FileName: "this-does-not-exist-azurite-cli-test",
                Arguments: [],
                WorkingDirectory: null,
                Timeout: _defaultTimeout),
            CancellationToken.None);

        outcome.ExecutableNotFound.Should().BeTrue();
        outcome.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_Timeout_IsReported()
    {
        (string fileName, string[] args) = ShellInvocation(OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 30 > nul"
            : "sleep 30");

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, WorkingDirectory: null, Timeout: TimeSpan.FromMilliseconds(250)),
            CancellationToken.None);

        outcome.TimedOut.Should().BeTrue();
        outcome.ExitCode.Should().BeNull();
    }
}
