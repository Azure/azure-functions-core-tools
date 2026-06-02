// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Azure.Functions.Cli.Workloads.Host.Interop;

/// <summary>
/// Windows implementation of <see cref="INativeHandleApi"/> backed by the kernel32 handle APIs.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class Win32NativeHandleApi : INativeHandleApi
{
    private const uint HandleFlagInherit = 0x1;

    public bool TryGetHandleFlags(nint handle, out uint flags) => GetHandleInformation(handle, out flags);

    public bool TryDisableInheritance(nint handle) => SetHandleInformation(handle, HandleFlagInherit, 0);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetHandleInformation(nint hObject, out uint lpdwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);
}
