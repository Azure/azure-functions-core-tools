// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed partial class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeFileHandle _jobHandle;

    private WindowsProcessJob(Process process)
    {
        SafeFileHandle jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
        {
            throw CreateWin32Exception("Failed to create a host process job object.");
        }

        try
        {
            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };

            if (!SetInformationJobObject(
                jobHandle,
                JobObjectInfoType.ExtendedLimitInformation,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                throw CreateWin32Exception("Failed to configure the host process job object.");
            }

            if (!AssignProcessToJobObject(jobHandle, process.Handle))
            {
                throw CreateWin32Exception("Failed to assign the host process to a job object.");
            }
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }

        _jobHandle = jobHandle;
    }

    public static WindowsProcessJob? TryAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return new WindowsProcessJob(process);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
        => _jobHandle.Dispose();

    private static Win32Exception CreateWin32Exception(string message)
        => new(Marshal.GetLastPInvokeError(), message);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeFileHandle hJob,
        JobObjectInfoType jobObjectInfoClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
