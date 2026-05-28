// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Thrown when the launcher cannot start an Azurite process. This represents
/// synchronous launch failure (executable missing, <c>docker run</c> rejected
/// the arguments before the container started, etc.). Once a process is
/// running, exit handling is the caller's responsibility.
/// </summary>
internal sealed class AzuriteLaunchException : Exception
{
    public AzuriteLaunchException(AzuriteLaunchMode mode, string fileName, string message)
        : base(message)
    {
        Mode = mode;
        FileName = fileName;
    }

    public AzuriteLaunchException(AzuriteLaunchMode mode, string fileName, string message, Exception innerException)
        : base(message, innerException)
    {
        Mode = mode;
        FileName = fileName;
    }

    public AzuriteLaunchMode Mode { get; }

    /// <summary>
    /// The executable the launcher attempted to start (e.g. <c>azurite</c> or
    /// <c>docker</c>).
    /// </summary>
    public string FileName { get; }
}
