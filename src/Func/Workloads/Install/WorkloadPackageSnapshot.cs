// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadPackageSnapshot(string directory, string path, Stream guard) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose()
    {
        guard.Dispose();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary-file cleanup failure must not hide verification failure or cancellation.
        }
    }
}
