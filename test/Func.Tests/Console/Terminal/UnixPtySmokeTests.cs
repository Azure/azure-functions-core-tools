// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common.Processes;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class UnixPtySmokeTests
{
    [UnixPtyFact]
    public async Task PrivatePty_QualifiesInputSignalsResizeSuspensionAndRestoration()
    {
        string root = FindCheckout();
        string folder = Path.Combine(root, "test", "Func.Tests", "Console", "Terminal", "Probe");
        string? configuredProbe = Environment.GetEnvironmentVariable("FUNC_TERMINAL_PROBE_PATH");
        string probe = configuredProbe ?? Path.Combine(root, "out", "bin", "Terminal.Native.Probe", "release",
            "Azure.Functions.Cli.Tests.TerminalProbe.dll");
        ProcessRunner runner = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(120));
        if (configuredProbe is null)
        {
            ProcessOutcome build = await runner.RunAsync(new ProcessRunRequest("dotnet",
                ["build", Path.Combine(folder, "Terminal.Native.Probe.csproj"), "-c", "Release",
                    "--verbosity", "quiet", $"-p:SpectreAssemblyPath={typeof(IAnsiConsole).Assembly.Location}"],
                root, TimeSpan.FromSeconds(60)), deadline.Token);
            build.ExitCode.Should().Be(0, build.StandardOutput + build.StandardError);
        }

        Path.IsPathFullyQualified(probe).Should().BeTrue("the native probe must be an explicit absolute target");
        File.Exists(probe).Should().BeTrue();
        ProcessOutcome run = await runner.RunAsync(new ProcessRunRequest("python3",
            [Path.Combine(folder, "run_unix_probe.py"), probe], root, TimeSpan.FromSeconds(70)), deadline.Token);
        run.ExecutableNotFound.Should().BeFalse("opt-in Unix PTY qualification requires Python 3");
        run.TimedOut.Should().BeFalse(run.StandardOutput + run.StandardError);
        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);
        run.StandardOutput.Should().Contain("\"status\": \"passed\"")
            .And.Contain("\"terminal_restored\": true")
            .And.Contain("parentSafe=True childSafe=True");
    }

    private static string FindCheckout()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test", "Func.Tests", "Console", "Terminal",
                "Probe", "run_unix_probe.py")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unix PTY qualification requires the built tests to remain in their source checkout.");
    }

    private sealed class UnixPtyFactAttribute : FactAttribute
    {
        public UnixPtyFactAttribute()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                Skip = "Unix PTY qualification runs on Linux or macOS.";
            }
            else if (Environment.GetEnvironmentVariable("FUNC_TERMINAL_NATIVE_TESTS") != "1")
            {
                Skip = "Set FUNC_TERMINAL_NATIVE_TESTS=1 to run the private Unix PTY qualification.";
            }
        }
    }
}
