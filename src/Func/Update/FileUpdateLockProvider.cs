// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IUpdateLockProvider" />
internal sealed class FileUpdateLockProvider : IUpdateLockProvider
{
    internal const string LockFileName = ".func-update.lock";

    public IDisposable Acquire(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        string lockPath = Path.Combine(installDirectory, LockFileName);
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException ex)
        {
            throw new GracefulException(
                $"Another Azure Functions CLI update is already running for '{installDirectory}'. Wait for it to finish and try again.",
                ex,
                isUserError: true);
        }
    }
}
