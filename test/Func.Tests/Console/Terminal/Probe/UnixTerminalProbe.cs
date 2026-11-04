// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if TERMINAL_NATIVE_PROBE
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Console.Terminal;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.TerminalProbe;

internal sealed partial class UnixTerminalProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This probe requires a Unix PTY.");
        }

        if (args.Contains("--signal-child", StringComparer.Ordinal))
        {
            return await RunSignalChildAsync();
        }

        if (!int.TryParse(Environment.GetEnvironmentVariable("FUNC_TERMINAL_PROBE_SESSION"), out int session)
            || GetSession(0) != session || GetProcessGroup() != Environment.ProcessId
            || GetForegroundGroup(0) != GetProcessGroup())
        {
            System.Console.Error.WriteLine("Use run_unix_probe.py to provide a test-owned foreground process group.");
            return 12;
        }

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        int signals = 0;
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            Interlocked.Increment(ref signals);
        });

        using Process child = StartSignalChild();
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(deadline.Token);
            if (ready != "SIGNAL_CHILD_READY" || GetGroup(child.Id) != GetProcessGroup())
            {
                throw new InvalidOperationException("The signal sentinel did not join the probe's foreground group.");
            }

            System.Console.WriteLine($"RUNTIME {RuntimeInformation.ProcessArchitecture} {Environment.Version}");
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            });
            UnixTerminalNative native = new(OperatingSystem.IsMacOS());
            byte[] original = native.GetAttributes();
            bool wheel = false;
            bool down = false;
            bool move = false;
            bool up = false;
            bool controlC = false;
            bool unicode = false;
            bool paste = false;
            bool resized = false;
            bool resumed = false;
            bool readyReported = false;
            byte[] active;
            TerminalSessionFactory factory = new(new UnixPlatform());
            await using (ITerminalSession terminal = factory.Open(console))
            {
                using var suspensionObserver = PosixSignalRegistration.Create(PosixSignal.SIGTSTP, _ =>
                    System.Console.Error.WriteLine("TSTP_CALLBACK_RECEIVED"));
                active = native.GetAttributes();
                TerminalSize size = terminal.Size;
                terminal.Render(new Text(string.Join("\n",
                    Enumerable.Range(0, size.Height).Select(row => new string((char)('A' + row), size.Width)))));
                terminal.WriteControl("\r\nINPUT_READY\r\n");
                await foreach (TerminalInputEvent input in terminal.ReadAsync(deadline.Token))
                {
                    wheel |= input.Kind == TerminalInputKind.Wheel && input.Delta == 3 && input.X == 4 && input.Y == 2;
                    down |= input.Kind == TerminalInputKind.PointerDown && input.X == 4 && input.Y == 2;
                    move |= input.Kind == TerminalInputKind.PointerMove && input.X == 5 && input.Y == 3;
                    up |= input.Kind == TerminalInputKind.PointerUp && input.X == 5 && input.Y == 3;
                    controlC |= input.Kind == TerminalInputKind.Key && input.Key.KeyChar == '\u0003';
                    unicode |= input.Kind == TerminalInputKind.Text && input.Text == "😀";
                    paste |= input.Kind == TerminalInputKind.Paste && input.Text == "cq\u0003";
                    if (!readyReported && wheel && down && move && up && controlC && unicode && paste)
                    {
                        readyReported = true;
                        terminal.WriteControl("\r\nEVENTS_READY\r\n");
                    }

                    if (input.Kind == TerminalInputKind.Resize && terminal.Size == new TerminalSize(96, 14))
                    {
                        resized = true;
                        terminal.WriteControl("\r\nRESIZE_READY\r\n");
                    }

                    if (input.Kind == TerminalInputKind.Key && input.Key.KeyChar == 'c')
                    {
                        resumed = native.GetAttributes().SequenceEqual(active);
                        terminal.WriteControl($"\r\nRESUME_READY:{resumed}\r\n");
                    }

                    if (input.Kind == TerminalInputKind.Key && input.Key.KeyChar == 'q')
                    {
                        break;
                    }
                }
            }

            bool restored = native.GetAttributes().SequenceEqual(original);
            await child.StandardInput.WriteLineAsync("report".AsMemory(), deadline.Token);
            await child.StandardInput.FlushAsync(deadline.Token);
            bool childSafe = await child.StandardOutput.ReadLineAsync(deadline.Token) == "SIGNALS:0";
            bool parentSafe = Volatile.Read(ref signals) == 0;
            System.Console.WriteLine($"RESULT wheel={wheel} down={down} move={move} up={up} ctrlc={controlC}"
                + $" unicode={unicode} paste={paste} resized={resized} resumed={resumed}"
                + $" restored={restored} parentSafe={parentSafe} childSafe={childSafe}");
            return wheel && down && move && up && controlC && unicode && paste && resized
                && resumed && restored && parentSafe && childSafe ? 0 : 11;
        }
        finally
        {
            child.StandardInput.Close();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }

    private static Process StartSignalChild()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Probe executable path is unavailable.");
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.Ordinal))
        {
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        start.ArgumentList.Add("--signal-child");
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the signal sentinel.");
    }

    private static async Task<int> RunSignalChildAsync()
    {
        int signals = 0;
        using var registration = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            Interlocked.Increment(ref signals);
        });
        System.Console.WriteLine("SIGNAL_CHILD_READY");
        string? command;
        while ((command = await System.Console.In.ReadLineAsync()) is not null)
        {
            if (command == "report")
            {
                System.Console.WriteLine($"SIGNALS:{Volatile.Read(ref signals)}");
            }
        }

        return 0;
    }

    [LibraryImport("libc", EntryPoint = "getsid")]
    private static partial int GetSession(int pid);

    [LibraryImport("libc", EntryPoint = "getpgrp")]
    private static partial int GetProcessGroup();

    [LibraryImport("libc", EntryPoint = "getpgid")]
    private static partial int GetGroup(int pid);

    [LibraryImport("libc", EntryPoint = "tcgetpgrp")]
    private static partial int GetForegroundGroup(int descriptor);

    private sealed class UnixPlatform : IPlatform
    {
        public bool IsWindows => false;
        public bool IsMacOS => OperatingSystem.IsMacOS();
    }
}
#endif
