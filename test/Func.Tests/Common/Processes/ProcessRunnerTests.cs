// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Common.Processes;

namespace Azure.Functions.Cli.Tests.Common.Processes;

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

    private static (string FileName, string[] Args) SleepingProcess()
    {
        return OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"])
            : ("/bin/sh", ["-c", "sleep 30"]);
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
        (string fileName, string[] args) = SleepingProcess();

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, WorkingDirectory: null, Timeout: TimeSpan.FromMilliseconds(250)),
            CancellationToken.None);

        outcome.TimedOut.Should().BeTrue();
        outcome.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_StandardInput_IsWrittenWithoutBomOrAddedNewline()
    {
        (string FileName, string[] Args) command = OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::OpenStandardInput().CopyTo([Console]::OpenStandardOutput())"])
            : ("/bin/cat", []);
        const string input = "exact input\nwith no final newline";

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(command.FileName, command.Args, null, _defaultTimeout, StandardInput: input), CancellationToken.None);

        outcome.ExitCode.Should().Be(0);
        outcome.StandardOutput.Should().Be(input);
    }

    [Fact]
    public async Task RunAsync_BlockedStandardInput_IsCoveredByWholeOperationTimeout()
    {
        (string fileName, string[] args) = SleepingProcess();
        var elapsed = Stopwatch.StartNew();

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, null, TimeSpan.FromMilliseconds(250), StandardInput: new string('x', 500_000)),
            CancellationToken.None);

        outcome.TimedOut.Should().BeTrue();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_CancellationOfBlockedInput_PropagatesPromptly()
    {
        (string fileName, string[] args) = SleepingProcess();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(250));
        Func<Task> run = () => _runner.RunAsync(
            new ProcessRunRequest(fileName, args, null, _defaultTimeout, StandardInput: new string('x', 500_000)), cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_InputByteLimit_RejectsBeforeStartingExecutable()
    {
        Func<Task> run = () => _runner.RunAsync(
            new ProcessRunRequest("must-not-start", [], null, _defaultTimeout, StandardInput: "界", MaximumStandardInputBytes: 2),
            CancellationToken.None);
        await run.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void ProcessRequest_DiagnosticFormattingDoesNotContainClipboardText()
    {
        ProcessRunRequest request = new("helper", [], null, _defaultTimeout, StandardInput: "private clipboard selection");
        request.ToString().Should().NotContain("private clipboard selection").And.Contain("<redacted>");
    }

    [Fact]
    public async Task RunAsync_CapturedOutput_IsBoundedButFullyDrained()
    {
        (string FileName, string[] Args) command = OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write(('x' * 100000))"])
            : ("/bin/sh", ["-c", "yes x | head -c 100000"]);

        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(command.FileName, command.Args, null, _defaultTimeout, MaximumCapturedOutputCharacters: 100), CancellationToken.None);

        outcome.ExitCode.Should().Be(0);
        outcome.TimedOut.Should().BeFalse();
        outcome.StandardOutput.Length.Should().Be(100);
    }

    [Fact]
    public async Task RunAsync_EnvironmentOverrides_AreAppliedOnlyToChild()
    {
        (string fileName, string[] args) = ShellInvocation(OperatingSystem.IsWindows()
            ? "echo %FUNC_TEST_STDIN_LOCALE%"
            : "printf '%s' \"$FUNC_TEST_STDIN_LOCALE\"");
        ProcessOutcome outcome = await _runner.RunAsync(
            new ProcessRunRequest(fileName, args, null, _defaultTimeout,
                EnvironmentVariables: new Dictionary<string, string?> { ["FUNC_TEST_STDIN_LOCALE"] = "utf8-test" }),
            CancellationToken.None);
        outcome.StandardOutput.Trim().Should().Be("utf8-test");
    }

    [Fact]
    public async Task RunAsync_InheritedOutputPipe_IsIncludedInDeadline()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string script = "$info = [Diagnostics.ProcessStartInfo]::new();"
            + "$info.FileName = 'powershell.exe';"
            + "$info.Arguments = '-NoLogo -NoProfile -NonInteractive -Command Start-Sleep -Seconds 8';"
            + "$info.UseShellExecute = $false;"
            + "$child = [Diagnostics.Process]::Start($info);"
            + "[Console]::Out.WriteLine(('OWNED:{0}:{1}' -f $child.Id, $child.StartTime.ToUniversalTime().Ticks));";
        ProcessOutcome outcome = await _runner.RunAsync(new ProcessRunRequest(
            "powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], null, TimeSpan.FromSeconds(2)),
            CancellationToken.None);
        string[] identity = outcome.StandardOutput.Trim().Split(':');
        identity.Length.Should().Be(3, outcome.StandardError);
        int processId = int.Parse(identity[1], System.Globalization.CultureInfo.InvariantCulture);
        long startTicks = long.Parse(identity[2], System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            outcome.TimedOut.Should().BeTrue("the parent exited, but its child still holds the inherited stdout/stderr pipes");
        }
        finally
        {
            try
            {
                using var child = Process.GetProcessById(processId);
                if (child.StartTime.ToUniversalTime().Ticks == startTicks && !child.HasExited)
                {
                    child.Kill();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch (ArgumentException)
            {
                // The explicitly owned child can finish before cleanup opens its process handle.
            }
        }
    }
}
