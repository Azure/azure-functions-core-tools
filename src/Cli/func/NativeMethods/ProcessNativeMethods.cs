﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.NativeMethods
{
    internal class ProcessNativeMethods
    {
        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
                IntPtr processHandle,
                int processInformationClass,
                ref ProcessInformation processInformation,
                int processInformationLength,
                out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            // These members must match PROCESS_BASIC_INFORMATION
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }
    }
}
