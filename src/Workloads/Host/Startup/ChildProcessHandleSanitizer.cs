// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Interop;

namespace Azure.Functions.Cli.Workloads.Host.Startup;

/// <summary>
/// Strips the inheritable flag from every handle this process currently owns so that language
/// workers spawned later cannot inherit the host's redirected stdio pipe handles.
/// </summary>
/// <remarks>
/// The CLI launches the host with redirected stdin/stdout/stderr, so the host's pipe handles
/// (and the C runtime's duplicates of them) are marked inheritable. The Functions host spawns
/// each language worker with <c>bInheritHandles = TRUE</c> because it redirects the worker's
/// streams, which leaks those host pipe handles into the worker. A leaked, shared, synchronous
/// stdout pipe handle deadlocks CPython startup, which seeks stdout (<c>fflush</c>) during
/// interpreter initialization. Clearing inheritance before any worker launches prevents the
/// leak; .NET re-enables inheritance on the specific worker handles transiently while it starts
/// each worker, so worker stdio still works.
/// </remarks>
internal sealed class ChildProcessHandleSanitizer(INativeHandleApi nativeHandleApi)
{
    private const uint HandleFlagInherit = 0x1;

    // Win32 handles are multiples of four. The host owns only a handful of low-valued handles
    // at startup (before any worker launches), so scanning a small fixed window is sufficient.
    private const int MaxHandleValue = 0x10000;

    private readonly INativeHandleApi _nativeHandleApi = nativeHandleApi ?? throw new ArgumentNullException(nameof(nativeHandleApi));

    /// <summary>
    /// Disables handle inheritance on the host's currently open handles.
    /// </summary>
    /// <returns>
    /// The number of handles whose inheritable flag was cleared.
    /// </returns>
    public int DisableInheritanceOnOpenHandles()
    {
        int disabledCount = 0;
        for (int value = 4; value <= MaxHandleValue; value += 4)
        {
            nint handle = value;
            if (!_nativeHandleApi.TryGetHandleFlags(handle, out uint flags))
            {
                continue;
            }

            if ((flags & HandleFlagInherit) == 0)
            {
                continue;
            }

            if (_nativeHandleApi.TryDisableInheritance(handle))
            {
                disabledCount++;
            }
        }

        return disabledCount;
    }
}
