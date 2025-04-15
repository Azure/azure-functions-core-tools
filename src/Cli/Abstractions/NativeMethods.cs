// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/NativeMethods.cs

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Abstractions
{
    internal static class NativeMethods
    {
        internal static class Windows
        {
            internal enum JobObjectInfoClass : uint
            {
                JobObjectExtendedLimitInformation = 9,
            }

            [Flags]
            internal enum JobObjectLimitFlags : uint
            {
                JobObjectLimitKillOnJobClose = 0x2000,
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JobObjectBasicLimitInformation
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public JobObjectLimitFlags LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct IoCounters
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JobObjectExtendedLimitInformation
            {
                public JobObjectBasicLimitInformation BasicLimitInformation;
                public IoCounters IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            internal const int ProcessBasicInformation = 0;

            [StructLayout(LayoutKind.Sequential)]
            internal struct PROCESS_BASIC_INFORMATION
            {
                public uint ExitStatus;
                public IntPtr PebBaseAddress;
                public UIntPtr AffinityMask;
                public int BasePriority;
                public UIntPtr UniqueProcessId;
                public UIntPtr InheritedFromUniqueProcessId;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeWaitHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoClass jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern IntPtr GetCommandLine();

            // NOTE: this used to be unsafe but I am usign safe handle based alternative here to make this compile
            [DllImport("ntdll.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern uint NtQueryInformationProcess(
                SafeProcessHandle ProcessHandle,
                int ProcessInformationClass,
                [In, Out] byte[] ProcessInformation,  // Use a managed byte array instead
                uint ProcessInformationLength,
                out uint ReturnLength);
        }

        internal static class Posix
        {
            [DllImport("libc", SetLastError = true)]
            internal static extern int kill(int pid, int sig);

            internal const int SIGINT = 2;
            internal const int SIGTERM = 15;
        }
    }
}
