// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Host.Interop;

/// <summary>
/// Thin seam over the Win32 handle-flag APIs so that handle-inheritance logic can be unit
/// tested without touching real operating system handles.
/// </summary>
internal interface INativeHandleApi
{
    /// <summary>
    /// Reads the flags (including the inheritable flag) for the given handle.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the handle is valid and its flags were read; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryGetHandleFlags(nint handle, out uint flags);

    /// <summary>
    /// Clears the inheritable flag on the given handle so child processes cannot inherit it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the flag was cleared; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryDisableInheritance(nint handle);
}
