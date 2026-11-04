// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Console.Terminal;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public sealed partial class ConPtySmokeTests
{
    [ConPtyFact]
    public async Task IsolatedConsole_RawMouseControlCUnicodeBottomRowAndRestoration()
    {
        string root = FindCheckout();
        string project = Path.Combine(root, "test", "Func.Tests", "Console", "Terminal", "Probe", "Terminal.Native.Probe.csproj");
        string assembly = Path.Combine(root, "out", "bin", "Terminal.Native.Probe", "release", "Azure.Functions.Cli.Tests.TerminalProbe.dll");
        ProcessRunner runner = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        ProcessOutcome build = await runner.RunAsync(new ProcessRunRequest(
            "dotnet", ["build", project, "-c", "Release", "--nologo", "--verbosity", "quiet",
                $"-p:SpectreAssemblyPath={typeof(IAnsiConsole).Assembly.Location}"],
            root, TimeSpan.FromSeconds(40)), deadline.Token);
        build.ExitCode.Should().Be(0, build.StandardOutput + build.StandardError);

        await using ConPtyProcess process = new($"dotnet \"{assembly}\"", root);
        await process.WaitForOutputAsync("HANDOFF_READY", deadline.Token);
        await process.SendAsync("ab", deadline.Token);
        await process.WaitForOutputAsync("INPUT_READY", deadline.Token);
        await process.SendAsync(
            "\u001b[<64;5;3M\u001b[<0;5;3M\u001b[<32;6;4M\u001b[<0;6;4m😀\u0003\u001b[200~cq\u0003\u001b[201~q",
            deadline.Token);
        int exitCode = await process.WaitForExitAsync(deadline.Token);
        await process.WaitForOutputAsync("RESULT", deadline.Token);
        exitCode.Should().Be(0, process.Output);
        process.Output.Should().Contain(
            "RESULT handoff=True frame=True wheel=True down=True move=True up=True ctrlc=True unicode=True paste=True restored=True");
    }

    private static string FindCheckout()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test", "Func.Tests", "Console", "Terminal", "Probe", "Terminal.Native.Probe.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Native terminal qualification requires the test output to remain within its source checkout.");
    }

    private sealed class ConPtyFactAttribute : FactAttribute
    {
        public ConPtyFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "ConPTY qualification runs on Windows only.";
            }
            else if (Environment.GetEnvironmentVariable("FUNC_TERMINAL_NATIVE_TESTS") != "1")
            {
                Skip = "Set FUNC_TERMINAL_NATIVE_TESTS=1 to run the isolated native terminal qualification.";
            }
        }
    }

    private sealed partial class ConPtyProcess : IAsyncDisposable
    {
        private readonly object _outputGate = new();
        private readonly StringBuilder _output = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly FileStream _input;
        private readonly FileStream _reader;
        private readonly SafeProcessHandle _process;
        private readonly nint _pseudoConsole;
        private readonly Task _capture;

        public ConPtyProcess(string command, string directory)
        {
            SafeFileHandle? inputRead = null;
            SafeFileHandle? inputWrite = null;
            SafeFileHandle? outputRead = null;
            SafeFileHandle? outputWrite = null;
            nint pseudoConsole = 0;
            nint attributes = 0;
            bool attributesInitialized = false;
            try
            {
                Check(CreatePipe(out inputRead, out inputWrite, 0, 0));
                Check(CreatePipe(out outputRead, out outputWrite, 0, 0));
                int result = CreatePseudoConsole(32U | (8U << 16), inputRead, outputWrite, 0, out pseudoConsole);
                Marshal.ThrowExceptionForHR(result);

                nuint attributeBytes = 0;
                InitializeProcThreadAttributeList(0, 1, 0, ref attributeBytes);
                attributes = Marshal.AllocHGlobal(checked((nint)attributeBytes));
                Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeBytes));
                attributesInitialized = true;
                Check(UpdateProcThreadAttribute(attributes, 0, 0x00020016, pseudoConsole, (nuint)nint.Size, 0, 0));

                StartupInfoEx startup = new()
                {
                    StartupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() },
                    AttributeList = attributes,
                };
                Marshal.SizeOf<StartupInfoEx>().Should().Be(112);
                Marshal.OffsetOf<StartupInfoEx>(nameof(StartupInfoEx.AttributeList)).ToInt64().Should().Be(104);
                ProcessInformation information = Start(command, directory, ref startup);
                CloseHandle(information.Thread);
                _process = new SafeProcessHandle(information.Process, ownsHandle: true);
                _pseudoConsole = pseudoConsole;
                _input = new FileStream(inputWrite, FileAccess.Write);
                _reader = new FileStream(outputRead, FileAccess.Read);
                _capture = CaptureAsync(_stop.Token);
            }
            catch
            {
                inputWrite?.Dispose();
                outputRead?.Dispose();
                if (pseudoConsole != 0)
                {
                    ClosePseudoConsole(pseudoConsole);
                }

                throw;
            }
            finally
            {
                inputRead?.Dispose();
                outputWrite?.Dispose();
                if (attributesInitialized)
                {
                    DeleteProcThreadAttributeList(attributes);
                }

                if (attributes != 0)
                {
                    Marshal.FreeHGlobal(attributes);
                }
            }
        }

        public string Output
        {
            get
            {
                lock (_outputGate)
                {
                    return _output.ToString();
                }
            }
        }

        public async Task SendAsync(string text, CancellationToken cancellationToken)
        {
            await _input.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            await _input.FlushAsync(cancellationToken);
        }

        public async Task WaitForOutputAsync(string marker, CancellationToken cancellationToken)
        {
            while (!Output.Contains(marker, StringComparison.Ordinal))
            {
                if (_capture.IsCompleted || WaitForSingleObject(_process, 0) != 0x00000102)
                {
                    if (_capture.IsCompleted)
                    {
                        await _capture;
                    }

                    throw new IOException($"The probe closed before '{marker}'. {Output}");
                }

                await Task.Delay(10, cancellationToken);
            }
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            while (WaitForSingleObject(_process, 0) == 0x00000102)
            {
                await Task.Delay(10, cancellationToken);
            }

            Check(GetExitCodeProcess(_process, out uint exitCode));
            return (int)exitCode;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // This handle is returned by CreateProcessW; no process-name or shared-console operations are used.
                if (WaitForSingleObject(_process, 0) == 0x00000102)
                {
                    TerminateProcess(_process, 1);
                }

                _input.Dispose();
                ClosePseudoConsole(_pseudoConsole);
            }
            finally
            {
                _stop.Cancel();
                _reader.Dispose();
                try
                {
                    await _capture.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException)
                {
                    // Closing the isolated pseudo console is expected to interrupt its private output pipe.
                }

                _process.Dispose();
                _stop.Dispose();
            }
        }

        private async Task CaptureAsync(CancellationToken cancellationToken)
        {
            using StreamReader reader = new(_reader, Encoding.UTF8, leaveOpen: true);
            char[] buffer = new char[1024];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                lock (_outputGate)
                {
                    int remaining = 65_536 - _output.Length;
                    if (remaining > 0)
                    {
                        _output.Append(buffer, 0, Math.Min(count, remaining));
                    }
                }
            }
        }

        private static unsafe ProcessInformation Start(string command, string directory, ref StartupInfoEx startup)
        {
            char[] buffer = [.. command, '\0'];
            fixed (char* pointer = buffer)
            {
                Check(CreateProcess(0, pointer, 0, 0, false, 0x00080000, 0, directory, ref startup, out ProcessInformation information));
                return information;
            }
        }

        private static void Check(bool success)
        {
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint security, uint size);

        [LibraryImport("kernel32.dll")]
        private static partial int CreatePseudoConsole(uint size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

        [LibraryImport("kernel32.dll")]
        private static partial void ClosePseudoConsole(nint console);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool InitializeProcThreadAttributeList(nint attributes, int count, uint flags, ref nuint bytes);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UpdateProcThreadAttribute(
            nint attributes, uint flags, nuint attribute, nint value, nuint bytes, nint previous, nint returnedBytes);

        [LibraryImport("kernel32.dll")]
        private static partial void DeleteProcThreadAttributeList(nint attributes);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool CreateProcess(
            nint application, char* command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("kernel32.dll")]
        private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint Size;
            public nint Reserved;
            public nint Desktop;
            public nint Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort ReservedSize;
            public nint ReservedPointer;
            public nint StandardInput;
            public nint StandardOutput;
            public nint StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public nint AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public nint Process;
            public nint Thread;
            public uint ProcessId;
            public uint ThreadId;
        }
    }
}
