#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Performance.Startup;

// PowerShell invokes this bridge after compiling it with the shared native source.
public sealed class StartupGroupCompletionChecks
{
    public void Run(string scenario)
    {
        if (scenario is not ("delayed-exit" or "timeout" or "inspection-error" or "native-inspection"))
        {
            throw new ArgumentException("Unknown group completion scenario.", nameof(scenario));
        }
        using var process = new ControlledProcess(scenario);
        using ManualResetEventSlim rechecked = process.Rechecked;
        using ManualResetEventSlim release = process.Release;
        process.Start();
        if (!process.WaitForExit(10000)) { throw new TimeoutException("Test leader did not exit."); }
        int leader = process.Id;
        if (scenario == "native-inspection" && OperatingSystem.IsLinux() &&
            File.ReadAllText($"/proc/{leader}/comm").TrimEnd('\n') != "worker ) ( name")
        {
            throw new InvalidOperationException("The Linux process-name parsing boundary was not established.");
        }
        Task cleanup = Task.Run(process.Dispose);
        try
        {
            if (scenario == "delayed-exit")
            {
                if (!process.Rechecked.Wait(10000))
                {
                    cleanup.GetAwaiter().GetResult();
                    throw new InvalidOperationException("Cleanup returned without waiting for live group members.");
                }
                if (cleanup.IsCompleted) { throw new InvalidOperationException("Cleanup completed before group exit was released."); }
                process.Release.Set();
                cleanup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                if (process.Checks < 2) { throw new InvalidOperationException("Live membership was not rechecked."); }
            }
            else if (scenario == "native-inspection")
            {
                cleanup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }
            else
            {
                Exception? failure = null;
                try { cleanup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
                catch (Exception error) when (error is TimeoutException or UnauthorizedAccessException) { failure = error; }
                if (scenario == "timeout" && (failure is not TimeoutException ||
                    !failure.Message.Contains("Owned process group cleanup", StringComparison.Ordinal) || process.Checks == 0))
                {
                    throw new InvalidOperationException("Live group cleanup did not fail with a bounded timeout.");
                }
                if (scenario == "inspection-error" && failure is not UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Group inspection failure was hidden.");
                }
            }
            if (waitpid(leader, out _, 1) != -1 || Marshal.GetLastPInvokeError() != 10)
            {
                throw new InvalidOperationException("Disposal did not reap the retained leader.");
            }
        }
        finally
        {
            process.Release.Set();
            try
            {
                if (!cleanup.IsCompleted) { cleanup.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            }
            finally { process.RestoreNativeInspection = true; }
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    private sealed class ControlledProcess(string scenario)
        : UnixStartupProcess("/bin/sh", ["-c", scenario == "native-inspection" && OperatingSystem.IsLinux()
            ? "printf 'worker ) ( name\\n' > /proc/self/comm; exit 0" : "exit 0"], Path.GetTempPath(), new Dictionary<string, string>())
    {
        internal readonly ManualResetEventSlim Rechecked = new();
        internal readonly ManualResetEventSlim Release = new();
        internal bool RestoreNativeInspection;
        internal int Checks;

        internal override int CleanupTimeoutMilliseconds => scenario == "delayed-exit" ? 10000 : 100;

        internal override bool GroupHasOnlyExitedMembers()
        {
            if (RestoreNativeInspection || scenario == "native-inspection") { return base.GroupHasOnlyExitedMembers(); }
            int count = Interlocked.Increment(ref Checks);
            if (scenario == "inspection-error") { throw new UnauthorizedAccessException("Expected group inspection failure."); }
            if (scenario == "timeout") { return false; }
            if (count == 1) { return false; }
            Rechecked.Set();
            if (!Release.Wait(10000)) { throw new TimeoutException("Test did not release group exit."); }
            return true;
        }
    }
}
