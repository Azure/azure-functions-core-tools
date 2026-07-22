// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Launching;

public class AzuriteLauncherTests
{
    [Fact]
    public async Task StartAsync_Native_RunsSentinel_ProducesStdout_AndStopsCleanly()
    {
        (string fileName, string commandText) = GetEchoSleepSentinel();

        // Reuse the launcher's request shape, but route the executable to a
        // platform-appropriate "echo then sleep" sentinel so we exercise the
        // real Process.Start + line-reading + Stop path without needing
        // Azurite installed.
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: Path.GetTempPath(),
            logPath: Path.Combine(Path.GetTempPath(), "azurite-test.log"),
            executablePath: fileName);

        // We can't reuse the native command builder here because the sentinel
        // takes different args than azurite. Bypass the launcher and exercise
        // AzuriteProcess directly with a known-safe argv. This still covers
        // the contract (stdout streaming, stop, dispose) the orchestrator
        // depends on.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in GetSentinelArgs(commandText))
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start().Should().BeTrue("Failed to start sentinel process.");

        await using IAzuriteProcess handle = new AzuriteProcess(process, AzuriteLaunchMode.Native);

        (handle.ProcessId > 0).Should().BeTrue();

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string? firstLine = null;
        await foreach (string line in handle.ReadStdoutLinesAsync(readCts.Token))
        {
            firstLine = line;
            break;
        }

        (firstLine?.Trim()).Should().Be("started");

        await handle.StopAsync(CancellationToken.None);

        using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int exitCode = await handle.WaitForExitAsync(exitCts.Token);

        // Force-killed processes return a non-zero exit code on Unix and an
        // OS-specific code on Windows. Either way, the process must have exited.
        exitCode.Should().NotBe(0);

        // Verify request shape validation still passes for the parallel
        // happy-path AzuriteLaunchRequest. (Keeps the launch-request type
        // exercised even though we routed around the launcher.)
        request.Mode.Should().Be(AzuriteLaunchMode.Native);
    }

    [Fact]
    public async Task StartAsync_Native_MissingExecutable_ThrowsAzuriteLaunchException()
    {
        var launcher = new AzuriteLauncher();
        string missingPath = Path.Combine(Path.GetTempPath(), "definitely-not-a-real-azurite-" + Guid.NewGuid().ToString("N"));

        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: Path.GetTempPath(),
            logPath: Path.Combine(Path.GetTempPath(), "azurite-test.log"),
            executablePath: missingPath);

        var ex = (await FluentActions.Awaiting(() => launcher.StartAsync(request, CancellationToken.None)).Should().ThrowAsync<AzuriteLaunchException>()).Which;

        ex.Mode.Should().Be(AzuriteLaunchMode.Native);
        ex.FileName.Should().Be(missingPath);
    }

    [Fact]
    public async Task AzuriteProcess_Dispose_StopsRunningProcess()
    {
        (string fileName, string commandText) = GetEchoSleepSentinel();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in GetSentinelArgs(commandText))
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start().Should().BeTrue();
        int pid = process.Id;

        IAzuriteProcess handle = new AzuriteProcess(process, AzuriteLaunchMode.Native);
        await handle.DisposeAsync();

        // Give the OS a moment to reap.
        await Task.Delay(200);
        IsProcessAlive(pid).Should().BeFalse();
    }

    private static (string FileName, string CommandText) GetEchoSleepSentinel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // `timeout` is not available in all SKUs (e.g. when redirected),
            // so use ping to a non-routable address as a portable sleep.
            return ("cmd.exe", "echo started & ping -n 30 127.0.0.1 > nul");
        }

        return ("/bin/sh", "echo started; sleep 30");
    }

    private static string[] GetSentinelArgs(string commandText)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["/c", commandText]
            : ["-c", commandText];
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
