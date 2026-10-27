// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Serializes updates that target the same CLI installation.
/// </summary>
internal interface IUpdateLockProvider
{
    /// <summary>
    /// Acquires an exclusive lock for <paramref name="installDirectory"/>.
    /// </summary>
    /// <exception cref="Azure.Functions.Cli.Common.GracefulException">
    /// Another update already holds the installation lock.
    /// </exception>
    public IDisposable Acquire(string installDirectory);
}
