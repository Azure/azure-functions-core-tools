using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SampleTestingUpdate
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
            internal static extern SafeWaitHandle CreateJobObjectW(IntPtr lpJobAttributes, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoClass jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern IntPtr GetCommandLine();
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
