#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Performance.Startup;

internal abstract class NativeStartupProcess : IDisposable
{
    internal abstract int Id { get; }

    internal abstract int ExitCode { get; }

    internal abstract StreamReader Output { get; }

    internal abstract StreamReader Error { get; }

    internal abstract void Start();

    internal abstract bool WaitForExit(int milliseconds);

    internal abstract void Terminate();

    public abstract void Dispose();

    protected static StreamReader OpenReader(SafeFileHandle handle)
    {
        try { return new StreamReader(new FileStream(handle, FileAccess.Read), Encoding.UTF8); }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}

internal sealed class WindowsStartupProcess : NativeStartupProcess
{
    private readonly SafeFileHandle _job;
    private readonly SafeFileHandle _input;
    private readonly SafeFileHandle _outputWrite;
    private readonly SafeFileHandle _errorWrite;
    private readonly StreamReader _output;
    private readonly StreamReader _error;
    private readonly string _executable;
    private readonly string _directory;
    private readonly StringBuilder _command;
    private readonly IntPtr _environment;
    private readonly IntPtr _attributes;
    private readonly IntPtr _handles;
    private readonly IntPtr _jobList;
    private readonly bool _attributesInitialized;
    private SafeWaitHandle? _process;
    private int _id;
    private bool _disposed;

    internal WindowsStartupProcess(string executable, string[] arguments, string directory, IDictionary<string, string> environment)
    {
        if (IntPtr.Size != 8) { throw new PlatformNotSupportedException("Startup containment requires a 64-bit process."); }
        _executable = executable;
        _directory = directory;
        _command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
        _job = Native.CreateJobObject(IntPtr.Zero, null);
        _input = new SafeFileHandle(IntPtr.Zero, true);
        _outputWrite = new SafeFileHandle(IntPtr.Zero, true);
        _errorWrite = new SafeFileHandle(IntPtr.Zero, true);
        _output = StreamReader.Null;
        _error = StreamReader.Null;
        try
        {
            if (_job.IsInvalid) { throw Failure("create startup job"); }
            // The job is mandatory, and CreateProcess assigns it before the target executes.
            var limits = new Native.JobLimits
            {
                Basic = new Native.JobBasicLimits { Flags = 0x2000 },
            };
            if (!Native.SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<Native.JobLimits>()))
            {
                throw Failure("configure startup job");
            }
            var security = new Native.SecurityAttributes { Length = Marshal.SizeOf<Native.SecurityAttributes>(), Inherit = true };
            if (!Native.CreatePipe(out SafeFileHandle inputRead, out SafeFileHandle inputWrite, ref security, 0))
            {
                throw Failure("create stdin pipe");
            }
            _input = inputRead;
            inputWrite.Dispose();
            if (!Native.CreatePipe(out SafeFileHandle outputRead, out SafeFileHandle outputWrite, ref security, 0))
            {
                throw Failure("create stdout pipe");
            }
            _outputWrite = outputWrite;
            _output = OpenReader(outputRead);
            if (!Native.SetHandleInformation(outputRead, 1, 0)) { throw Failure("protect stdout handle"); }
            if (!Native.CreatePipe(out SafeFileHandle errorRead, out SafeFileHandle errorWrite, ref security, 0))
            {
                throw Failure("create stderr pipe");
            }
            _errorWrite = errorWrite;
            _error = OpenReader(errorRead);
            if (!Native.SetHandleInformation(errorRead, 1, 0)) { throw Failure("protect stderr handle"); }
            nuint size = 0;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            _attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!Native.InitializeProcThreadAttributeList(_attributes, 2, 0, ref size)) { throw Failure("initialize launch attributes"); }
            _attributesInitialized = true;
            _handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(_handles, 0, _input.DangerousGetHandle());
            Marshal.WriteIntPtr(_handles, IntPtr.Size, _outputWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(_handles, IntPtr.Size * 2, _errorWrite.DangerousGetHandle());
            _jobList = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_jobList, _job.DangerousGetHandle());
            if (!Native.UpdateProcThreadAttribute(_attributes, 0, 0x20002, _handles, (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero) ||
                !Native.UpdateProcThreadAttribute(_attributes, 0, 0x2000D, _jobList, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw Failure("set contained launch attributes");
            }
            string block = string.Join("\0", environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
            _environment = Marshal.StringToHGlobalUni(block);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal override int Id => _id;

    internal override int ExitCode
    {
        get
        {
            if (_process is null || !Native.GetExitCodeProcess(_process, out uint code)) { throw Failure("read target exit code"); }
            return unchecked((int)code);
        }
    }

    internal override StreamReader Output => _output;

    internal override StreamReader Error => _error;

    internal override void Start()
    {
        var startup = new Native.StartupInfoEx
        {
            Info = new Native.StartupInfo
            {
                Size = Marshal.SizeOf<Native.StartupInfoEx>(),
                Flags = 0x100,
                Input = _input.DangerousGetHandle(),
                Output = _outputWrite.DangerousGetHandle(),
                Error = _errorWrite.DangerousGetHandle(),
            },
            Attributes = _attributes,
        };
        if (!Native.CreateProcess(_executable, _command, IntPtr.Zero, IntPtr.Zero, true,
            0x08080400, _environment, _directory, ref startup, out Native.ProcessInformation info))
        {
            int error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, $"An error occurred trying to start process '{_executable}': {new Win32Exception(error).Message}");
        }
        _process = new SafeWaitHandle(info.Process, true);
        using var thread = new SafeWaitHandle(info.Thread, true);
        _id = info.ProcessId;
        _input.Dispose();
        _outputWrite.Dispose();
        _errorWrite.Dispose();
    }

    internal override bool WaitForExit(int milliseconds)
    {
        uint result = Native.WaitForSingleObject(_process ?? throw new InvalidOperationException("Target has not started."), (uint)milliseconds);
        if (result == 0) { return true; }
        if (result == 258) { return false; }
        throw Failure("wait for target");
    }

    internal override void Terminate()
    {
        if (_process is null) { return; }
        if (!Native.TerminateJobObject(_job, 1)) { throw Failure("terminate startup job"); }
        var guard = Stopwatch.StartNew();
        while (true)
        {
            if (!Native.QueryInformationJobObject(_job, 1, out Native.JobAccounting accounting,
                (uint)Marshal.SizeOf<Native.JobAccounting>(), IntPtr.Zero))
            {
                throw Failure("verify startup job cleanup");
            }
            if (accounting.ActiveProcesses == 0) { break; }
            if (guard.ElapsedMilliseconds >= 10000) { throw new TimeoutException("Owned process cleanup exceeded 10 seconds."); }
            Thread.Sleep(1);
        }
        if (!WaitForExit(10000)) { throw new TimeoutException("Owned target cleanup exceeded 10 seconds."); }
    }

    public override void Dispose()
    {
        if (_disposed) { return; }
        try { Terminate(); }
        finally
        {
            _disposed = true;
            _job.Dispose();
            _process?.Dispose();
            _input.Dispose();
            _outputWrite.Dispose();
            _errorWrite.Dispose();
            _output.Dispose();
            _error.Dispose();
            if (_attributesInitialized) { Native.DeleteProcThreadAttributeList(_attributes); }
            Marshal.FreeHGlobal(_attributes);
            Marshal.FreeHGlobal(_handles);
            Marshal.FreeHGlobal(_jobList);
            Marshal.FreeHGlobal(_environment);
        }
    }

    private static Win32Exception Failure(string action)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"Unable to {action}: {new Win32Exception(error).Message}");
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    // DllImport is also compiled by PowerShell Add-Type, without source generators.
    private sealed class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupInfo
        {
            internal int Size;
            internal IntPtr Reserved, Desktop, Title;
            internal uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
            internal ushort Show, ReservedSize;
            internal IntPtr ReservedBytes, Input, Output, Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupInfoEx { internal StartupInfo Info; internal IntPtr Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation { internal IntPtr Process, Thread; internal int ProcessId, ThreadId; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobBasicLimits
        {
            internal long ProcessTime, JobTime;
            internal uint Flags;
            internal UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            internal uint ActiveProcessLimit;
            internal UIntPtr Affinity;
            internal uint Priority, Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobLimits
        {
            internal JobBasicLimits Basic;
            internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobAccounting
        {
            internal long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
            internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref JobLimits information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeFileHandle job, int type, out JobAccounting information, uint length, IntPtr returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);

        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
            [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup,
            out ProcessInformation information);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeWaitHandle process, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeWaitHandle process, out uint code);
    }
}

internal sealed class UnixStartupProcess : NativeStartupProcess
{
    private readonly string _executable;
    private readonly IntPtr _arguments;
    private readonly IntPtr _environment;
    // Darwin uses opaque pointers; the supported 64-bit glibc ABI uses inline spawn structures.
    private readonly IntPtr _actions;
    private readonly IntPtr _attributes;
    private readonly List<IntPtr> _strings = [];
    private readonly StreamReader _output = StreamReader.Null;
    private readonly StreamReader _error = StreamReader.Null;
    private readonly SafeFileHandle? _input;
    private readonly SafeFileHandle? _outputWrite;
    private readonly SafeFileHandle? _errorWrite;
    private readonly bool _actionsInitialized;
    private readonly bool _attributesInitialized;
    private bool _disposed;
    private int _id;
    private int _exitCode;
    private Task? _exit;

    internal UnixStartupProcess(string executable, string[] arguments, string directory, IDictionary<string, string> environment)
    {
        _executable = executable;
        try
        {
            if (IntPtr.Size != 8) { throw new PlatformNotSupportedException("Startup containment requires a 64-bit process."); }
            _actions = Marshal.AllocHGlobal(OperatingSystem.IsMacOS() ? IntPtr.Size : 80);
            _attributes = Marshal.AllocHGlobal(OperatingSystem.IsMacOS() ? IntPtr.Size : 336);
            Check(Native.posix_spawn_file_actions_init(_actions), "initialize spawn actions");
            _actionsInitialized = true;
            Check(Native.posix_spawnattr_init(_attributes), "initialize spawn attributes");
            _attributesInitialized = true;
            Check(Native.posix_spawnattr_setpgroup(_attributes, 0), "set target process group");
            IntPtr mask = Marshal.AllocHGlobal(128);
            try
            {
                if (Native.sigemptyset(mask) < 0) { throw Failure("initialize target signal mask"); }
                Check(Native.posix_spawnattr_setsigmask(_attributes, mask), "clear target signal mask");
            }
            finally { Marshal.FreeHGlobal(mask); }
            Check(Native.posix_spawnattr_setflags(_attributes, 2 | 8), "enable spawn process group and signal mask");
            Check(Native.posix_spawn_file_actions_addchdir_np(_actions, directory), "set target directory");
            int input = Native.open("/dev/null", 0);
            if (input < 0) { throw Failure("open target input"); }
            _input = new SafeFileHandle((IntPtr)input, true);
            AddDescriptor(input, 0);
            _output = CreatePipe(1, out _outputWrite);
            _error = CreatePipe(2, out _errorWrite);
            _arguments = Vector(new[] { executable }.Concat(arguments));
            _environment = Vector(environment.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal override int Id => _id;

    internal override int ExitCode => _exitCode;

    internal override StreamReader Output => _output;

    internal override StreamReader Error => _error;

    internal override void Start()
    {
        int error = Native.posix_spawnp(out int id, _executable, _actions, _attributes, _arguments, _environment);
        if (error != 0)
        {
            throw new Win32Exception(error, $"An error occurred trying to start process '{_executable}': {new Win32Exception(error).Message}");
        }
        _id = id;
        _input?.Dispose();
        _outputWrite?.Dispose();
        _errorWrite?.Dispose();
        // Keep the exited leader unreaped until group cleanup, so its PGID cannot be recycled.
        _exit = Task.Factory.StartNew(() =>
        {
            IntPtr information = Marshal.AllocHGlobal(128);
            try
            {
                int result;
                do
                {
                    result = Native.waitid(1, _id, information, 4 | (OperatingSystem.IsMacOS() ? 0x20 : 0x1000000));
                }
                while (result < 0 && Marshal.GetLastPInvokeError() == 4);
                if (result < 0) { throw Failure("wait for target"); }
                int code = Marshal.ReadInt32(information, 8);
                int status = Marshal.ReadInt32(information, OperatingSystem.IsMacOS() ? 20 : 24);
                _exitCode = code == 1 ? status : 128 + status;
            }
            finally { Marshal.FreeHGlobal(information); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal override bool WaitForExit(int milliseconds) => (_exit ?? throw new InvalidOperationException("Target has not started.")).Wait(milliseconds);

    internal override void Terminate()
    {
        if (_id <= 0) { return; }
        if (Native.kill(-_id, 9) < 0 && Marshal.GetLastPInvokeError() != 3) { throw Failure("terminate target process group"); }
        if (!WaitForExit(10000)) { throw new TimeoutException("Owned process cleanup exceeded 10 seconds."); }
    }

    public override void Dispose()
    {
        if (_disposed) { return; }
        try
        {
            Terminate();
            if (_id > 0)
            {
                int result;
                do { result = Native.waitpid(_id, out _, 0); }
                while (result < 0 && Marshal.GetLastPInvokeError() == 4);
                if (result < 0) { throw Failure("reap target"); }
            }
        }
        finally
        {
            _disposed = true;
            _input?.Dispose();
            _outputWrite?.Dispose();
            _errorWrite?.Dispose();
            _output.Dispose();
            _error.Dispose();
            if (_actionsInitialized) { Check(Native.posix_spawn_file_actions_destroy(_actions), "destroy spawn actions"); }
            if (_attributesInitialized) { Check(Native.posix_spawnattr_destroy(_attributes), "destroy spawn attributes"); }
            Marshal.FreeHGlobal(_actions);
            Marshal.FreeHGlobal(_attributes);
            Marshal.FreeHGlobal(_arguments);
            Marshal.FreeHGlobal(_environment);
            foreach (IntPtr value in _strings) { Marshal.FreeCoTaskMem(value); }
        }
    }

    private StreamReader CreatePipe(int target, out SafeFileHandle write)
    {
        if (Native.pipe(out Pipe descriptors) < 0) { throw Failure("create target pipe"); }
        var read = new SafeFileHandle((IntPtr)descriptors.Read, true);
        write = new SafeFileHandle((IntPtr)descriptors.Write, true);
        try
        {
            if (Native.fcntl(descriptors.Read, 2, 1) < 0) { throw Failure("protect pipe reader"); }
            AddDescriptor(descriptors.Write, target);
            Check(Native.posix_spawn_file_actions_addclose(_actions, descriptors.Read), "close child pipe reader");
            return OpenReader(read);
        }
        catch
        {
            read.Dispose();
            write.Dispose();
            throw;
        }
    }

    private void AddDescriptor(int descriptor, int target)
    {
        if (descriptor <= 2) { throw new InvalidOperationException("Native launch requires open standard descriptors."); }
        if (Native.fcntl(descriptor, 2, 1) < 0) { throw Failure("protect target descriptor"); }
        Check(Native.posix_spawn_file_actions_adddup2(_actions, descriptor, target), "redirect target stream");
        Check(Native.posix_spawn_file_actions_addclose(_actions, descriptor), "close child source descriptor");
    }

    private IntPtr Vector(IEnumerable<string> values)
    {
        IntPtr[] strings = [.. values.Select(value =>
        {
            IntPtr pointer = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(pointer);
            return pointer;
        }), IntPtr.Zero];
        IntPtr vector = Marshal.AllocHGlobal(strings.Length * IntPtr.Size);
        Marshal.Copy(strings, 0, vector, strings.Length);
        return vector;
    }

    private static Win32Exception Failure(string action)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"Unable to {action}: {new Win32Exception(error).Message}");
    }

    private static void Check(int error, string action)
    {
        if (error != 0) { throw new Win32Exception(error, $"Unable to {action}: {new Win32Exception(error).Message}"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Pipe { internal int Read, Write; }

    private sealed class Native
    {
        [DllImport("libc", SetLastError = true)]
        internal static extern int pipe(out Pipe descriptors);

        [DllImport("libc", SetLastError = true)]
        internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        internal static extern int fcntl(int descriptor, int command, int flags);

        [DllImport("libc")]
        internal static extern int posix_spawn_file_actions_init(IntPtr actions);

        [DllImport("libc")]
        internal static extern int posix_spawn_file_actions_destroy(IntPtr actions);

        [DllImport("libc")]
        internal static extern int posix_spawn_file_actions_addchdir_np(IntPtr actions, [MarshalAs(UnmanagedType.LPUTF8Str)] string directory);

        [DllImport("libc")]
        internal static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int descriptor, int target);

        [DllImport("libc")]
        internal static extern int posix_spawn_file_actions_addclose(IntPtr actions, int descriptor);

        [DllImport("libc")]
        internal static extern int posix_spawnattr_init(IntPtr attributes);

        [DllImport("libc")]
        internal static extern int posix_spawnattr_destroy(IntPtr attributes);

        [DllImport("libc")]
        internal static extern int posix_spawnattr_setpgroup(IntPtr attributes, int group);

        [DllImport("libc")]
        internal static extern int posix_spawnattr_setflags(IntPtr attributes, short flags);

        [DllImport("libc", SetLastError = true)]
        internal static extern int sigemptyset(IntPtr mask);

        [DllImport("libc")]
        internal static extern int posix_spawnattr_setsigmask(IntPtr attributes, IntPtr mask);

        [DllImport("libc")]
        internal static extern int posix_spawnp(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string executable,
            IntPtr actions, IntPtr attributes, IntPtr arguments, IntPtr environment);

        [DllImport("libc", SetLastError = true)]
        internal static extern int waitid(int type, int pid, IntPtr information, int options);

        [DllImport("libc", SetLastError = true)]
        internal static extern int waitpid(int pid, out int status, int options);

        [DllImport("libc", SetLastError = true)]
        internal static extern int kill(int pid, int signal);
    }
}
